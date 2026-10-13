using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Loteadora.Crm.Tests.Infrastructure;

/// <summary>Imita a Graph API da Meta para envios de mensagem; registra as requisições recebidas.</summary>
public sealed class FakeGraphApiHandler : HttpMessageHandler
{
    public sealed record Captured(Uri Uri, string? Authorization, string Body);

    public ConcurrentQueue<Captured> Requests { get; } = new();

    /// <summary>Quando true, responde erro como a Meta faria.</summary>
    public bool Fail { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new Captured(request.RequestUri!, request.Headers.Authorization?.ToString(), body));

        var json = Fail
            ? """{"error":{"message":"(#131047) Re-engagement message","code":131047}}"""
            : $$"""{"messaging_product":"whatsapp","contacts":[{"input":"x","wa_id":"x"}],"messages":[{"id":"wamid.TEST{{Guid.NewGuid():N}}"}]}""";
        return new HttpResponseMessage(Fail ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
