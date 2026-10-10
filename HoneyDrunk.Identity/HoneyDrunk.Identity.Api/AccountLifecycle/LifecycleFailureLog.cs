using System.Diagnostics;

namespace HoneyDrunk.Identity.Api.AccountLifecycle;

/// <summary>Retains exception types and method frames without sending database values to logging providers.</summary>
public static class LifecycleFailureLog
{
    /// <summary>Logs a bounded, redacted snapshot; the original exception never reaches a logging provider.</summary>
    /// <param name="logger">The maintenance logger.</param>
    /// <param name="error">The caught failure, potentially containing sensitive messages or database entries.</param>
    public static void Write(ILogger logger, Exception error)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(error);
        logger.LogError(new RedactedFailure(error, 0), "Lifecycle maintenance failed; access restrictions remain in force.");
    }

    // Do not retain the original exception, message, Data, Source, HelpLink, DB entries,
    // file paths or line numbers. Some providers inspect Exception properties instead
    // of ToString(), so overriding only ToString() would not protect those sinks.
    private sealed class RedactedFailure : Exception
    {
        private readonly string? safeStackTrace;

        public RedactedFailure(Exception error, int depth)
            : base(
                $"Redacted {error.GetType().FullName}",
                depth < 3 && error.InnerException is { } inner ? new RedactedFailure(inner, depth + 1) : null)
        {
            HResult = error.HResult;
            var frames = new StackTrace(error, false).GetFrames();
            safeStackTrace = frames is null ? null : string.Join(
                Environment.NewLine,
                frames.Take(32).Select(frame => frame.GetMethod()).Where(method => method is not null)
                    .Select(method => $"at {method!.DeclaringType?.FullName}.{method.Name}"));
        }

        public override string? StackTrace => safeStackTrace;
    }
}
