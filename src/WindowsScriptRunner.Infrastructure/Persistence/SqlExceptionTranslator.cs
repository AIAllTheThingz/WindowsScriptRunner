using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Exceptions;

namespace WindowsScriptRunner.Infrastructure.Persistence;

internal static class SqlExceptionTranslator
{
    private static readonly HashSet<int> ConnectionFailureNumbers =
    [
        2,
        20,
        53,
        64,
        233,
        258,
        4060,
        10928,
        10929,
        40197,
        40501,
        40613,
    ];

    public static Exception Translate(DbUpdateException exception, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logger);
        if (exception is DbUpdateConcurrencyException concurrencyException)
        {
            logger.LogWarning(
                "Persistence operation failed due to a concurrency conflict for {AffectedEntries} entries",
                concurrencyException.Entries.Count);
            return new ApplicationConflictException(
                "The persisted aggregate changed after it was loaded.",
                exception);
        }

        if (!TryGetSqlException(exception, out var sqlException))
        {
            logger.LogError(
                "Persistence operation failed with provider exception type {ExceptionType}",
                exception.InnerException?.GetType().Name ?? exception.GetType().Name);
            return new PersistenceOperationException(
                "The persistence operation failed.",
                exception);
        }

        return TranslateSqlException(exception, sqlException, logger);
    }

    public static Exception TranslateRetryLimitExceeded(
        RetryLimitExceededException exception,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logger);
        if (TryGetSqlException(exception, out var sqlException))
        {
            return TranslateSqlException(exception, sqlException, logger);
        }

        logger.LogError(
            "Persistence retry limit was exceeded with provider exception type {ExceptionType}",
            exception.InnerException?.GetType().Name ?? exception.GetType().Name);
        return new PersistenceOperationException(
            "The persistence operation failed after exhausting retries.",
            exception);
    }

    public static Exception Translate(SqlException exception, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logger);
        return TranslateSqlException(exception, exception, logger);
    }

    public static Exception Translate(
        Exception operationException,
        SqlException sqlException,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(operationException);
        ArgumentNullException.ThrowIfNull(sqlException);
        ArgumentNullException.ThrowIfNull(logger);
        return TranslateSqlException(operationException, sqlException, logger);
    }

    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RetryLimitExceededException exception)
        {
            throw TranslateRetryLimitExceeded(exception, logger);
        }
        catch (InvalidOperationException exception)
            when (TryGetSqlException(exception, out var sqlException))
        {
            throw Translate(exception, sqlException, logger);
        }
        catch (SqlException exception)
        {
            throw Translate(exception, logger);
        }
    }

    internal static bool TryGetSqlException(
        Exception exception,
        out SqlException sqlException)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (Exception? candidate = exception; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is SqlException match)
            {
                sqlException = match;
                return true;
            }
        }

        sqlException = null!;
        return false;
    }

    private static Exception TranslateSqlException(
        Exception operationException,
        SqlException sqlException,
        ILogger logger)
    {
        logger.LogWarning(
            "Persistence operation failed with SQL Server category {SqlErrorNumber}",
            sqlException.Number);
        return sqlException.Number switch
        {
            2601 or 2627 => new ApplicationConflictException(
                "The persistence operation conflicts with existing data.",
                operationException),
            547 => new ApplicationValidationException(
                "The persistence operation violates a required relationship or data constraint.",
                operationException),
            -2 => new PersistenceUnavailableException(
                "The persistence operation timed out.",
                operationException),
            _ when ConnectionFailureNumbers.Contains(sqlException.Number) =>
                new PersistenceUnavailableException(
                    "SQL Server is unavailable.",
                    operationException),
            _ => new PersistenceOperationException(
                "The persistence operation failed.",
                operationException),
        };
    }
}
