using ApprovalWorkflow.Application;
using ApprovalWorkflow.Domain;

namespace ApprovalWorkflow.Infrastructure;

/// <summary>
/// Writes structured log events. Replace with an email / Teams / webhook
/// implementation of <see cref="INotifier"/> to alert approvers for real.
/// </summary>
public sealed partial class LoggingNotifier(ILogger<LoggingNotifier> logger) : INotifier
{
    private readonly ILogger _logger = logger;

    public Task StageAwaitingAsync(ApprovalRequest request, CancellationToken ct)
    {
        LogAwaiting(request.Id, request.Title, request.CurrentStage?.Name, request.CurrentStage?.Role, request.CurrentStageDueAt);
        return Task.CompletedTask;
    }

    public Task SlaBreachedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var stage = request.CurrentStage!;
        LogBreached(request.Id, request.Title, stage.Name, stage.EscalateToRole ?? stage.Role);
        return Task.CompletedTask;
    }

    public Task CompletedAsync(ApprovalRequest request, CancellationToken ct)
    {
        LogCompleted(request.Id, request.Title, request.Status);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request {Id} '{Title}' awaiting {Stage} ({Role}), due {DueAt}")]
    private partial void LogAwaiting(Guid id, string title, string? stage, string? role, DateTimeOffset? dueAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SLA breached on request {Id} '{Title}' at stage {Stage}; escalating to {Role}")]
    private partial void LogBreached(Guid id, string title, string stage, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request {Id} '{Title}' completed with status {Status}")]
    private partial void LogCompleted(Guid id, string title, RequestStatus status);
}
