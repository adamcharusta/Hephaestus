# ADR-0002: Single-writer orchestrator over a durable state machine; fixed lifecycle

- **Status:** Accepted (the transaction and Signal rules were tightened by review; the dependency rule was added when the owner moved declared dependencies into v0.1)
- **Date:** 2026-11-25
- **Spine:** AD-1, AD-18

## Context

Work must survive restarts without repeating outside effects, and the Owner must be able to reconstruct what happened. The product requirements fix the lifecycle of a Task (readiness, optional planning, implementation, validation, review, bounded correction, pull request) and exclude user-defined workflows from v1.

OpenAI's Symphony specification solves a similar problem with one orchestrator that owns all scheduling state and a poll tick that reconciles, admits, and dispatches. It keeps that state in memory and rebuilds it from the tracker after a restart.

## Decision

Adopt Symphony's single-writer loop and add durability.

- The Task state machine is persisted in SQLite. One orchestrator loop is the only writer of Task and step-run records.
- Every other source of change (GitHub poller, local API, container exit) appends a Signal to a persisted inbox. The loop applies Signals in a database-assigned order, one transaction per Signal, together with the intents of the effects that Signal causes.
- `Running` is entered only by dispatch from `Queued`, which is where "one Task at a time" is enforced.
- Dispatch passes over a Task with an unsatisfied declared dependency. Until stacked pull requests exist (v0.3), a dependency is satisfied only when the Task it names has been merged, because a dependent Task branches from the Base Branch. Dependencies are parsed in the core from the issue text a Trusted User vouched for.
- Every wait is a state with a recorded resume condition. No in-memory timer carries meaning.
- The loop never waits for a step. It starts a container and returns; the exit arrives as a Signal.
- A step interrupted by a restart is not resumed in place. It is checkpointed and attempted again.

The lifecycle is code, not configuration.

## Alternatives considered

- **A durable-execution engine** (workflow as code with replay). Supplies timers, retries, and recovery, but adds a heavy dependency, imposes determinism rules on workflow code, and contradicts the fixed-lifecycle decision. Rejected.
- **Full event sourcing of the Task.** History by construction, but projections and event versioning are more ceremony than a single writer handling one Task at a time needs. Rejected; an append-only journal of effects gives the audit trail instead.
- **A general workflow engine configured in YAML.** Explicitly out of scope for v1.

## Consequences

- Recovery is the loop's ordinary first tick, not a special mode.
- A cancellation or a new label is applied within one tick even during a long agent run.
- Timers, retries, and idempotency are written by hand; see ADR-0007.
- User-defined workflows would later need a new design, not an extension of this one.
- Before v0.3 a dependent Task waits for the Owner to merge the pull request it depends on. A Task whose dependency can never be merged is never started; how that is reported is not decided yet.
