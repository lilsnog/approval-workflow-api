using ApprovalWorkflow.Application;
using ApprovalWorkflow.Domain;
using ApprovalWorkflow.Infrastructure;
using Xunit;
using static ApprovalWorkflow.Tests.Workflows;

namespace ApprovalWorkflow.Tests;

public class ApprovalServiceTests
{
    private readonly ManualTimeProvider _clock = new(T0);
    private readonly RecordingNotifier _notifier = new();
    private readonly InMemoryRequestRepository _repo = new();
    private readonly ApprovalService _service;

    private static readonly CurrentUser Ada = new("ada", "staff");
    private static readonly CurrentUser Bayo = new("bayo", "manager");
    private static readonly CurrentUser Chidi = new("chidi", "finance");

    public ApprovalServiceTests()
    {
        var catalog = new ConfigWorkflowCatalog(new WorkflowOptions
        {
            Workflows = new()
            {
                ["expense"] =
                [
                    new() { Name = "Line Manager", Role = "manager", SlaHours = 24, EscalateToRole = "head-of-dept" },
                    new() { Name = "Finance", Role = "finance", SlaHours = 24 },
                ],
            },
        });

        _service = new ApprovalService(_repo, catalog, _notifier, _clock);
    }

    [Fact]
    public async Task Submitting_notifies_the_first_approver()
    {
        await _service.SubmitAsync(new SubmitRequest("expense", "Laptop", 900_000m, "NGN"), Ada, default);

        Assert.Equal(new[] { "awaiting:Line Manager" }, _notifier.Events);
    }

    [Fact]
    public async Task Unknown_workflow_is_rejected()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SubmitAsync(new SubmitRequest("travel", "Flight", 1m, "NGN"), Ada, default));
    }

    [Fact]
    public async Task Full_approval_flow_notifies_each_step()
    {
        var r = await _service.SubmitAsync(new SubmitRequest("expense", "Laptop", 900_000m, "NGN"), Ada, default);

        await _service.ApproveAsync(r.Id, new Decision("ok"), Bayo, default);
        await _service.ApproveAsync(r.Id, new Decision(null), Chidi, default);

        Assert.Equal(
            new[] { "awaiting:Line Manager", "awaiting:Finance", "completed:Approved" },
            _notifier.Events);
    }

    [Fact]
    public async Task Missing_request_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.ApproveAsync(Guid.NewGuid(), new Decision(null), Bayo, default));
    }

    [Fact]
    public async Task Sla_monitor_escalates_overdue_requests_exactly_once()
    {
        var late = await _service.SubmitAsync(new SubmitRequest("expense", "Late one", 1_000m, "NGN"), Ada, default);
        _clock.Advance(TimeSpan.FromHours(20));
        await _service.SubmitAsync(new SubmitRequest("expense", "Fresh one", 1_000m, "NGN"), Ada, default);
        _clock.Advance(TimeSpan.FromHours(5)); // late one is now 25h old, fresh one 5h

        Assert.Equal(1, await _service.EscalateOverdueAsync(default));
        Assert.Equal(0, await _service.EscalateOverdueAsync(default));

        var stored = await _repo.GetAsync(late.Id, default);
        Assert.True(stored!.CurrentStageEscalated);
        Assert.Contains("breached:Line Manager", _notifier.Events);
    }

    [Fact]
    public async Task Concurrent_approvals_cannot_double_advance_a_stage()
    {
        var r = await _service.SubmitAsync(new SubmitRequest("expense", "Laptop", 900_000m, "NGN"), Ada, default);
        var second = new CurrentUser("efe", "manager");

        var results = await Task.WhenAll(
            Capture(() => _service.ApproveAsync(r.Id, new Decision(null), Bayo, default)),
            Capture(() => _service.ApproveAsync(r.Id, new Decision(null), second, default)));

        // Exactly one manager approval lands; the other sees the request already at Finance.
        Assert.Equal(1, results.Count(e => e is null));
        Assert.Equal(1, results.Count(e => e is ForbiddenException));
        Assert.Equal("Finance", (await _repo.GetAsync(r.Id, default))!.CurrentStage!.Name);
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }
}
