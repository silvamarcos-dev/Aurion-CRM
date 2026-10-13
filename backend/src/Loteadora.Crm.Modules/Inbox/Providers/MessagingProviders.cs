using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Loteadora.Crm.Modules.Inbox.Domain;
using Microsoft.Extensions.Options;

namespace Loteadora.Crm.Modules.Inbox.Providers;

/// <summary>Mensagem a enviar, já normalizada (independente do provedor).</summary>
public sealed record OutgoingMessage(
    Channel Channel,
    string? AccessToken,
    string ToE164,
    MessageType Type,
    string Body,
    string? TemplateName,
    string? TemplateLanguage,
    IReadOnlyList<string> TemplateParameters);

public sealed record SendResult(bool Success, string? ExternalId, string? Error)
{
    public static SendResult Ok(string externalId) => new(true, externalId, null);

    public static SendResult Fail(string error) => new(false, null, error);
}

/// <summary>Camada anti-corrupção: a regra de negócio nunca conhece a API do provedor.</summary>
public interface IMessagingProvider
{
    ChannelProvider Provider { get; }

    Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct);
}

public sealed class MessagingProviderRegistry(IEnumerable<IMessagingProvider> providers)
{
    private readonly Dictionary<ChannelProvider, IMessagingProvider> _providers = providers.ToDictionary(p => p.Provider);

    public IMessagingProvider For(ChannelProvider provider) =>
        _providers.TryGetValue(provider, out var p) ? p : throw new InvalidOperationException($"Provedor não registrado: {provider}");
}

/// <summary>
/// WhatsApp simulado: "envia" na hora (gera um id no formato do WhatsApp) sem sair do servidor.
/// As mensagens do cliente são geradas pela tela "Simular cliente".
/// </summary>
public sealed class SimulatorProvider : IMessagingProvider
{
    public ChannelProvider Provider => ChannelProvider.Simulator;

    public Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct) =>
        Task.FromResult(SendResult.Ok($"sim.{Guid.CreateVersion7():N}"));
}

public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";

    /// <summary>App Secret do app da Meta — valida a assinatura X-Hub-Signature-256 dos webhooks.</summary>
    public string AppSecret { get; set; } = "";

    /// <summary>Token escolhido por você e informado no painel da Meta para a verificação do webhook (GET).</summary>
    public string VerifyToken { get; set; } = "";

    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com/";
    public string GraphApiVersion { get; set; } = "v21.0";
}

