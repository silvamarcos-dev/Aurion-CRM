using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Inbox.Application;
using Loteadora.Crm.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loteadora.Crm.Tests.Integration;

/// <summary>Fase 3 — atendimento: simulador, envio, janela de 24h, idempotência, webhook da Meta, escopo e mascaramento.</summary>
[Collection(ApiCollection.Name)]
public sealed class InboxTests(CrmApiFactory factory)
{
    private static string Phone() => "119" + Random.Shared.Next(10_000_000, 99_999_999);

    private async Task<Guid> CreateSimulatorChannelAsync(HttpClient admin, Guid ownerUserId)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/channels",
            new { provider = "Simulator", name = "Sim " + Guid.NewGuid().ToString("N")[..6], phoneNumber = "11900000000", defaultOwnerUserId = ownerUserId }, TestHelpers.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> SimulateAsync(HttpClient admin, Guid channelId, string phone, string text, string? name = null)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/simulator/inbound", new { channelId, fromPhone = phone, profileName = name, text }, TestHelpers.Json);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> SendTextAsync(HttpClient client, Guid conversationId, string text, string? clientId = null) =>
        client.PostAsJsonAsync($"/api/v1/conversations/{conversationId}/messages", new { clientMessageId = clientId ?? Guid.NewGuid().ToString("N"), text }, TestHelpers.Json);

    [Fact]
    public async Task Inbound_from_new_number_creates_lead_and_conversation_for_default_owner_only()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var owner = factory.OrgA.UserIdsByEmail["corretor.norte1@a.test"];
        var channelId = await CreateSimulatorChannelAsync(admin, owner);

