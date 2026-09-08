namespace OraiWebhookManager.Application.Exceptions;

public class WebhookBufferPublishException : Exception
{
    public Guid CorrelationId { get; }
    public bool IsAmbiguousTimeout { get; }

    public WebhookBufferPublishException(
        string message,
        Guid correlationId,
        bool isAmbiguousTimeout = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        CorrelationId = correlationId;
        IsAmbiguousTimeout = isAmbiguousTimeout;
    }
}
