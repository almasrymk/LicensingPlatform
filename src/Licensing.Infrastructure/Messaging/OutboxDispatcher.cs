using System.Text.Json;
using Licensing.Application.Events;
using Licensing.Infrastructure.Identity;
using Licensing.Infrastructure.Persistence;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Licensing.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
    /// <summary>After this many failed attempts a message is dead-lettered and needs manual attention.</summary>
    public int MaxAttempts { get; set; } = 8;
}

/// <summary>
/// Publishes outbox messages to their in-process handlers with retry and exponential backoff. Because messages are written
/// in the business transaction and only marked processed after the handlers ran, nothing is lost if the worker stops;
/// the inbox table prevents a handler from running twice for the same event.
/// </summary>
public sealed class OutboxProcessor(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, TimeProvider clock, ILogger<OutboxProcessor> logger)
{
    private static readonly Dictionary<string, Type> EventTypes = typeof(Domain.Tenants.Tenant).Assembly.GetTypes()
        .Where(t => typeof(IDomainEvent).IsAssignableFrom(t) && !t.IsAbstract)
        .ToDictionary(t => t.Name);

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().RunAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var o = options.Value;

        var batch = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && !m.DeadLettered && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.OccurredAt).Take(o.BatchSize).ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await DispatchAsync(message, ct);
                message.ProcessedAt = clock.GetUtcNow();
                message.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                message.DeadLettered = message.Attempts >= o.MaxAttempts;
                message.NextAttemptAt = clock.GetUtcNow().AddSeconds(Math.Min(3600, Math.Pow(2, message.Attempts)));
                logger.Log(message.DeadLettered ? LogLevel.Error : LogLevel.Warning, ex,
                    "Outbox message {MessageId} ({Type}) failed, attempt {Attempt}{DeadLetter}",
                    message.Id, message.Type, message.Attempts, message.DeadLettered ? " - dead-lettered" : "");
            }
            await db.SaveChangesAsync(ct);
        }
        return batch.Count;
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken ct)
    {
        if (!EventTypes.TryGetValue(message.Type, out var eventType))
            throw new InvalidOperationException($"Unknown event type '{message.Type}'.");
        var @event = JsonSerializer.Deserialize(message.Payload, eventType)!;
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

        // Each handler runs in its own scope and unit of work, recorded in the inbox in the same transaction.
        using var probe = scopes.CreateScope();
        var handlerCount = probe.ServiceProvider.GetServices(handlerType).Count();
        for (var i = 0; i < handlerCount; i++)
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().RunAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = scope.ServiceProvider.GetServices(handlerType).ElementAt(i)!;
            var name = handler.GetType().FullName!;

            if (await db.InboxMessages.AnyAsync(x => x.MessageId == message.Id && x.Handler == name, ct)) continue;

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await (Task)handlerType.GetMethod("HandleAsync")!.Invoke(handler, [@event, ct])!;
            db.InboxMessages.Add(new InboxMessage { MessageId = message.Id, Handler = name, ProcessedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}

public sealed class OutboxDispatcher(OutboxProcessor processor, IOptions<OutboxOptions> options, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await processor.ProcessBatchAsync(stoppingToken);
                if (processed > 0) continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatcher iteration failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken);
        }
    }
}
