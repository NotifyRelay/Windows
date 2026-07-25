using Microsoft.Extensions.Logging;

namespace NotifyRelay.Extensions;

public static class LoggerExtensions
{
    public static void Info(this ILogger logger, string message) => logger.LogInformation(message);
    public static void Info<T>(this ILogger<T> logger, string message) => logger.LogInformation(message);
    public static void Warn(this ILogger logger, string message) => logger.LogWarning(message);
    public static void Warn<T>(this ILogger<T> logger, string message) => logger.LogWarning(message);
    public static void Error(this ILogger logger, string message, Exception? ex = null) => logger.LogError(ex, message);
    public static void Error<T>(this ILogger<T> logger, string message, Exception? ex = null) => logger.LogError(ex, message);
    public static void Debug(this ILogger logger, string message) => logger.LogDebug(message);
    public static void Debug<T>(this ILogger<T> logger, string message) => logger.LogDebug(message);
}
