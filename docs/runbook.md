# Operations runbook

## Signals

- `/health/live` proves the API process can serve requests.
- `/health/ready` proves Azure SQL connectivity.
- Structured logs carry `CorrelationId` and worker scopes add `JobId`.
- OpenTelemetry emits HTTP/client traces, runtime metrics, and workflow counters/histograms when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured.
- The dashboard reads persisted aggregates and job events, so it remains useful even after a worker restarts.

## Job is queued for too long

1. Check worker replica count and revision health in Container Apps.
2. Check active message count and the Service Bus queue's incoming/outgoing metrics.
3. Query unprocessed `OutboxMessages`. A growing set indicates Service Bus authentication, networking, throttling, or availability problems.
4. Confirm the worker identity still has Data Receiver and Data Sender on the queue.
5. Restart a failed revision only after capturing its logs; outbox and message delivery are safe to replay.

## Job failed

1. Read `GET /api/v1/jobs/{id}` for the stable error code and current retry state.
2. Read `/events` to identify the step and attempt history.
3. Correct the dependency or input problem.
4. Call `POST /api/v1/jobs/{id}/retry`. Only failed jobs accept this transition.

## Dead-letter queue grows

Application-exhausted jobs are deliberately dead-lettered with reason `workflow-failed`. Malformed envelopes use `invalid-message`. Inspect messages without destructive receive first, correlate them to SQL, fix the cause, then resubmit through the public retry endpoint. Avoid blindly forwarding the DLQ: the SQL job remains the authoritative state machine.

## Database migration fails

The API applies EF Core migrations when `Database__MigrateOnStartup=true`. Verify SQL firewall connectivity and the configured login, then inspect the `__EFMigrationsHistory` table. Migrations are idempotent. For a higher-scale environment, set startup migration false and run the same migration bundle as a single deployment job before shifting revision traffic.

## Rollback

Container images are tagged with the full Git commit SHA and Container Apps uses single-revision mode. Redeploy `apps.bicep` with the last known-good API and worker tags. Database migrations in this project are additive; introduce explicit forward-fix migrations for later destructive schema changes.
