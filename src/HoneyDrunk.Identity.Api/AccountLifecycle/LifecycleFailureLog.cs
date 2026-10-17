using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;

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
        logger.LogError(CreateSnapshot(error, 0), "Lifecycle maintenance failed; access restrictions remain in force.");
    }

    // Do not retain the original exception, message, Data, Source, HelpLink, DB entries,
    // file paths or line numbers. Some providers inspect Exception properties instead
    // of ToString(), so overriding only ToString() would not protect those sinks.
    private static InvalidOperationException CreateSnapshot(Exception error, int depth)
    {
        var snapshot = new InvalidOperationException(
            $"Redacted {error.GetType().FullName} (HResult: {error.HResult})",
            depth < 3 && error.InnerException is { } inner ? CreateSnapshot(inner, depth + 1) : null);
        var frames = new StackTrace(error, false).GetFrames();
        if (frames.Length != 0)
        {
            ExceptionDispatchInfo.SetRemoteStackTrace(snapshot, string.Join(
                Environment.NewLine,
                frames.Take(32).Select(frame => frame.GetMethod()).OfType<MethodBase>()
                    .Select(method => $"at {method.DeclaringType?.FullName}.{method.Name}")));
        }

        return snapshot;
    }
}
