# ADR-0017: Failure classes, bounds, and capacity

- **Status:** Accepted (precedence, the two bounds, and capacity as a record were added by review; the owner confirmed that a waiting Task does not block others)
- **Date:** 2026-11-25
- **Spine:** AD-11, AD-20

## Context

Handoff and waiting for capacity depend on recognising a usage limit, and the signals both vendors emit at a limit are undocumented or unconfirmed. Treating an unrecognised failure as a limit would make Hephaestus wait for a reset that never comes. Three components can observe a failure (the container runtime, the adapter, the core), and without an order they will classify the same failure differently. The requirements also say that an agent that is not installed or not signed in has no capacity, and that an outage must not lose state.

## Decision

**One vocabulary.** Every failed step run is exactly one of: usage limit, timeout, agent error, validation failure, infrastructure error, cancelled, unknown. Anything not positively identified is unknown.

**One order.** The runtime's cause comes first (cancelled, timeout, out-of-memory or Docker fault). Then the adapter's reading of a CLI that exited by itself. Then the core, which marks a missing or invalid result as an agent error. A step interrupted by a restart is an infrastructure error.

**Two bounds.** Failed Validation and blocking Findings spend correction rounds. Timeouts, agent errors, infrastructure errors, and unknown failures spend a retry budget for that step kind. Exhausting either fails the Task. A usage limit spends neither; handoffs have their own bound.

**Capacity belongs to the agent.** It is recorded per agent credential (exhausted until, last probe), not on the Task that met the limit, and dispatch consults it before choosing an agent. An agent that a probe finds unavailable has no capacity; that is not a failed step. A Task waiting for capacity returns to the queue when capacity returns and does not hold the single execution slot meanwhile.

**Outages.** An unreachable GitHub or vendor does not fail a Task (ADR-0007).

## Alternatives considered

- **Infer a limit from exit code and text.** The basis for unknown-as-limit mistakes. Rejected; a limit must be positively identified.
- **One bound for everything.** A crash would then use up a correction round, and a flaky network could fail a Task whose code was fine. Rejected.
- **Capacity as a state of the Task only.** The next Task would be dispatched to the same exhausted agent. Rejected.

## Consequences

- Until the usage-limit spike is run, most real limits will be classified unknown and retried, not handed off. That is the safe direction.
- Whether a Task waiting for capacity should block other Tasks is a product question the requirements did not settle. The owner confirmed that it does not.
