using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Infrastructure.Persistence;

namespace WindowsScriptRunner.UnitTests;

public sealed class PersistenceExceptionTests
{
    [Fact]
    public void ConcurrencyUpdateFailureTranslatesToApplicationConflict()
    {
        var exception = new DbUpdateConcurrencyException();

        var translated = SqlExceptionTranslator.Translate(
            exception,
            NullLogger.Instance);

        Assert.IsType<ApplicationConflictException>(translated);
    }
}
