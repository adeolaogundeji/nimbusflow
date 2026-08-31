using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NimbusFlow.Application.Jobs;
using NimbusFlow.Domain.Jobs;
using NimbusFlow.Domain.Messaging;

namespace NimbusFlow.Infrastructure.Persistence;

public sealed class JobStore(NimbusFlowDbContext dbContext) : IJobStore, IOutboxStore
{
    public Task<WorkflowJob?> FindAsync(Guid id, bool includeEvents, CancellationToken cancellationToken)
    {
        IQueryable<WorkflowJob> query = dbContext.Jobs;
        if (includeEvents)
        {
            query = query.Include(job => job.Events);
        }

        return query.SingleOrDefaultAsync(job => job.Id == id, cancellationToken);
    }

    public Task<WorkflowJob?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.Jobs
            .AsNoTracking()
            .SingleOrDefaultAsync(job => job.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<WorkflowJob>> ListAsync(
        WorkflowJobStatus? status,
        string? workflowType,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        await ApplyFilters(dbContext.Jobs.AsNoTracking(), status, workflowType)
            .OrderByDescending(job => job.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(
        WorkflowJobStatus? status,
        string? workflowType,
        CancellationToken cancellationToken) =>
        ApplyFilters(dbContext.Jobs.AsNoTracking(), status, workflowType).CountAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<WorkflowJobStatus, int>> CountByStatusAsync(CancellationToken cancellationToken) =>
        await dbContext.Jobs
            .AsNoTracking()
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);

    public async Task AddJobAsync(WorkflowJob job, CancellationToken cancellationToken) =>
        await dbContext.Jobs.AddAsync(job, cancellationToken);

    public async Task AddOutboxAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        await dbContext.OutboxMessages.AddAsync(message, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateIdempotencyKeyException(exception);
        }
    }

    public Task ReloadAsync(WorkflowJob job, CancellationToken cancellationToken) =>
        dbContext.Entry(job).ReloadAsync(cancellationToken);

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await dbContext.OutboxMessages
            .Where(message => message.ProcessedAt == null && message.AvailableAt <= now)
            .OrderBy(message => message.OccurredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    private static IQueryable<WorkflowJob> ApplyFilters(
        IQueryable<WorkflowJob> query,
        WorkflowJobStatus? status,
        string? workflowType)
    {
        if (status is not null)
        {
            query = query.Where(job => job.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(workflowType))
        {
            query = query.Where(job => job.WorkflowType == workflowType);
        }

        return query;
    }
}
