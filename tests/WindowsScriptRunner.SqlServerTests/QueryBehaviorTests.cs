using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Infrastructure.Persistence.Repositories;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class QueryBehaviorTests
{
    [Fact]
    public async Task ScriptAggregateLoadUsesBoundedParameterizedSplitQueries()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var firstParameter = SqlServerTestData.Parameter(
            "Mode",
            ScriptParameterType.Enum,
            allowedValues: ["Safe", "Force"]);
        var firstVersion = SqlServerTestData.Version([firstParameter]);
        var secondParameter = SqlServerTestData.Parameter("Name");
        var secondVersion = SqlServerTestData.Version(
            [secondParameter],
            version: "2.0.0");
        var script = SqlServerTestData.Script(firstVersion);
        script.AddVersion(secondVersion, script.UpdatedUtc.AddMinutes(1));
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        var capture = new CommandCaptureInterceptor();
        await using var context = database.CreateContext(capture);
        var repository = new SqlScriptDefinitionRepository(
            context,
            NullLogger<SqlScriptDefinitionRepository>.Instance);

        var loaded = await repository.GetByIdAsync(script.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Versions.Count);
        Assert.InRange(capture.Commands.Count, 2, 6);
        Assert.All(
            capture.Commands,
            command => Assert.DoesNotContain(
                script.Id.ToString(),
                command,
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.ParameterCounts, count => count > 0);
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public ConcurrentQueue<int> ParameterCounts { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            ParameterCounts.Enqueue(command.Parameters.Count);
            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}
