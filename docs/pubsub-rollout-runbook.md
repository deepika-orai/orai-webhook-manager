# Google Cloud Pub/Sub Rollout Runbook

This runbook outlines the operational procedure for deploying and activating Google Cloud Pub/Sub asynchronous buffering in ORAI Webhook Manager across Staging and Production environments.

---

## 1. Prerequisites & Infrastructure Setup

### 1.1 Topic & Subscription
Ensure the following Google Cloud Pub/Sub resources are provisioned in the target GCP Project:
* **Topic ID**: `whatsapp-webhook-inbox`
* **Subscription ID**: `whatsapp-webhook-inbox-sub`
  * **Delivery Type**: Pull
  * **Acknowledgment Deadline**: 30–60 seconds
  * **Message Retention Duration**: 7 days
  * **Dead Letter Topic**: (Optional/Recommended for unparseable poison messages)
  * **Retry Policy**: Exponential backoff (minimum 10s, maximum 600s)

### 1.2 Google IAM & Authentication Prerequisites
The App Service identity (or Service Account) requires the following IAM permissions:
* **Publisher Role**: `roles/pubsub.publisher` on topic `projects/{PROJECT_ID}/topics/whatsapp-webhook-inbox`
* **Subscriber Role**: `roles/pubsub.subscriber` on subscription `projects/{PROJECT_ID}/subscriptions/whatsapp-webhook-inbox-sub`
* **Authentication Method**:
  * Set environment variable `GOOGLE_APPLICATION_CREDENTIALS` to the path of the authorized Service Account JSON key, or configure Google Application Default Credentials (ADC) / Workload Identity.

### 1.3 Database Migration 004 Verification
Pub/Sub consumer worker performs a strict database readiness check on startup. Schema Migration 004 (`004_add_pubsub_message_id_to_inbox.sql`) must be applied and verified before enabling the subscriber.

Verify schema readiness with the following SQL query:
```sql
-- 1. Check Column Existence
SELECT column_name, data_type, character_maximum_length
FROM information_schema.columns
WHERE table_name = 'webhook_inbox' AND column_name = 'pubsub_message_id';

-- 2. Check Unique Partial Index Readiness and Validity
SELECT
    idx.indisvalid,
    idx.indisready,
    idx.indisunique,
    pg_get_expr(idx.indpred, idx.indrelid) AS index_predicate,
    att.attname AS indexed_column
FROM pg_class t
JOIN pg_namespace n ON n.oid = t.relnamespace
JOIN pg_index idx ON idx.indrelid = t.oid
JOIN pg_class idx_cls ON idx_cls.oid = idx.indexrelid
JOIN pg_attribute att ON att.attrelid = t.oid AND att.attnum = ANY(idx.indkey)
WHERE t.relname = 'webhook_inbox'
  AND idx_cls.relname = 'ix_webhook_inbox_pubsub_message_id';
```
Expected result: `indisvalid = true`, `indisready = true`, `indisunique = true`, `indexed_column = 'pubsub_message_id'`, predicate includes `pubsub_message_id IS NOT NULL`.

---

## 2. Configuration Settings (Azure App Service / Environment)

Configure the following environment variables (do not hardcode in repository files):

| Setting Key | Staging / Production Value | Description |
|---|---|---|
| `GooglePubSub__ProjectId` | `<GCP_PROJECT_ID>` | GCP Project ID (configured per environment) |
| `GooglePubSub__TopicId` | `whatsapp-webhook-inbox` | Ingestion buffer topic name |
| `GooglePubSub__SubscriptionId` | `whatsapp-webhook-inbox-sub` | Consumer pull subscription name |
| `GooglePubSub__PublishTimeoutSeconds` | `5` | Synchronous publish timeout threshold |
| `GooglePubSub__SubscriberClientCount` | `1` | Number of gRPC subscriber stream channels |
| `GooglePubSub__MaxOutstandingElementCount` | `100` | Flow control message batch limit |
| `GooglePubSub__MaxOutstandingByteCount` | `20971520` | Flow control byte limit (20 MB) |
| `GooglePubSub__EnableSubscriber` | `false` (initial) | Activates background subscriber worker |
| `GooglePubSub__UsePubSubBuffer` | `false` (initial) | Activates producer buffer on webhook ingestion |

---

## 3. Staged Rollout Procedure (Consumer-First)

To prevent message accumulation in Pub/Sub without an active consumer, the rollout must follow a strict **Consumer-First** order.

```
Step 1: Verify Migration 004 in DB
       │
       ▼
Step 2: Enable Subscriber (EnableSubscriber=true, UsePubSubBuffer=false)
       │
       ▼
Step 3: Verify Subscriber Startup & Readiness Logs
       │
       ▼
Step 4: Enable Producer (UsePubSubBuffer=true)
       │
       ▼
Step 5: Run End-to-End Ingestion & Processing Verification
```

