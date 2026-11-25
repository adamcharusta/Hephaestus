# ADR-0005: Artifact-sufficient steps without session resume

- **Status:** Accepted
- **Date:** 2026-11-25
- **Spine:** AD-12

## Context

A Task may be handed from one vendor's agent to another's when a usage limit is reached. Conversation history cannot be transferred between vendors. Both CLIs can resume their own sessions, and the research report recommended persisting session state per Task so that a restarted container could continue.

## Decision

Every step must be executable from two things alone: a set of context files and the repository at its current commit. Every step runs in a fresh agent session. Session resume is not used in v1 and may only ever be added as an optimisation on which correctness does not depend.

The core assembles the context files for each step run under fixed names (`instruction.md`, `task.json`, `issue.md`, `comments.json`, `plan.md`, `findings.json`, `validation.json`, `handoff.md`, `result.schema.json`). The runtime places them read-only in the container. The prompt is one fixed sentence pointing at `instruction.md`. The handoff summary is assembled by the core from recorded data; no agent writes it, since the outgoing agent may be the one that just ran out of usage.

## Alternatives considered

- **Resume sessions for the same agent; use files only for handoff.** Cheaper and faster in correction rounds. It creates two paths, and the handoff path, the one the product exists for, would run only when a limit is hit. It also needs persistent session state per Task and per agent. Rejected.

This is a conscious departure from the research recommendation.

## Consequences

- A handoff is an ordinary step given to a different agent. The path is exercised by every Task.
- Each correction round costs tokens to re-read context.
- No per-agent session directory needs to persist, which keeps step containers disposable (ADR-0008, ADR-0009).
- If the token cost proves high in practice, resume can be added later behind an adapter capability without changing correctness.
