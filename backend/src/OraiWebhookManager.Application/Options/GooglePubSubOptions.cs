using Microsoft.Extensions.Options;

namespace OraiWebhookManager.Application.Options;

public class GooglePubSubOptions
{
    public const string SectionName = "GooglePubSub";

    public bool UsePubSubBuffer { get; set; } = false;
    public bool EnableSubscriber { get; set; } = false;
    public string ProjectId { get; set; } = "orai-official";
    public string TopicId { get; set; } = "whatsapp-webhook-inbox";
    public string SubscriptionId { get; set; } = "whatsapp-webhook-inbox-sub";
    public int PublishTimeoutSeconds { get; set; } = 5;
    public int SubscriberClientCount { get; set; } = 1;
    public int MaxOutstandingElementCount { get; set; } = 100;
    public long MaxOutstandingByteCount { get; set; } = 20_971_520; // 20 MB
}

public class GooglePubSubOptionsValidator : IValidateOptions<GooglePubSubOptions>
{
    public ValidateOptionsResult Validate(string? name, GooglePubSubOptions options)
    {
        // When both publisher buffer and subscriber consumer are disabled, do not block application startup
        if (!options.UsePubSubBuffer && !options.EnableSubscriber)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        // Validation for Producer / Publisher
        if (options.UsePubSubBuffer)
        {
            if (string.IsNullOrWhiteSpace(options.ProjectId))
            {
                failures.Add("GooglePubSub:ProjectId is required when UsePubSubBuffer is true.");
            }

            if (string.IsNullOrWhiteSpace(options.TopicId))
            {
                failures.Add("GooglePubSub:TopicId is required when UsePubSubBuffer is true.");
            }

            if (options.PublishTimeoutSeconds <= 0)
            {
                failures.Add("GooglePubSub:PublishTimeoutSeconds must be greater than 0 when UsePubSubBuffer is true.");
            }
        }

        // Validation for Consumer / Subscriber
        if (options.EnableSubscriber)
        {
            if (string.IsNullOrWhiteSpace(options.ProjectId))
            {
                failures.Add("GooglePubSub:ProjectId is required when EnableSubscriber is true.");
            }

            if (string.IsNullOrWhiteSpace(options.SubscriptionId))
            {
                failures.Add("GooglePubSub:SubscriptionId is required when EnableSubscriber is true.");
            }

            if (options.SubscriberClientCount <= 0)
            {
                failures.Add("GooglePubSub:SubscriberClientCount must be greater than 0 when EnableSubscriber is true.");
            }

            if (options.MaxOutstandingElementCount <= 0)
            {
                failures.Add("GooglePubSub:MaxOutstandingElementCount must be greater than 0 when EnableSubscriber is true.");
            }

            if (options.MaxOutstandingByteCount <= 0)
            {
                failures.Add("GooglePubSub:MaxOutstandingByteCount must be greater than 0 when EnableSubscriber is true.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