### Step 3.1: Enable Subscriber First
1. Update environment setting:
   ```env
   GooglePubSub__EnableSubscriber=true
   GooglePubSub__UsePubSubBuffer=false
   ```
2. Restart the application.
3. Inspect startup logs:
   - Verify `PubSubDatabaseReadinessChecker` succeeds:
     `"Pub/Sub database schema readiness check succeeded..."`
   - Verify `WebhookPubSubConsumerWorker` starts:
     `"WebhookPubSubConsumerWorker starting. ProjectId: ..., SubscriptionId: ..."`
   - Ensure no critical schema or permission exceptions are logged.

### Step 3.2: Enable Producer
1. Update environment setting:
   ```env
   GooglePubSub__UsePubSubBuffer=true
   GooglePubSub__EnableSubscriber=true
   ```
2. Restart the application.
3. Webhook endpoints (`/api/webhooks/whatsapp/{key}`) will now publish envelopes to Pub/Sub instead of inserting directly into PostgreSQL.

---

## 4. End-to-End Verification

### 4.1 Ingestion Verification
Send a test WhatsApp webhook payload to an active endpoint:
```http
POST /api/webhooks/whatsapp/{endpoint_plain_key}
Content-Type: application/json

{
  "object": "whatsapp_business_account",
  "entry": [
    {
      "id": "12345",
      "changes": [
        {
          "field": "messages",
          "value": {
            "messaging_product": "whatsapp",
            "statuses": [
              {
                "id": "wamid.TEST_VERIFICATION_001",
                "status": "delivered",
                "timestamp": "1740000000"
              }
            ]
          }
        }
      ]
    }
  ]
}
```

**Expected Response**:
```json
{
  "received": true,
  "buffered": true,
  "correlation_id": "...",
  "queue_message_id": "...",
  "inbox_id": null
}
```

### 4.2 Pipeline Verification
1. Verify subscriber pulled the message from Pub/Sub and inserted it into `webhook_inbox` with `pubsub_message_id` populated.
2. Verify downstream processor worker claims and processes the inbox entry into `whatsapp_messages` and `message_status_events`.
3. Check application logs for:
   `"Pub/Sub consumer successfully processed and acknowledged message: {PubSubMessageId}"`.

---

## 5. Resilience & Failure Verification (503 / Retry-After)

The buffering architecture enforces strict safety: **under no circumstances does the system fall back to direct PostgreSQL ingestion if Pub/Sub publishing fails or times out**, avoiding race conditions and split-brain ingestion.

* **Behavior on Pub/Sub Publish Failure / Timeout (>5s)**:
  * HTTP Status: `503 Service Unavailable`
  * Header: `Retry-After: 5`
  * Response Body:
    ```json
    {
      "error": "Pub/Sub buffer is temporarily unavailable. Please retry shortly.",
      "retry_after_seconds": 5,
      "correlation_id": "..."
    }
    ```
* Meta Webhook infrastructure will automatically respect the `503` + `Retry-After` header and retry delivery.

---

## 6. Operational Monitoring & Health Indicators

Monitor the following key metrics in Azure Application Insights and Google Cloud Console:

1. **Google Cloud Pub/Sub Metrics**:
   * `pubsub.googleapis.com/subscription/num_undelivered_messages` (Subscription backlog - should remain near 0).
   * `pubsub.googleapis.com/subscription/oldest_unacked_message_age` (Message latency).
   * `pubsub.googleapis.com/topic/send_request_count` (Publish throughput).
2. **Application Log Alerts**:
   * `WebhookPubSubConsumerWorker` error logs (`LogCritical` / `LogError`).
   * `GooglePubSubWebhookPublisher` publish timeouts or exceptions.
   * Duplicate insertion acknowledgment logs (`"Duplicate PubSub message {MessageId} already in inbox. Acknowledging message."`).

---

## 7. Emergency Rollback Plan

If an issue occurs in Pub/Sub buffering, roll back to direct PostgreSQL ingestion in zero-downtime stages:

### Step 7.1: Disable Producer Immediately
1. Update environment setting:
   ```env
   GooglePubSub__UsePubSubBuffer=false
   GooglePubSub__EnableSubscriber=true
   ```
2. Restart application.
3. Ingestion immediately reverts to synchronous direct PostgreSQL writes. Incoming webhooks are unaffected.

### Step 7.2: Drain Pub/Sub Backlog
1. Keep `GooglePubSub__EnableSubscriber=true` running until the subscription backlog reaches 0.
2. Monitor GCP subscription `num_undelivered_messages` until all queued messages are processed and acknowledged into PostgreSQL.

### Step 7.3: Disable Subscriber (Optional)
Once backlog is drained:
```env
GooglePubSub__EnableSubscriber=false
GooglePubSub__UsePubSubBuffer=false
```
The application is now operating entirely in Direct PostgreSQL mode.
