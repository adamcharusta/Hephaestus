# ADR-0001: Modular monolith with hexagonal boundaries enforced by project references

- **Status:** Accepted
- **Date:** 2026-11-25
- **Spine:** AD-3

## Context

Hephaestus runs for one person on one machine and must stay light. It also has to keep vendor-specific logic (two agent CLIs, GitHub, Docker) out of its orchestration logic, so that a third agent or a second task provider can be added without redesign. Most of its code will be written by coding agents working on one feature at a time, which makes accidental coupling more likely than in a hand-written codebase.

## Decision

One process, structured as a core with adapters around it. The core defines ports; adapters implement them. The boundaries are six .NET projects, and project references are the rule:

- `Hephaestus.Core` references no adapter and no infrastructure package.
- `Hephaestus.Agents`, `Hephaestus.GitHub`, `Hephaestus.Workspaces`, and `Hephaestus.Persistence` reference only Core.
- `Hephaestus.Host` references everything and wires it together.

Two adapters cooperate only through Core.

## Alternatives considered

- **A single project with folders and architecture tests.** Fewer files, but the boundary is then only a test that can be disabled or left out of date. Rejected.
- **Microservices or separate orchestration and execution services.** No requirement justifies the operational cost for a single-user tool. Rejected.

## Consequences

- The compiler rejects a dependency in the wrong direction.
- Six projects is more than the brief's caution against fragmentation would suggest; each corresponds to one boundary the design depends on, and no further projects are added for methodologies or notifications in v0.1.
- The core is testable with no Docker, GitHub, or database.
