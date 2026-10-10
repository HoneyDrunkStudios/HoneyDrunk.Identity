using HoneyDrunk.Identity.Api.AccountLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace HoneyDrunk.Identity.Tests.AccountLifecycle;

/// <summary>Checks the actual provider-facing exception graph for sensitive values.</summary>
public sealed class LifecycleFailureLogTests
{
    /// <summary>Logging retains useful types and method frames while removing every raw exception data channel.</summary>
    [Fact]
    public void DatabaseFailureIsRedactedBeforeTheProvider()
    {
        var logger = new CaptureLogger();
        var error = CaptureFailure();
        error.Data["private-key"] = "private-data";
        error.Source = "private-source";
        error.HelpLink = "https://private.example.test/secret";

        LifecycleFailureLog.Write(logger, error);

        Assert.Equal(LogLevel.Error, logger.Level);
        var safe = Assert.IsType<Exception>(logger.Error, exactMatch: false);
        Assert.NotSame(error, safe);
        Assert.NotSame(error.InnerException, safe.InnerException);
        Assert.Contains(nameof(DbUpdateException), safe.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), safe.InnerException!.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ThrowFailure), safe.StackTrace, StringComparison.Ordinal);
        Assert.Contains("access restrictions remain in force", logger.Message, StringComparison.Ordinal);
        foreach (var item in new[] { safe, safe.InnerException })
        {
            Assert.Empty(item.Data);
            Assert.Null(item.HelpLink);
            Assert.Null(item.Source);
            Assert.Null(item.TargetSite);
            Assert.DoesNotContain("private-", item.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(".cs:line", item.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>Unthrown exceptions and long chains remain bounded and do not expose messages.</summary>
    [Fact]
    public void LongUnthrownChainIsBounded()
    {
        Exception error = new InvalidOperationException("private-leaf");
        for (var index = 0; index < 10; index++)
            error = new DbUpdateException("private-parent", error);
        var logger = new CaptureLogger();
        LifecycleFailureLog.Write(logger, error);
        var count = 0;
        for (var safe = logger.Error; safe is not null; safe = safe.InnerException)
        {
            count++;
            Assert.True(string.IsNullOrEmpty(safe.StackTrace));
            Assert.DoesNotContain("private-", safe.ToString(), StringComparison.Ordinal);
        }

        Assert.Equal(4, count);
    }

    private static Exception CaptureFailure()
    {
        try
        {
            ThrowFailure();
        }
        catch (DbUpdateException error)
        {
            return error;
        }

        throw new InvalidOperationException("The fixture must throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFailure() => throw new DbUpdateException("private-sql-value", new InvalidOperationException("private-inner-value"));

    private sealed class CaptureLogger : ILogger
    {
        public Exception? Error { get; private set; }

        public string? Message { get; private set; }

        public LogLevel Level { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            Error = exception;
            Message = formatter(state, exception);
        }
    }
}
