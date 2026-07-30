using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;

namespace WindowsScriptRunner.Infrastructure.Persistence.Queue;

public sealed class SqlWorkerCoordinationClock(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlWorkerCoordinationClock> logger) : IWorkerCoordinationClock
{
    private const string CurrentUtcSql =
        "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')";

    public Task<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken) =>
        SqlExceptionTranslator.ExecuteAsync(
            async () =>
            {
                var connection = dbContext.Database.GetDbConnection();
                var shouldClose = connection.State != ConnectionState.Open;
                if (shouldClose)
                {
                    await connection.OpenAsync(cancellationToken);
                }

                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = CurrentUtcSql;
                    command.CommandType = CommandType.Text;
                    command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
                    var value = await command.ExecuteScalarAsync(cancellationToken);
                    return value is DateTimeOffset utc
                        ? utc
                        : throw new InvalidOperationException(
                            "SQL Server did not return a UTC coordination timestamp.");
                }
                finally
                {
                    if (shouldClose)
                    {
                        await connection.CloseAsync();
                    }
                }
            },
            logger);
}
