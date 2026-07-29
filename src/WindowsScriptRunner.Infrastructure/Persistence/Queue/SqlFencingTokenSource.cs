using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;

namespace WindowsScriptRunner.Infrastructure.Persistence.Queue;

public sealed class SqlFencingTokenSource(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlFencingTokenSource> logger) : IFencingTokenSource
{
    private const string NextFencingTokenSql =
        "SELECT NEXT VALUE FOR [wsr].[JobLeaseFencingSequence] AS [Value]";

    public Task<long> GetNextAsync(CancellationToken cancellationToken) =>
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
                    command.CommandText = NextFencingTokenSql;
                    command.CommandType = CommandType.Text;
                    command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
                    var value = await command.ExecuteScalarAsync(cancellationToken);
                    return Convert.ToInt64(value, CultureInfo.InvariantCulture);
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
