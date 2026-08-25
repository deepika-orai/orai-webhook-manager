using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OraiWebhookManager.Api.Logging;

namespace OraiWebhookManager.UnitTests;

public class RedactingLoggerTests
{
    [Theory]
    [InlineData("POST /api/webhooks/whatsapp/whk_live_d7fc9840da394d22ac8609005cebd7c3", "POST /api/webhooks/whatsapp/whk_live_***")]
    [InlineData("Request finished HTTP/1.1 POST http://localhost:5135/api/webhooks/whatsapp/whk_local_d7fc9840da394d22ac8609005cebd7c3 - 200", "Request finished HTTP/1.1 POST http://localhost:5135/api/webhooks/whatsapp/whk_local_*** - 200")]
    [InlineData("POST /api/webhooks/whatsapp/whk_test_1234567890abcdef", "POST /api/webhooks/whatsapp/whk_test_***")]
    [InlineData("POST /api/webhooks/whatsapp/whk_dev_1234567890abcdef", "POST /api/webhooks/whatsapp/whk_dev_***")]
    [InlineData("POST /api/webhooks/whatsapp/whk_unknownkey123456", "POST /api/webhooks/whatsapp/whk_***")]
    [InlineData("Normal log message without keys", "Normal log message without keys")]
    public void Redact_RedactsRawKeys_PreservingSafePrefix(string input, string expected)
    {
        var result = RedactingLogger.Redact(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void RedactingLogger_InterceptsLogCalls_SanitizesMessageToInnerLogger()
    {
        // Arrange
        var capturedLogs = new List<string>();
        var mockLogger = new SimpleTestLogger(msg => capturedLogs.Add(msg));
        var redactingLogger = new RedactingLogger(mockLogger);

        const string rawKey = "whk_local_d7fc9840da394d22ac8609005cebd7c3";
        var rawMessage = $"Request starting HTTP/1.1 POST http://localhost:5135/api/webhooks/whatsapp/{rawKey} - application/json 128";

        // Act
        redactingLogger.Log(LogLevel.Information, new EventId(1, "RequestStarting"), rawMessage, null, (s, _) => s);

        // Assert
        capturedLogs.Should().HaveCount(1);
        capturedLogs[0].Should().NotContain(rawKey);
        capturedLogs[0].Should().Contain("whk_local_***");
    }

    [Fact]
    public void RedactingLoggerFactory_CreatesRedactingLoggers()
    {
        // Arrange
        using var factory = LoggerFactory.Create(builder => { });
        var redactingFactory = new RedactingLoggerFactory(factory);

        // Act
        var logger = redactingFactory.CreateLogger("TestCategory");

        // Assert
        logger.Should().BeOfType<RedactingLogger>();
    }

    private class SimpleTestLogger : ILogger
    {
        private readonly Action<string> _onLog;

        public SimpleTestLogger(Action<string> onLog)
        {
            _onLog = onLog;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            _onLog(msg);
        }
    }
}
