# ADR-0007: Effect journal for idempotent outside effects

- **Status:** Accepted
- **Date:** 2026-11-25
- **Spine:** AD-5

## Context

A restart must never produce a duplicate pull request, a duplicate comment, or a second Workspace for the same Task. Every action visible outside Hephaestus must be recorded with its cause. A crash can happen between performing an action and recording it.

## Decision

Before any outside effect, the loop records an **intent** with a deterministic idempotency key. After the effect, it records the **result**. Only the loop writes the journal; adapters receive the key as a parameter and neither journal nor derive keys.

Effects are: push, pull request open and update, comment, label change, merge, issue creation, creation and removal of containers, volumes, and networks, and notifications. Reads are not effects.

On startup, an intent with no result is resolved by looking for the effect by a natural key before anything is retried:

| Effect | Resolved by |
| --- | --- |
| Pull request | Head branch |
| Comment, created issue | Hidden marker in the last line, written by Hephaestus's own identity |
| Label | Set operation, safe to repeat |
| Push | Fast-forward, safe to repeat |
| Merge | Pull request state |
| Docker object | Label |
| Removal | Absence |
| Notification | Not sent again |

The key has the form `<scope>:<cause>:<effect-kind>:<ordinal>`, where scope is a Task, a Project, or the installation, so that effects with no Task, such as creating issues from stories, can be journaled too.

An effect that fails because the far side is unreachable is retried at a recorded time within a bound while the Task keeps its state. At the bound the Task waits for the Owner; it does not fail.

The journal is append-only and is the audit trail.

## Alternatives considered

- **Record after the effect.** A crash between the two produces a duplicate. Rejected.
- **No journal; search GitHub before every action.** Slow, spends API quota, and gives no audit trail. Rejected.

## Consequences

- One mechanism serves both idempotency and audit.
- Every new kind of effect needs a natural key defined before it is built.
- Hidden markers are visible in the raw source of comments.
