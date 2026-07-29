using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;

namespace WindowsScriptRunner.Infrastructure.Persistence;

public sealed class SqlUnitOfWork(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlUnitOfWork> logger) : IUnitOfWork
{
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogDebug(
                "Persistence unit of work committed in {DurationMs} ms with {Outcome}",
                stopwatch.ElapsedMilliseconds,
                "Succeeded");
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug(
                "Persistence unit of work ended in {DurationMs} ms with {Outcome}",
                stopwatch.ElapsedMilliseconds,
                "Cancelled");
            throw;
        }
        catch (DbUpdateException exception)
        {
            throw SqlExceptionTranslator.Translate(exception, logger);
        }
        catch (RetryLimitExceededException exception)
            when (exception.InnerException is SqlException sqlException)
        {
            throw SqlExceptionTranslator.Translate(exception, sqlException, logger);
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is SqlException sqlException)
        {
            throw SqlExceptionTranslator.Translate(exception, sqlException, logger);
        }
        catch (SqlException exception)
        {
            throw SqlExceptionTranslator.Translate(exception, logger);
        }
    }
}
