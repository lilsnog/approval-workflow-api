using ApprovalWorkflow.Application;

namespace ApprovalWorkflow.Infrastructure;

public sealed class SlaOptions
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Background job that periodically escalates requests whose current stage
/// has been waiting longer than its SLA.
/// </summary>
public sealed partial class SlaEscalationService(
    IServiceScopeFactory scopes,
    SlaOptions options,
    TimeProvider clock,
    ILogger<SlaEscalationService> logger) : BackgroundService
{
    private readonly ILogger _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.CheckInterval, clock);

        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ApprovalService>();
                var count = await service.EscalateOverdueAsync(stoppingToken);

                if (count > 0)
                {
                    LogEscalated(count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let one bad run kill the monitor.
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SLA monitor escalated {Count} request(s)")]
    private partial void LogEscalated(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "SLA monitor run failed")]
    private partial void LogFailed(Exception ex);
}
