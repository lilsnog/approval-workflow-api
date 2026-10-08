using System.Collections.Concurrent;
using ApprovalWorkflow.Application;
using ApprovalWorkflow.Domain;

namespace ApprovalWorkflow.Infrastructure;

/// <summary>
/// Thread-safe in-memory store. Swap for an EF Core / Dapper implementation of
/// <see cref="IRequestRepository"/> to persist to SQL Server, PostgreSQL or MySQL.
/// </summary>
public sealed class InMemoryRequestRepository : IRequestRepository
{
    private readonly ConcurrentDictionary<Guid, ApprovalRequest> _items = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public Task AddAsync(ApprovalRequest request, CancellationToken ct)
    {
        if (!_items.TryAdd(request.Id, request))
        {
            throw new InvalidOperationException($"Request {request.Id} already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_items.TryGetValue(id, out var r) ? r : null);

    public Task<IReadOnlyList<ApprovalRequest>> ListAsync(RequestQuery query, CancellationToken ct)
    {
        IEnumerable<ApprovalRequest> q = _items.Values;

        if (query.Status is { } status)
        {
            q = q.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.AwaitingRole))
        {
            q = q.Where(r => r.CurrentStage?.Role.Equals(query.AwaitingRole, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(query.RequestedBy))
        {
            q = q.Where(r => r.RequestedBy.Equals(query.RequestedBy, StringComparison.OrdinalIgnoreCase));
        }

        IReadOnlyList<ApprovalRequest> list = q.OrderByDescending(r => r.SubmittedAt).ToList();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(CancellationToken ct) =>
        ListAsync(new RequestQuery(Status: RequestStatus.Pending), ct);

    public async Task<ApprovalRequest?> UpdateAsync(Guid id, Action<ApprovalRequest> change, CancellationToken ct)
    {
        if (!_items.TryGetValue(id, out var request))
        {
            return null;
        }

        var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            change(request);
            return request;
        }
        finally
        {
            gate.Release();
        }
    }
}