/// <summary>WhatsApp Business Platform — Cloud API da Meta.</summary>
public sealed class WhatsAppCloudProvider(HttpClient http, IOptions<WhatsAppOptions> options) : IMessagingProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ChannelProvider Provider => ChannelProvider.WhatsAppCloud;

    public async Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(message.AccessToken))
            return SendResult.Fail("Canal sem token de acesso configurado.");

        object payload = message.Type == MessageType.Template
            ? new
            {
                messaging_product = "whatsapp",
                to = message.ToE164.TrimStart('+'),
                type = "template",
                template = new
                {
                    name = message.TemplateName,
                    language = new { code = message.TemplateLanguage ?? "pt_BR" },
                    components = message.TemplateParameters.Count == 0
                        ? Array.Empty<object>()
                        : [new { type = "body", parameters = message.TemplateParameters.Select(p => new { type = "text", text = p }).ToArray() }],
                },
            }
            : new { messaging_product = "whatsapp", to = message.ToE164.TrimStart('+'), type = "text", text = new { body = message.Body } };

        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"{options.Value.GraphApiBaseUrl.TrimEnd('/')}/{options.Value.GraphApiVersion}/{message.Channel.ExternalAccountId}/messages"))
        {
            Content = JsonContent.Create(payload, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", message.AccessToken);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return SendResult.Fail(ExtractError(body) ?? $"Meta respondeu HTTP {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.TryGetProperty("messages", out var msgs) && msgs.GetArrayLength() > 0 ? msgs[0].GetProperty("id").GetString() : null;
        return id is null ? SendResult.Fail("Resposta da Meta sem id da mensagem.") : SendResult.Ok(id);
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// ───────────────────────── Webhook da Meta ─────────────────────────

public sealed record InboundMessageEvent(string PhoneNumberId, string FromE164, string? ProfileName, string ExternalId, MessageType Type, string Body, DateTimeOffset At);

public sealed record StatusEvent(string PhoneNumberId, string ExternalId, MessageStatus Status, DateTimeOffset At, string? Error);

public static class WhatsAppWebhook
{
    /// <summary>Valida X-Hub-Signature-256 = "sha256=" + HMAC-SHA256(corpo bruto, App Secret). Comparação em tempo constante.</summary>
    public static bool IsSignatureValid(string rawBody, string? signatureHeader, string appSecret)
    {
        if (string.IsNullOrEmpty(appSecret) || string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.Ordinal))
            return false;

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(rawBody));
        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signatureHeader["sha256=".Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>Extrai mensagens recebidas e atualizações de status do payload da Cloud API.</summary>
    public static (IReadOnlyList<InboundMessageEvent> Messages, IReadOnlyList<StatusEvent> Statuses) Parse(string rawBody)
    {
        var messages = new List<InboundMessageEvent>();
        var statuses = new List<StatusEvent>();
        using var doc = JsonDocument.Parse(rawBody);

        foreach (var entry in Array(doc.RootElement, "entry"))
        foreach (var change in Array(entry, "changes"))
        {
            if (!change.TryGetProperty("value", out var value))
                continue;
            var phoneNumberId = value.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("phone_number_id", out var pid) ? pid.GetString() : null;
            if (phoneNumberId is null)
                continue;

            var names = Array(value, "contacts").Where(c => c.TryGetProperty("wa_id", out _))
                .GroupBy(c => c.GetProperty("wa_id").GetString() ?? "")
                .ToDictionary(g => g.Key, g => g.First().TryGetProperty("profile", out var p) && p.TryGetProperty("name", out var n) ? n.GetString() : null);

            foreach (var m in Array(value, "messages"))
            {
                var from = m.GetProperty("from").GetString()!;
                var type = m.TryGetProperty("type", out var t) ? t.GetString() : "text";
                var (kind, body) = type switch
                {
                    "text" => (MessageType.Text, m.GetProperty("text").GetProperty("body").GetString() ?? ""),
                    "image" => (MessageType.Image, Caption(m, "image")),
                    "document" => (MessageType.Document, Caption(m, "document")),
                    "audio" => (MessageType.Audio, ""),
                    "video" => (MessageType.Video, Caption(m, "video")),
                    "button" => (MessageType.Text, m.GetProperty("button").GetProperty("text").GetString() ?? ""),
                    "interactive" => (MessageType.Text, InteractiveTitle(m)),
                    _ => (MessageType.Other, $"[{type}]"),
                };
                messages.Add(new InboundMessageEvent(phoneNumberId, "+" + from, names.GetValueOrDefault(from), m.GetProperty("id").GetString()!, kind, body, Timestamp(m)));
            }

            foreach (var s in Array(value, "statuses"))
            {
                var status = s.GetProperty("status").GetString() switch
                {
                    "sent" => MessageStatus.Sent,
                    "delivered" => MessageStatus.Delivered,
                    "read" => MessageStatus.Read,
                    "failed" => MessageStatus.Failed,
                    _ => (MessageStatus?)null,
                };
                if (status is null)
                    continue;
                var error = Array(s, "errors").Select(e => e.TryGetProperty("title", out var ti) ? ti.GetString() : null).FirstOrDefault(e => e is not null);
                statuses.Add(new StatusEvent(phoneNumberId, s.GetProperty("id").GetString()!, status.Value, Timestamp(s), error));
            }
        }

        return (messages, statuses);
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().ToList()
            : [];

    private static string Caption(JsonElement m, string kind) =>
        m.TryGetProperty(kind, out var media) && media.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";

    private static string InteractiveTitle(JsonElement m)
    {
        if (!m.TryGetProperty("interactive", out var i))
            return "";
        foreach (var key in new[] { "button_reply", "list_reply" })
        {
            if (i.TryGetProperty(key, out var r) && r.TryGetProperty("title", out var title))
                return title.GetString() ?? "";
        }
        return "";
    }

    private static DateTimeOffset Timestamp(JsonElement e) =>
        e.TryGetProperty("timestamp", out var ts) && long.TryParse(ts.GetString(), out var secs)
            ? DateTimeOffset.FromUnixTimeSeconds(secs)
            : DateTimeOffset.UtcNow;
}
