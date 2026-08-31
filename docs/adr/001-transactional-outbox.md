# ADR 001: Transactional outbox for job dispatch

- Status: Accepted
- Date: 2026-08-30

## Context

Submitting a workflow requires two durable effects: create a SQL job and publish a Service Bus command. Azure SQL and Service Bus do not share the application's local transaction. Writing SQL first can strand a job if publishing fails; publishing first can execute work for a job that never commits.

## Decision

Write the job and an outbox row in one SQL transaction. A hosted dispatcher publishes pending rows separately and marks each row processed after Service Bus accepts it. Use the outbox id as the broker message id and enable queue duplicate detection. Keep the worker idempotent because duplicate delivery remains possible outside the broker's detection window.

## Consequences

Submission no longer depends on broker availability, and a committed job is eventually publishable. The system adds a small dispatch delay, an outbox table, a polling process, and cleanup work. Publication is at-least-once rather than exactly-once, which is made safe through job-state checks.

## Rejected alternatives

- Direct publish from the controller: simple, but creates a dual-write failure window.
- Distributed transaction: not portable across this Azure architecture and adds operational coupling.
- Service Bus as the only source of truth: makes querying, idempotency, audit history, cancellation, and manual retry substantially harder.
