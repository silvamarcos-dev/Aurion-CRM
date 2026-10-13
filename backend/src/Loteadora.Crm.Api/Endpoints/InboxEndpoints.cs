using Loteadora.Crm.Modules.Inbox.Application;
using Loteadora.Crm.Modules.Inbox.Domain;
using Microsoft.AspNetCore.RateLimiting;

namespace Loteadora.Crm.Api.Endpoints;

public static class InboxEndpoints
{
    public const string WebhookRateLimitPolicy = "webhooks";

    public static void MapInboxEndpoints(this RouteGroupBuilder api)
    {
        var conversations = api.MapGroup("/conversations").WithTags("Atendimento");
        conversations.MapGet("/", (ConversationFilter? filter, ConversationState? state, string? search, int? limit, ConversationService service, CancellationToken ct) =>
            service.ListAsync(filter ?? ConversationFilter.All, state, search, limit ?? 100, ct));
        conversations.MapGet("/counters", (ConversationService service, CancellationToken ct) => service.GetCountersAsync(ct));
        conversations.MapGet("/{id:guid}", (Guid id, ConversationService service, CancellationToken ct) => service.GetAsync(id, ct));
        conversations.MapPost("/{id:guid}/open", async (Guid id, ConversationService service, CancellationToken ct) =>
        {
            await service.OpenAsync(id, ct);
            return Results.NoContent();
        });
        conversations.MapPost("/{id:guid}/read", async (Guid id, ConversationService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(id, ct);
            return Results.NoContent();
        });
        conversations.MapPost("/{id:guid}/messages", (Guid id, SendMessageCommand cmd, ConversationService service, CancellationToken ct) => service.SendAsync(id, cmd, ct));
        conversations.MapPost("/{id:guid}/messages/{messageId:guid}/retry", (Guid id, Guid messageId, ConversationService service, CancellationToken ct) =>
            service.RetryAsync(id, messageId, ct));
        conversations.MapPost("/{id:guid}/assign", async (Guid id, AssignConversationCommand cmd, ConversationService service, CancellationToken ct) =>
        {
            await service.AssignAsync(id, cmd, ct);
            return Results.NoContent();
        });
        conversations.MapPost("/{id:guid}/close", async (Guid id, ConversationService service, CancellationToken ct) =>
        {
            await service.SetStateAsync(id, ConversationState.Closed, ct);
            return Results.NoContent();
        });
        conversations.MapPost("/{id:guid}/reopen", async (Guid id, ConversationService service, CancellationToken ct) =>
        {
            await service.SetStateAsync(id, ConversationState.Open, ct);
            return Results.NoContent();
        });
        api.MapGet("/leads/{id:guid}/conversations", (Guid id, ConversationService service, CancellationToken ct) => service.ListForLeadAsync(id, ct)).WithTags("Atendimento");

        var channels = api.MapGroup("/channels").WithTags("Canais");
        channels.MapGet("/", (ChannelService service, CancellationToken ct) => service.ListAsync(ct));
        channels.MapGet("/webhook-setup", (ChannelService service) => service.GetWebhookSetup());
        channels.MapPost("/", async (SaveChannelCommand cmd, ChannelService service, CancellationToken ct) => Results.Ok(new CreatedResponse(await service.CreateAsync(cmd, ct))));
        channels.MapPut("/{id:guid}", async (Guid id, SaveChannelCommand cmd, ChannelService service, CancellationToken ct) =>
        {
            await service.UpdateAsync(id, cmd, ct);
            return Results.NoContent();
        });
        channels.MapPost("/{id:guid}/enable", async (Guid id, ChannelService service, CancellationToken ct) =>
        {
            await service.SetEnabledAsync(id, true, ct);
            return Results.NoContent();
        });
        channels.MapPost("/{id:guid}/disable", async (Guid id, ChannelService service, CancellationToken ct) =>
        {
            await service.SetEnabledAsync(id, false, ct);
            return Results.NoContent();
        });
        channels.MapGet("/{id:guid}/templates", (Guid id, ChannelService service, CancellationToken ct) => service.ListTemplatesAsync(id, ct));
        channels.MapPost("/{id:guid}/templates", async (Guid id, SaveTemplateCommand cmd, ChannelService service, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await service.CreateTemplateAsync(id, cmd, ct))));
        channels.MapDelete("/{id:guid}/templates/{templateId:guid}", async (Guid id, Guid templateId, ChannelService service, CancellationToken ct) =>
        {
            await service.DeleteTemplateAsync(id, templateId, ct);
            return Results.NoContent();
        });

        api.MapPost("/simulator/inbound", (SimulateInboundCommand cmd, SimulatorService service, CancellationToken ct) => service.InboundAsync(cmd, ct)).WithTags("Canais");
    }

    /// <summary>
    /// Webhook da Meta (público). Fica FORA de /api/v1 e sem autenticação de usuário:
    /// a autenticidade é garantida pela assinatura HMAC (X-Hub-Signature-256) com o App Secret.
    /// </summary>
    public static void MapWhatsAppWebhook(this WebApplication app)
    {
        var hooks = app.MapGroup("/webhooks").AllowAnonymous().RequireRateLimiting(WebhookRateLimitPolicy).WithTags("Webhooks");

        hooks.MapGet("/whatsapp", (HttpRequest request, WebhookReceiver receiver) =>
        {
            var mode = request.Query["hub.mode"].ToString();
            var token = request.Query["hub.verify_token"].ToString();
            var challenge = request.Query["hub.challenge"].ToString();
            return receiver.VerifySubscription(mode, token) && challenge.Length is > 0 and < 256
                ? Results.Text(challenge, "text/plain")
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        });

        hooks.MapPost("/whatsapp", async (HttpRequest request, WebhookReceiver receiver, CancellationToken ct) =>
        {
            if (request.ContentLength is > 1_000_000)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            using var reader = new StreamReader(request.Body);
            var raw = await reader.ReadToEndAsync(ct);
            var outcome = await receiver.ReceiveAsync(raw, request.Headers["X-Hub-Signature-256"].ToString(), ct);
            return outcome == WebhookReceiver.Outcome.InvalidSignature ? Results.Unauthorized() : Results.Ok();
        });
    }
}
