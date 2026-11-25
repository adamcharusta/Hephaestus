# ADR-0004: Agents as CLI processes behind pure adapters

- **Status:** Accepted
- **Date:** 2026-11-25
- **Spine:** AD-10, AD-13

## Context

Hephaestus drives Claude Code and Codex CLI and must add others later. The two tools do not expose identical capabilities. The orchestration core must not know vendor flags, and the component that runs containers must not know about agents.

Research found that both tools offer a documented non-interactive mode with a machine-readable event stream (`claude -p --output-format stream-json`, `codex exec --json`) and schema-constrained final output. The Claude Agent SDK ships for Python and TypeScript only. Neither tool speaks the Agent Client Protocol natively.

## Decision

Integrate through the command-line interfaces, one adapter per agent, and keep adapters pure. An adapter:

1. describes the invocation for a step: argument vector, environment, references to secrets, files, the network hosts the CLI needs in that authentication mode, and where the result is found;
2. describes a probe that checks the CLI is present, authenticated, and has capacity;
3. parses a stored output stream into normalized events;
4. produces a normalized result, with usage where the CLI reports it;
5. classifies a CLI that exited by itself.

It starts no process and knows nothing about Docker. The Workspace runtime runs what it is handed.

Step results are structured documents. The core owns one result type per step kind, restricted to what both CLIs accept; each adapter adapts the schema to its CLI; the core validates every result by strict deserialization. A missing or invalid result is a failure and is never interpreted. Every result can say "I need a decision" instead of completing.

A capability one CLI lacks is declared absent and reported, never imitated.

## Alternatives considered

- **The vendor SDK.** Not available for .NET. Rejected.
- **Agent Client Protocol.** Adapter-only for both tools, aimed at editor sessions, and still changing. Deferred until a third agent is added; the adapter interface is narrow enough to back with it later.
- **Result as a file the agent writes.** Works with any agent but nothing enforces that the file is written. Kept as an option an adapter may use, since how the result is obtained is adapter detail.
- **Parsing free text.** Fragile. Rejected.

## Consequences

- Adapters are tested against recorded CLI transcripts with no container and no vendor call.
- A scripted fake adapter can drive end-to-end tests.
- Both CLIs release often; an adapter is only valid against the version it shipped with (ADR-0011).
- Because the adapter names the hosts its CLI needs, a Project never lists vendor hosts in its egress allowlist, and those hosts are opened only for the container that runs that agent (ADR-0008).
- Live progress inside a step is not available in v1, because the stream is parsed after the container exits.