        var result = await SimulateAsync(admin, channelId, Phone(), "Olá, quero saber do loteamento", "Renata Alves");
        Assert.True(result.GetProperty("leadCreated").GetBoolean());
        var conversationId = result.GetProperty("conversationId").GetGuid();

        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var mine = await agent.GetFromJsonAsync<JsonElement>("/api/v1/conversations?filter=Mine");
        var conv = mine.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == conversationId);
        Assert.Equal("Renata Alves", conv.GetProperty("contactName").GetString());
        Assert.Equal(1, conv.GetProperty("unreadCount").GetInt32());

        var lead = await agent.GetLeadAsync(result.GetProperty("leadId").GetGuid());
        Assert.Equal("WhatsApp", lead.Source);
        Assert.NotNull(lead.LastContactAt);

        var other = await factory.LoginAsync("corretor.sul@a.test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri($"/api/v1/conversations/{conversationId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendTextAsync(other, conversationId, "intrometido")).StatusCode);
    }

    [Fact]
    public async Task Inbound_from_existing_contact_reuses_open_lead()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var phone = Phone();
        var created = await agent.PostAsJsonAsync("/api/v1/leads", new { fullName = "Cliente Existente", phone }, TestHelpers.Json);
        var leadId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var channelId = await CreateSimulatorChannelAsync(admin, factory.OrgA.UserIdsByEmail["corretor.norte2@a.test"]);

        var first = await SimulateAsync(admin, channelId, phone, "Oi");
        var second = await SimulateAsync(admin, channelId, phone, "Tudo bem?");

        Assert.False(first.GetProperty("leadCreated").GetBoolean());
        Assert.Equal(leadId, first.GetProperty("leadId").GetGuid());
        Assert.Equal(first.GetProperty("conversationId").GetGuid(), second.GetProperty("conversationId").GetGuid());
    }

    [Fact]
    public async Task Owner_replies_idempotently_and_window_rule_requires_template()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var channelId = await CreateSimulatorChannelAsync(admin, factory.OrgA.UserIdsByEmail["corretor.norte1@a.test"]);
        var templateResponse = await admin.PostAsJsonAsync($"/api/v1/channels/{channelId}/templates",
            new { name = "retomar_contato", language = "pt_BR", body = "Olá {{1}}, aqui é {{2}}!" }, TestHelpers.Json);
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var conversationId = (await SimulateAsync(admin, channelId, Phone(), "Oi!")).GetProperty("conversationId").GetGuid();

        var clientId = Guid.NewGuid().ToString("N");
        var first = await (await SendTextAsync(agent, conversationId, "Olá! Como posso ajudar?", clientId)).Content.ReadFromJsonAsync<JsonElement>();
        var again = await (await SendTextAsync(agent, conversationId, "Olá! Como posso ajudar?", clientId)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        Assert.Equal("Delivered", first.GetProperty("status").GetString());

        var detail = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversationId}");
        Assert.Equal(2, detail.GetProperty("messages").GetArrayLength());
        Assert.Equal(0, detail.GetProperty("conversation").GetProperty("unreadCount").GetInt32());

        // Cliente sumiu há mais de 24h: texto livre bloqueado, template permitido.
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE inbox.conversations SET last_inbound_at = now() - interval '25 hours' WHERE id = {conversationId}"));

        var outside = await SendTextAsync(agent, conversationId, "Oi, sumiu?");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outside.StatusCode);
        Assert.Equal("message.outside_window", (await outside.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var missingParams = await agent.PostAsJsonAsync($"/api/v1/conversations/{conversationId}/messages",
            new { clientMessageId = Guid.NewGuid().ToString("N"), templateId, templateParameters = new[] { "Ana" } }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingParams.StatusCode);

        var template = await agent.PostAsJsonAsync($"/api/v1/conversations/{conversationId}/messages",
            new { clientMessageId = Guid.NewGuid().ToString("N"), templateId, templateParameters = new[] { "Ana", "Corretor" } }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal("Olá Ana, aqui é Corretor!", (await template.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("body").GetString());
    }

    [Fact]
    public async Task Meta_webhook_requires_valid_signature_and_is_processed_idempotently()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var phoneNumberId = "PN" + Guid.NewGuid().ToString("N")[..12];
        var create = await admin.PostAsJsonAsync("/api/v1/channels", new
        {
            provider = "WhatsAppCloud", name = "Número oficial", phoneNumber = "11955550000", phoneNumberId,
            defaultOwnerUserId = factory.OrgA.UserIdsByEmail["corretor.norte1@a.test"], accessToken = "EAAG-token-de-teste",
        }, TestHelpers.Json);
        create.EnsureSuccessStatusCode();

        // Token nunca volta na API.
        var channels = await admin.GetStringAsync(new Uri("/api/v1/channels", UriKind.Relative));
        Assert.DoesNotContain("EAAG-token-de-teste", channels, StringComparison.Ordinal);
        Assert.Contains("\"hasCredentials\":true", channels, StringComparison.Ordinal);

        var from = "55" + Phone();
        var wamid = "wamid.IN" + Guid.NewGuid().ToString("N");
        var payload = MetaMessagePayload(phoneNumberId, from, "Carlos Lima", wamid, "Quero agendar uma visita");
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWebhookAsync(anonymous, payload, "sha256=" + new string('0', 64))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWebhookAsync(anonymous, payload, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(anonymous, payload, Sign(payload))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(anonymous, payload, Sign(payload))).StatusCode); // reentrega

        await ProcessWebhooksAsync();

        var list = await agent.GetFromJsonAsync<JsonElement>("/api/v1/conversations?search=Carlos Lima");
        var conversationId = list.EnumerateArray().Single().GetProperty("id").GetGuid();
        var detail = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversationId}");
        Assert.Single(detail.GetProperty("messages").EnumerateArray());
        Assert.Equal("Quero agendar uma visita", detail.GetProperty("messages")[0].GetProperty("body").GetString());

        // Envio pela Cloud API (falsa): token decifrado vai no Bearer, rota usa o Phone Number ID.
        var sent = await (await SendTextAsync(agent, conversationId, "Claro! Qual dia fica melhor?")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sent", sent.GetProperty("status").GetString());
        var request = factory.FakeGraphApi.Requests.Last(r => r.Uri.AbsolutePath.Contains(phoneNumberId, StringComparison.Ordinal));
        Assert.Equal("Bearer EAAG-token-de-teste", request.Authorization);
        Assert.Contains($"\"to\":\"{from}\"", request.Body, StringComparison.Ordinal);

        // Status "read" chega por webhook e só avança o estado.
        var externalId = await factory.WithDbAsync(db => db.Messages.IgnoreQueryFilters().Where(m => m.Id == sent.GetProperty("id").GetGuid()).Select(m => m.ExternalId).SingleAsync());
        var status = MetaStatusPayload(phoneNumberId, externalId!, "read");
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(anonymous, status, Sign(status))).StatusCode);
        await ProcessWebhooksAsync();
        var after = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversationId}");
        Assert.Equal("Read", after.GetProperty("messages").EnumerateArray().Last().GetProperty("status").GetString());
    }

    [Fact]
    public async Task Webhook_for_unknown_phone_number_id_is_ignored()
    {
        var payload = MetaMessagePayload("PN-NAO-CADASTRADO", "5511999990000", "Ninguém", "wamid.X" + Guid.NewGuid().ToString("N"), "oi");
        var response = await PostWebhookAsync(factory.CreateClient(), payload, Sign(payload));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ProcessWebhooksAsync();

        var created = await factory.WithDbAsync(db => db.Contacts.IgnoreQueryFilters().AnyAsync(c => c.PhoneE164 == "+5511999990000"));
        Assert.False(created);
    }

    [Fact]
    public async Task Webhook_verification_checks_token()
    {
        var client = factory.CreateClient();
        var ok = await client.GetAsync(new Uri($"/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={factory.WhatsAppVerifyToken}&hub.challenge=12345", UriKind.Relative));
        var bad = await client.GetAsync(new Uri("/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=errado&hub.challenge=12345", UriKind.Relative));
        Assert.Equal("12345", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
    }

    [Fact]
    public async Task Guards_isolation_simulator_permissions_and_visitor_masking()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var adminB = await factory.LoginAsync("admin@b.test");
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var channelId = await CreateSimulatorChannelAsync(admin, factory.OrgA.UserIdsByEmail["corretor.norte1@a.test"]);
        var phone = Phone();
        var conversationId = (await SimulateAsync(admin, channelId, phone, "Meu CPF é 123.456.789-00", "Beatriz Moura")).GetProperty("conversationId").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync(new Uri($"/api/v1/conversations/{conversationId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("/api/v1/simulator/inbound", new { channelId, fromPhone = Phone(), text = "x" }, TestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("/api/v1/channels", new { provider = "Simulator", name = "x", phoneNumber = "11900000001", defaultOwnerUserId = Guid.NewGuid() }, TestHelpers.Json)).StatusCode);

        var link = await admin.PostAsJsonAsync("/api/v1/access-links", new { label = "Visitante inbox", role = SystemRoles.Visitor, expiresInDays = 1 }, TestHelpers.Json);
        var token = (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var visitor = factory.CreateClient();
        var tokens = await (await visitor.PostAsJsonAsync("/api/v1/auth/access-link", new { token })).Content.ReadFromJsonAsync<TokenPair>();
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var detail = await visitor.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversationId}");
        Assert.True(detail.GetProperty("contentHidden").GetBoolean());
        Assert.False(detail.GetProperty("canReply").GetBoolean());
        Assert.Equal("Beatriz M.", detail.GetProperty("conversation").GetProperty("contactName").GetString());
        Assert.DoesNotContain("123.456", detail.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendTextAsync(visitor, conversationId, "oi")).StatusCode);
    }

    // ───────────────────────── helpers ─────────────────────────

    private async Task ProcessWebhooksAsync()
    {
        var processor = ActivatorUtilities.CreateInstance<WebhookProcessor>(factory.Services);
        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }
    }

    private string Sign(string payload) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(factory.WhatsAppAppSecret), Encoding.UTF8.GetBytes(payload)));

    private static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/whatsapp", UriKind.Relative))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (signature is not null)
            request.Headers.Add("X-Hub-Signature-256", signature);
        return client.SendAsync(request);
    }

    private static string MetaMessagePayload(string phoneNumberId, string from, string name, string wamid, string text) => JsonSerializer.Serialize(new
    {
        @object = "whatsapp_business_account",
        entry = new[]
        {
            new
            {
                id = "WABA",
                changes = new[]
                {
                    new
                    {
                        field = "messages",
                        value = new
                        {
                            messaging_product = "whatsapp",
                            metadata = new { display_phone_number = "5511955550000", phone_number_id = phoneNumberId },
                            contacts = new[] { new { profile = new { name }, wa_id = from } },
                            messages = new[] { new { from, id = wamid, timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), type = "text", text = new { body = text } } },
                        },
                    },
                },
            },
        },
    });

    private static string MetaStatusPayload(string phoneNumberId, string wamid, string status) => JsonSerializer.Serialize(new
    {
        @object = "whatsapp_business_account",
        entry = new[]
        {
            new
            {
                id = "WABA",
                changes = new[]
                {
                    new
                    {
                        field = "messages",
                        value = new
                        {
                            messaging_product = "whatsapp",
                            metadata = new { display_phone_number = "5511955550000", phone_number_id = phoneNumberId },
                            statuses = new[] { new { id = wamid, status, timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), recipient_id = "5511" } },
                        },
                    },
                },
            },
        },
    });
}
