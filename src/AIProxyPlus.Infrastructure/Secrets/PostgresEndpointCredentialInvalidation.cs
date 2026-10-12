using System.Text.Json;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Infrastructure.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AIProxyPlus.Infrastructure.Secrets;

public sealed class NoOpEndpointCredentialInvalidationPublisher : IEndpointCredentialInvalidationPublisher
{
    public Task PublishAsync(
        EndpointCredentialInvalidation invalidation,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class PostgresEndpointCredentialInvalidationPublisher(
    string connectionString,
    AIProxyPlusMetrics metrics,
    ILogger<PostgresEndpointCredentialInvalidationPublisher> logger)
    : IEndpointCredentialInvalidationPublisher
{
    internal const string ChannelName = "aiproxyplus_endpoint_credentials";

    public async Task PublishAsync(
        EndpointCredentialInvalidation invalidation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection);
            command.Parameters.AddWithValue("channel", ChannelName);
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(invalidation));
            await command.ExecuteNonQueryAsync(cancellationToken);
            metrics.RecordCredentialInvalidationNotification("publish", "succeeded");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            metrics.RecordCredentialInvalidationNotification("publish", "failed");
            logger.LogWarning(exception, "Failed to publish credential invalidation for endpoint {EndpointId}", invalidation.EndpointId);
        }
    }
}

public sealed class PostgresEndpointCredentialInvalidationListener(
    string connectionString,
    IEndpointCredentialCache credentialCache,
    AIProxyPlusMetrics metrics,
    ILogger<PostgresEndpointCredentialInvalidationListener> logger)
    : BackgroundService
{
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Listening => _listening.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hasConnected = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                await using (var command = new NpgsqlCommand(
                    $"LISTEN {PostgresEndpointCredentialInvalidationPublisher.ChannelName}", connection))
                {
                    await command.ExecuteNonQueryAsync(stoppingToken);
                }

                if (hasConnected)
                {
                    metrics.RecordCredentialInvalidationNotification("listen", "reconnected");
                }
                hasConnected = true;
                _listening.TrySetResult();
                connection.Notification += (_, args) => HandleNotification(args.Payload);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                metrics.RecordCredentialInvalidationNotification("listen", "failed");
                logger.LogWarning(exception, "Credential invalidation listener disconnected; reconnecting");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private void HandleNotification(string payload)
    {
        try
        {
            var invalidation = JsonSerializer.Deserialize<EndpointCredentialInvalidation>(payload)
                ?? throw new JsonException("Credential invalidation payload was empty.");
            foreach (var secretRef in invalidation.SecretRefs)
            {
                credentialCache.Invalidate(secretRef);
            }
            metrics.RecordCredentialInvalidationNotification("receive", "succeeded");
        }
        catch (JsonException exception)
        {
            metrics.RecordCredentialInvalidationNotification("receive", "failed");
            logger.LogWarning(exception, "Ignored an invalid credential invalidation payload");
        }
    }
}