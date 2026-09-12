# Google Cloud Pub/Sub Rollout Runbook

This runbook outlines the operational procedure, verification status, security constraints, and execution steps for deploying and activating Google Cloud Pub/Sub asynchronous buffering in ORAI Webhook Manager across Staging and Production environments.

---

## 1. Production Readiness & Verification Status

### 1.1 Local & Staging Verification Results (Phases 4A–4D-Local: PASSED)
* **Test Suite**: 292/292 unit and integration tests passing (`194 UnitTests`, `98 IntegrationTests`).
* **Load & Backlog Creation**: 100 synthetic webhooks ingested with concurrency 10 with producer enabled (`UsePubSubBuffer=true`) and subscriber disabled (`EnableSubscriber=false`); 100/100 accepted into Pub/Sub buffer with unique message IDs and 0 direct DB writes.
* **Backlog Drain**: 100/100 buffered messages drained into PostgreSQL `webhook_inbox` with subscriber enabled (`EnableSubscriber=true`); 100/100 reached `Processed` status and persisted to downstream `messages` and `message_status_events` tables.
* **Concurrent Live Flow**: 100 synthetic webhooks ingested under concurrent producer and subscriber operation (`UsePubSubBuffer=true`, `EnableSubscriber=true`, concurrency 10); 100/100 processed with 0 loss and 0 duplicates.
* **Real Broker Redelivery**: Verified against live GCP Pub/Sub broker using message ACK deadline expiration (deadline set to 0); identical Pub/Sub broker message ID redelivered and resolved idempotently (`AlreadyExists`), resulting in exactly 1 database inbox row and 0 duplicate records.
* **Loss & Failure Rates**: 0 lost messages, 0 duplicate rows, 0 Failed rows, 0 DeadLetter rows across all test phases.
* **Staging Subscription Backlog**: Cleaned and verified at exactly 0 unacknowledged messages.

### 1.2 Current Production Status & Blockers
* **GCP Production Resources**: Confirmed already provisioned in GCP project `orai-official`:
  * **Topic ID**: `whatsapp-webhook-inbox`
  * **Subscription ID**: `whatsapp-webhook-inbox-sub` (Pull delivery, ACK deadline 30–60s, 7-day retention)
* **Production Blockers (PENDING / BLOCKED)**:
  * **Azure Authentication**: Azure App Service name is `oraiapi`, but Azure managed-identity details (Tenant ID, Client/Object ID) are unavailable. Production authentication probe is **BLOCKED**.
  * **Workload Identity Federation (WIF) Design**: Unverified until Azure managed-identity details and an in-memory token-exchange prototype are established.
  * **Production Migration 004**: Pending application and verification on the production database.

---

## 2. Strict Security & Operational Invariants

The following constraints are mandatory across all rollout stages:

1. **No Broad Workload Identity User Bindings**: Prohibit tenant-wide `attribute.tid` bindings in Google Cloud IAM. Workload Identity Federation must strictly bind to the specific Azure managed-identity subject/Object ID (`attribute.sub` / `principal://...`).
2. **In-Memory Identity Only**: Never write `IDENTITY_HEADER`, Azure IMDS tokens, GCP STS tokens, or other identity secrets to disk or log files. Token exchange must occur entirely in-memory.
3. **No Service Account JSON Keys**: Service Account JSON keys are prohibited as the default authentication solution for production App Service workloads.
4. **Strict Consumer-First Activation**: Never enable the producer (`UsePubSubBuffer=true`) before the subscriber (`EnableSubscriber=true`) is running, verified, and healthy.
5. **No Premature Flag Activation**: Prohibit setting `UsePubSubBuffer=true` or `EnableSubscriber=true` in production until Schema Migration 004 and authentication probes pass.

---

## 3. Configuration Settings (Azure App Service / Environment)

Configure the following environment variables via App Service Configuration / environment variables (never commit active flags in repository configuration files):

| Setting Key | Staging / Production Value | Description |
|---|---|---|
| `GooglePubSub__ProjectId` | `orai-official` | Target GCP Project ID |
| `GooglePubSub__TopicId` | `whatsapp-webhook-inbox` | Ingestion buffer topic name |
| `GooglePubSub__SubscriptionId` | `whatsapp-webhook-inbox-sub` | Consumer pull subscription name |
| `GooglePubSub__PublishTimeoutSeconds` | `5` | Synchronous publish timeout threshold |
| `GooglePubSub__SubscriberClientCount` | `1` | Number of gRPC subscriber stream channels |
| `GooglePubSub__MaxOutstandingElementCount` | `100` | Flow control message batch limit |
| `GooglePubSub__MaxOutstandingByteCount` | `20971520` | Flow control byte limit (20 MB) |
| `GooglePubSub__EnableSubscriber` | `false` (default / initial) | Activates background subscriber worker |
| `GooglePubSub__UsePubSubBuffer` | `false` (default / initial) | Activates producer buffer on webhook ingestion |

---

## 4. Schema Migration 004 Verification

Before enabling the subscriber in any environment, Schema Migration 004 (`004_add_pubsub_message_id_to_inbox.sql`) must be verified on the target database.

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

## 5. Staged Rollout Procedure (Consumer-First)

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

### Step 5.1: Enable Subscriber First
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
   - Ensure no schema, permission, or connection exceptions are logged.

### Step 5.2: Enable Producer
1. Update environment setting:
   ```env
   GooglePubSub__UsePubSubBuffer=true
   GooglePubSub__EnableSubscriber=true
   ```
2. Restart the application.
3. Webhook endpoints (`/api/webhooks/whatsapp/{key}`) will now publish envelopes to Pub/Sub instead of inserting directly into PostgreSQL.

---

## 6. End-to-End Verification

### 6.1 Ingestion Verification
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

### 6.2 Pipeline Verification
1. Verify subscriber pulled the message from Pub/Sub and inserted it into `webhook_inbox` with `pubsub_message_id` populated.
2. Verify downstream processor worker claims and processes the inbox entry into `messages` and `message_status_events`.
3. Check application logs for:
   `"Pub/Sub consumer successfully processed and acknowledged message: {PubSubMessageId}"`.

---

## 7. Resilience & Failure Verification (503 / Retry-After)

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

## 8. Operational Monitoring & Health Indicators

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

## 9. Emergency Rollback Plan

If an issue occurs in Pub/Sub buffering, roll back to direct PostgreSQL ingestion in zero-downtime stages:

### Step 9.1: Disable Producer Immediately
1. Update environment setting:
   ```env
   GooglePubSub__UsePubSubBuffer=false
   GooglePubSub__EnableSubscriber=true
   ```
2. Restart application.
3. Ingestion immediately reverts to synchronous direct PostgreSQL writes. Incoming webhooks are unaffected.

### Step 9.2: Drain Pub/Sub Backlog
1. Keep `GooglePubSub__EnableSubscriber=true` running until the subscription backlog reaches 0.
2. Monitor GCP subscription `num_undelivered_messages` until all queued messages are processed and acknowledged into PostgreSQL.

### Step 9.3: Disable Subscriber (Optional)
Once backlog is drained:
```env
GooglePubSub__EnableSubscriber=false
GooglePubSub__UsePubSubBuffer=false
```
The application is now operating entirely in Direct PostgreSQL mode.
