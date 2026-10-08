using System.Security.Claims;
using ApprovalWorkflow.Application;
using ApprovalWorkflow.Domain;
using ApprovalWorkflow.Infrastructure;

namespace ApprovalWorkflow.Endpoints;

public sealed record StageDto(string Name, string Role, double SlaHours);

public sealed record RequestDto(
    Guid Id,
    string Workflow,
    string Title,
    decimal Amount,
    string Currency,
    string RequestedBy,
    string Status,
    DateTimeOffset SubmittedAt,
    StageDto? CurrentStage,
    DateTimeOffset? CurrentStageDueAt,
    bool Overdue,
    IReadOnlyList<StageDto> Route,
    DateTimeOffset? CompletedAt)
{
    public static RequestDto From(ApprovalRequest r, DateTimeOffset now) => new(
        r.Id,
        r.WorkflowKey,
        r.Title,
        r.Amount,
        r.Currency,
        r.RequestedBy,
        r.Status.ToString(),
        r.SubmittedAt,
        r.CurrentStage is { } s ? new StageDto(s.Name, s.Role, s.Sla.TotalHours) : null,
        r.CurrentStageDueAt,
        r.CurrentStageDueAt is { } due && now > due,
        r.Stages.Select(s => new StageDto(s.Name, s.Role, s.Sla.TotalHours)).ToList(),
        r.CompletedAt);
}

public static class RequestEndpoints
{
    public static void MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/requests").RequireAuthorization();

        group.MapPost("/", async (SubmitRequest body, ClaimsPrincipal user, ApprovalService service, TimeProvider clock, CancellationToken ct) =>
        {
            var request = await service.SubmitAsync(body, user.ToCurrentUser(), ct);
            return Results.Created($"/api/requests/{request.Id}", RequestDto.From(request, clock.GetUtcNow()));
        });

        group.MapGet("/", async (RequestStatus? status, bool? mine, bool? awaitingMe, ClaimsPrincipal principal, IRequestRepository repo, TimeProvider clock, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUser();
            var query = new RequestQuery(
                status,
                AwaitingRole: awaitingMe == true ? user.Role : null,
                RequestedBy: mine == true ? user.Name : null);

            var now = clock.GetUtcNow();
            return (await repo.ListAsync(query, ct)).Select(r => RequestDto.From(r, now));
        });

        group.MapGet("/{id:guid}", async (Guid id, IRequestRepository repo, TimeProvider clock, CancellationToken ct) =>
            await repo.GetAsync(id, ct) is { } r
                ? Results.Ok(RequestDto.From(r, clock.GetUtcNow()))
                : Results.NotFound());

        group.MapGet("/{id:guid}/audit", async (Guid id, IRequestRepository repo, CancellationToken ct) =>
            await repo.GetAsync(id, ct) is { } r
                ? Results.Ok(r.Audit.Select(a => new { a.At, Action = a.Action.ToString(), a.Actor, a.Stage, a.Comment }))
                : Results.NotFound());

        group.MapPost("/{id:guid}/approve", async (Guid id, Decision body, ClaimsPrincipal user, ApprovalService service, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(RequestDto.From(await service.ApproveAsync(id, body, user.ToCurrentUser(), ct), clock.GetUtcNow())));

        group.MapPost("/{id:guid}/reject", async (Guid id, Decision body, ClaimsPrincipal user, ApprovalService service, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(RequestDto.From(await service.RejectAsync(id, body, user.ToCurrentUser(), ct), clock.GetUtcNow())));

        group.MapPost("/{id:guid}/withdraw", async (Guid id, ClaimsPrincipal user, ApprovalService service, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(RequestDto.From(await service.WithdrawAsync(id, user.ToCurrentUser(), ct), clock.GetUtcNow())));

        app.MapGet("/api/workflows", (IWorkflowCatalog catalog) =>
            catalog.All.Select(w => new
            {
                w.Key,
                Stages = w.Stages.Select(s => new { s.Name, s.Role, SlaHours = s.Sla.TotalHours, s.MinimumAmount, s.EscalateToRole }),
            })).RequireAuthorization();
    }
}
