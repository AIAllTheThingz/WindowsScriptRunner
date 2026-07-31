using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Infrastructure.Persistence.Mapping;

namespace WindowsScriptRunner.Infrastructure.Persistence.Repositories;

public sealed class SqlJobReportRepository(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlJobReportRepository> logger) : IJobReportRepository
{
    public async Task<JobReport?> GetByIdAsync(
        JobReportId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var entity = await LoadAsync(
            item => item.Id == id.Value,
            cancellationToken);
        return Map(entity);
    }

    public async Task<JobReport?> GetByJobIdAsync(
        JobId jobId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        var entity = await LoadAsync(
            item => item.JobId == jobId.Value,
            cancellationToken);
        return Map(entity);
    }

    public Task AddAsync(
        JobReport report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();
        if (dbContext.ChangeTracker
            .Entries<JobReportEntity>()
            .Any(entry => entry.Entity.Id == report.Id.Value))
        {
            throw new ApplicationConflictException(
                "A report with the deterministic identifier is already tracked.");
        }

        dbContext.JobReports.Add(JobReportPersistenceMapper.ToEntity(report));
        logger.LogDebug(
            "Staged immutable report {ReportId} for job {JobId}",
            report.Id,
            report.JobId);
        return Task.CompletedTask;
    }

    private async Task<JobReportEntity?> LoadAsync(
        System.Linq.Expressions.Expression<Func<JobReportEntity, bool>> predicate,
        CancellationToken cancellationToken) =>
        await SqlExceptionTranslator.ExecuteAsync(
            () => dbContext.JobReports
                .Include(entity => entity.Inventory)
                .AsNoTracking()
                .SingleOrDefaultAsync(predicate, cancellationToken),
            logger);

    private JobReport? Map(JobReportEntity? entity)
    {
        if (entity is null)
        {
            return null;
        }

        try
        {
            return JobReportPersistenceMapper.ToDomain(entity);
        }
        catch (Exception exception)
            when (exception is DomainException or
                InvalidOperationException or
                ArgumentException)
        {
            logger.LogError(
                "Persisted report failed closed during typed rehydration.");
            throw new PersistenceOperationException(
                "Persisted report data is inconsistent.",
                exception);
        }
    }
}
