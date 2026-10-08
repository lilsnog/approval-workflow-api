using ApprovalWorkflow.Domain;
using Xunit;
using static ApprovalWorkflow.Tests.Workflows;

namespace ApprovalWorkflow.Tests;

public class ApprovalRequestTests
{
    [Fact]
    public void Small_amounts_skip_stages_above_their_threshold()
    {
        var request = ApprovalRequest.Submit(Expense(), "Taxi fares", 40_000m, "NGN", "ada", T0);

        Assert.Equal(new[] { "Line Manager", "Finance" }, request.Stages.Select(s => s.Name));
    }

    [Fact]
    public void Large_amounts_go_through_every_stage()
    {
        var request = ApprovalRequest.Submit(Expense(), "Servers", 9_000_000m, "NGN", "ada", T0);

        Assert.Equal(3, request.Stages.Count);
        Assert.Equal("CFO", request.Stages[^1].Name);
    }

    [Fact]
    public void Approving_every_stage_completes_the_request()
    {
        var request = ApprovalRequest.Submit(Expense(), "Taxi fares", 40_000m, "NGN", "ada", T0);

        request.Approve("bayo", "manager", "ok", T0.AddHours(1));
        Assert.Equal("Finance", request.CurrentStage!.Name);

        request.Approve("chidi", "finance", null, T0.AddHours(2));

        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.Null(request.CurrentStage);
        Assert.Equal(T0.AddHours(2), request.CompletedAt);
        Assert.Equal(
            new[] { AuditAction.Submitted, AuditAction.Approved, AuditAction.Approved, AuditAction.Completed },
            request.Audit.Select(a => a.Action));
    }

    [Fact]
    public void Only_the_stage_role_can_decide()
    {
        var request = ApprovalRequest.Submit(Expense(), "Taxi fares", 40_000m, "NGN", "ada", T0);

        Assert.Throws<ForbiddenException>(() => request.Approve("chidi", "finance", null, T0));
    }

    [Fact]
    public void Requester_cannot_approve_own_request()
    {
        var request = ApprovalRequest.Submit(Expense(), "Laptop", 900_000m, "NGN", "bayo", T0);

        Assert.Throws<ForbiddenException>(() => request.Approve("bayo", "manager", null, T0));
    }

    [Fact]
    public void Same_person_cannot_approve_two_stages()
    {
        var request = ApprovalRequest.Submit(Expense(), "Laptop", 900_000m, "NGN", "ada", T0);
        request.Approve("bayo", "manager", null, T0);

        // bayo somehow also holds the finance role
        Assert.Throws<ForbiddenException>(() => request.Approve("bayo", "finance", null, T0));
    }

    [Fact]
    public void Rejecting_requires_a_reason_and_ends_the_request()
    {
        var request = ApprovalRequest.Submit(Expense(), "Laptop", 900_000m, "NGN", "ada", T0);

        Assert.Throws<DomainException>(() => request.Reject("bayo", "manager", " ", T0));

        request.Reject("bayo", "manager", "Use the shared pool laptop", T0);

        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Throws<DomainException>(() => request.Approve("bayo", "manager", null, T0));
    }

    [Fact]
    public void Only_requester_can_withdraw()
    {
        var request = ApprovalRequest.Submit(Expense(), "Laptop", 900_000m, "NGN", "ada", T0);

        Assert.Throws<ForbiddenException>(() => request.Withdraw("bayo", T0));

        request.Withdraw("ada", T0);
        Assert.Equal(RequestStatus.Withdrawn, request.Status);
    }

    [Fact]
    public void Escalates_once_after_sla_and_resets_on_next_stage()
    {
        var request = ApprovalRequest.Submit(Expense(), "Laptop", 900_000m, "NGN", "ada", T0);

        Assert.False(request.TryEscalate(T0.AddHours(23)));
        Assert.True(request.TryEscalate(T0.AddHours(24)));
        Assert.False(request.TryEscalate(T0.AddHours(30)), "already escalated");

        request.Approve("bayo", "manager", null, T0.AddHours(31));
        Assert.False(request.CurrentStageEscalated);
        Assert.Equal(T0.AddHours(55), request.CurrentStageDueAt);
    }

    [Theory]
    [InlineData("", 1000, "NGN")]
    [InlineData("Laptop", 0, "NGN")]
    [InlineData("Laptop", 1000, "NAIRA")]
    public void Rejects_invalid_submissions(string title, double amount, string currency)
    {
        Assert.Throws<DomainException>(() => ApprovalRequest.Submit(Expense(), title, (decimal)amount, currency, "ada", T0));
    }
}
