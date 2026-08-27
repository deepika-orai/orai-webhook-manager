using FluentAssertions;
using OraiWebhookManager.Domain.Rules;

namespace OraiWebhookManager.UnitTests;

public class MessageStateEngineTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private const string Wamid = "wamid.HBgLMTY1MDY5Nzg1MjYVAgASGBgyMjhBRDM2M0JBMzM3QjgyQkY1MEQ0OEIwMzgzOTg0NQA=";

    [Fact]
    public void ComputeEventFingerprint_ShouldBeDeterministic()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var fp1 = MessageStateEngine.ComputeEventFingerprint(_tenantId, Wamid, "delivered", ts, null);
        var fp2 = MessageStateEngine.ComputeEventFingerprint(_tenantId, Wamid, "delivered", ts, null);

        fp1.Should().NotBeEmpty();
        fp1.Length.Should().Be(32);
        fp1.Should().Equal(fp2);
    }

    [Fact]
    public void ComputeEventFingerprint_ShouldDifferForDifferentStatuses()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var fpDelivered = MessageStateEngine.ComputeEventFingerprint(_tenantId, Wamid, "delivered", ts, null);
        var fpRead = MessageStateEngine.ComputeEventFingerprint(_tenantId, Wamid, "read", ts, null);

        fpDelivered.Should().NotEqual(fpRead);
    }

    [Fact]
    public void ShouldApplyStateTransition_UnseenMessage_AcceptsAnyInitialStatus()
    {
        var now = DateTimeOffset.UtcNow;
        MessageStateEngine.ShouldApplyStateTransition(null, null, null, "sent", MessageStateEngine.RankSent, now)
            .Should().BeTrue();

        MessageStateEngine.ShouldApplyStateTransition(null, null, null, "delivered", MessageStateEngine.RankDelivered, now)
            .Should().BeTrue();

        MessageStateEngine.ShouldApplyStateTransition(null, null, null, "failed", MessageStateEngine.RankFailed, now)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldApplyStateTransition_SentMessage_AcceptsForwardProgressionAndFailure()
    {
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2 = DateTimeOffset.UtcNow;

        // Sent -> Delivered
        MessageStateEngine.ShouldApplyStateTransition("sent", MessageStateEngine.RankSent, t1, "delivered", MessageStateEngine.RankDelivered, t2)
            .Should().BeTrue();

        // Sent -> Read
        MessageStateEngine.ShouldApplyStateTransition("sent", MessageStateEngine.RankSent, t1, "read", MessageStateEngine.RankRead, t2)
            .Should().BeTrue();

        // Sent -> Failed
        MessageStateEngine.ShouldApplyStateTransition("sent", MessageStateEngine.RankSent, t1, "failed", MessageStateEngine.RankFailed, t2)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldApplyStateTransition_DeliveredMessage_RejectsLateFailureAndSent()
    {
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2 = DateTimeOffset.UtcNow;

        // Delivered receiving late failure -> MUST NOT downgrade
        MessageStateEngine.ShouldApplyStateTransition("delivered", MessageStateEngine.RankDelivered, t1, "failed", MessageStateEngine.RankFailed, t2)
            .Should().BeFalse();

        // Delivered receiving late sent -> MUST NOT downgrade
        MessageStateEngine.ShouldApplyStateTransition("delivered", MessageStateEngine.RankDelivered, t1, "sent", MessageStateEngine.RankSent, t2)
            .Should().BeFalse();

        // Delivered receiving read -> Monotonic forward progression
        MessageStateEngine.ShouldApplyStateTransition("delivered", MessageStateEngine.RankDelivered, t1, "read", MessageStateEngine.RankRead, t2)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldApplyStateTransition_ReadMessage_RejectsLateDeliveredOrFailedOrSent()
    {
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2 = DateTimeOffset.UtcNow;

        // Read receiving late delivered -> MUST NOT downgrade
        MessageStateEngine.ShouldApplyStateTransition("read", MessageStateEngine.RankRead, t1, "delivered", MessageStateEngine.RankDelivered, t2)
            .Should().BeFalse();

        // Read receiving late failure -> MUST NOT downgrade
        MessageStateEngine.ShouldApplyStateTransition("read", MessageStateEngine.RankRead, t1, "failed", MessageStateEngine.RankFailed, t2)
            .Should().BeFalse();

        // Read receiving late sent -> MUST NOT downgrade
        MessageStateEngine.ShouldApplyStateTransition("read", MessageStateEngine.RankRead, t1, "sent", MessageStateEngine.RankSent, t2)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldApplyStateTransition_FailedMessage_RejectsLateSent()
    {
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2 = DateTimeOffset.UtcNow;

        // A failed message cannot become sent
        MessageStateEngine.ShouldApplyStateTransition("failed", MessageStateEngine.RankFailed, t1, "sent", MessageStateEngine.RankSent, t2)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldApplyStateTransition_FailedMessage_RecoversWithNewerDeliveredEvidence()
    {
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2Newer = DateTimeOffset.UtcNow;
        var t0Older = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Newer delivered evidence recovers failed state
        MessageStateEngine.ShouldApplyStateTransition("failed", MessageStateEngine.RankFailed, t1, "delivered", MessageStateEngine.RankDelivered, t2Newer)
            .Should().BeTrue();

        // Older delivered evidence does NOT recover
        MessageStateEngine.ShouldApplyStateTransition("failed", MessageStateEngine.RankFailed, t1, "delivered", MessageStateEngine.RankDelivered, t0Older)
            .Should().BeFalse();
    }

    [Fact]
    public void ComputeEventFingerprint_ExactDuplicateCallbacks_ProduceIdenticalFingerprint()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var fpInitial = MessageStateEngine.ComputeEventFingerprint(_tenantId, Wamid, "sent", ts, null);
        var fpDuplicate = MessageStateEngine.ComputeEventFingerprint(_tenantId, Wamid, "sent", ts, null);

        fpInitial.Should().Equal(fpDuplicate);
    }

    [Fact]
    public void ShouldApplyStateTransition_OutOfOrder_DeliveredBeforeSent_RejectsLateSent()
    {
        var tDelivered = DateTimeOffset.UtcNow.AddMinutes(-2);
        var tSent = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Step 1: Delivered arrives first (unseen message) -> accepted
        MessageStateEngine.ShouldApplyStateTransition(null, null, null, "delivered", MessageStateEngine.RankDelivered, tDelivered)
            .Should().BeTrue();

        // Step 2: Delayed Sent callback arrives later -> rejected (delivered never downgrades to sent)
        MessageStateEngine.ShouldApplyStateTransition("delivered", MessageStateEngine.RankDelivered, tDelivered, "sent", MessageStateEngine.RankSent, tSent)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldApplyStateTransition_OutOfOrder_ReadBeforeDelivered_RejectsLateDelivered()
    {
        var tRead = DateTimeOffset.UtcNow.AddMinutes(-1);
        var tDelivered = DateTimeOffset.UtcNow.AddMinutes(-3);

        // Step 1: Read arrives first (unseen message) -> accepted
        MessageStateEngine.ShouldApplyStateTransition(null, null, null, "read", MessageStateEngine.RankRead, tRead)
            .Should().BeTrue();

        // Step 2: Delayed Delivered callback arrives -> rejected (read never downgrades to delivered)
        MessageStateEngine.ShouldApplyStateTransition("read", MessageStateEngine.RankRead, tRead, "delivered", MessageStateEngine.RankDelivered, tDelivered)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldApplyStateTransition_OnlyReadEventReceived_TransitionsDirectlyToRead()
    {
        var tRead = DateTimeOffset.UtcNow;

        // Unseen message receiving only Read event transitions to Read directly without requiring sent/delivered
        var allowed = MessageStateEngine.ShouldApplyStateTransition(null, null, null, "read", MessageStateEngine.RankRead, tRead);
        allowed.Should().BeTrue();
    }
}
