using System.Text.Json;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loteadora.Crm.Modules.Outbox;

/// <summary>Handler de evento de domínio. Entrega "at-least-once": handlers DEVEM ser idempotentes.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

/// <summary>
/// Despacha mensagens do outbox. Usa FOR UPDATE SKIP LOCKED para permitir várias instâncias.
/// Cada mensagem é tratada em um escopo próprio com ator de sistema do tenant da mensagem.
/// (Fase de automações: substituível por Wolverine/RabbitMQ sem alterar os handlers.)
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, Type> EventTypes = typeof(OutboxDispatcher).Assembly.GetTypes()
        .Where(t => typeof(IDomainEvent).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
        .ToDictionary(t => t.FullName!, t => t);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                if (processed == 0)
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo do outbox");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = clock.GetUtcNow();
        var batch = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM messaging.outbox_messages
                WHERE processed_at IS NULL AND attempts < {OutboxMessage.MaxAttempts} AND available_at <= {now}
                ORDER BY occurred_at
                LIMIT 50
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await DispatchAsync(message, ct);
                message.MarkProcessed(clock.GetUtcNow());
            }
            catch (Exception ex)
            {
                message.MarkFailed(ex.Message, clock.GetUtcNow());
                logger.LogWarning(ex, "Mensagem de outbox {MessageId} ({Type}) falhou (tentativa {Attempt})", message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken ct)
    {
        if (!EventTypes.TryGetValue(message.Type, out var eventType))
            throw new InvalidOperationException($"Tipo de evento desconhecido: {message.Type}");

        var domainEvent = JsonSerializer.Deserialize(message.Payload, eventType, Json)!;

        await using var handlerScope = scopes.CreateAsyncScope();
        handlerScope.ServiceProvider.GetRequiredService<RequestContext>().InitializeSystem(message.OrganizationId);

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        foreach (var handler in handlerScope.ServiceProvider.GetServices(handlerType))
        {
            var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            await (Task)method.Invoke(handler, [domainEvent, ct])!;
        }
    }
}
