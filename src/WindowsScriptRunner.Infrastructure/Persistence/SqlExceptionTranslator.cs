using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
        if (exception.InnerException is not SqlException sqlException)
        {
            logger.LogError(
                "Persistence commit failed with provider category {Category}",
                exception.GetType().Name);
            return new PersistenceOperationException(
                "The persistence operation failed.",
                exception);
        }

        return TranslateSqlException(exception, sqlException, logger);
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

    private static Exception TranslateSqlException(
        Exception operationException,
        SqlException sqlException,
        ILogger logger)
    {
        logger.LogWarning(
            "Persistence commit failed with SQL Server category {SqlErrorNumber}",
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
