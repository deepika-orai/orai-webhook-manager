using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OraiWebhookManager.Api.Logging;

public static class RedactionLoggingExtensions
{
    public static IServiceCollection AddWebhookKeyRedactionLogging(this IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ILoggerFactory));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<ILoggerFactory>(sp =>
        {
            var providers = sp.GetServices<ILoggerProvider>();
            var filterOptions = sp.GetService<IOptions<LoggerFilterOptions>>()?.Value ?? new LoggerFilterOptions();
            var innerFactory = new LoggerFactory(providers, filterOptions);
            return new RedactingLoggerFactory(innerFactory);
        });

        return services;
    }
}

public sealed class RedactingLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory _innerFactory;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public RedactingLoggerFactory(ILoggerFactory innerFactory)
    {
        _innerFactory = innerFactory ?? throw new ArgumentNullException(nameof(innerFactory));
    }

    public void AddProvider(ILoggerProvider provider)
    {
        _innerFactory.AddProvider(provider);
        _loggers.Clear();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new RedactingLogger(_innerFactory.CreateLogger(name)));
    }

    public void Dispose()
    {
        _innerFactory.Dispose();
        _loggers.Clear();
    }
}

public sealed class RedactingLogger : ILogger
{
    private readonly ILogger _innerLogger;

    private static readonly Regex WebhookKeyRegex = new(
        @"whk_[a-zA-Z0-9_-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public RedactingLogger(ILogger innerLogger)
    {
        _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _innerLogger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _innerLogger.IsEnabled(logLevel);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string RedactedFormatter(TState s, Exception? ex)
        {
            var raw = formatter != null ? formatter(s, ex) : s?.ToString() ?? string.Empty;
            return Redact(raw);
        }

        var sanitizedException = exception != null ? RedactException(exception) : null;

        _innerLogger.Log(
            logLevel,
            eventId,
            state,
            sanitizedException,
            RedactedFormatter
        );
    }

    public static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return WebhookKeyRegex.Replace(input, match =>
        {
            var val = match.Value;
            if (val.StartsWith("whk_live_", StringComparison.OrdinalIgnoreCase)) return "whk_live_***";
            if (val.StartsWith("whk_local_", StringComparison.OrdinalIgnoreCase)) return "whk_local_***";
            if (val.StartsWith("whk_test_", StringComparison.OrdinalIgnoreCase)) return "whk_test_***";
            if (val.StartsWith("whk_dev_", StringComparison.OrdinalIgnoreCase)) return "whk_dev_***";
            return "whk_***";
        });
    }

    private static Exception? RedactException(Exception? ex)
    {
        if (ex == null) return null;
        var redactedMessage = Redact(ex.Message);
        if (redactedMessage == ex.Message && ex.InnerException == null)
        {
            return ex;
        }

        return new Exception(redactedMessage, ex.InnerException != null ? RedactException(ex.InnerException) : null);
    }
}
