# ADR-0012: Methodology as data

- **Status:** Accepted (reading Project-supplied files from the Base Branch was added by review)
- **Date:** 2026-11-25
- **Spine:** AD-14

## Context

Agents from different vendors have different habits. The Owner wants every agent working on a Project to follow the same instructions for the same step, so that a handoff or a review does not produce two styles in one pull request. BMAD Method is the first methodology supported beyond a built-in default; it must stay optional, and the orchestration core must not depend on it.

## Decision

A methodology is a directory containing a manifest and one instruction template per agent step kind: readiness, planning, implementation, review, correction, rework. `default` and `bmad` ship with Hephaestus.

- The core selects a template by step kind only. It never branches on the methodology's name or on which agent is running.
- Templates contain no substitution. They refer only to the fixed context file paths (ADR-0005). The same `instruction.md` goes to whichever agent performs the step.
- Each step run records the methodology name and template identity that were applied.
- Files a Project supplies for its methodology, such as BMAD's workflows, are read from the Base Branch at the commit of the Task's configuration snapshot and mounted read-only at `/hephaestus/methodology`. Templates refer to that path, never to a copy in the working tree.

## Alternatives considered

- **Methodologies as code** (a class per methodology). Every new methodology would change the orchestrator. Rejected.
- **Let each CLI discover methodology files in the working tree.** The implementing agent could rewrite the review workflow that the Reviewer then follows. Rejected in review.

## Consequences

- Adding a methodology changes no scheduling, validation, or approval code.
- A Project with no methodology files completes a Task with the built-in default.
- Which BMAD workflow serves each step kind, and whether those workflows complete unattended when mounted outside the working tree, is unknown until the BMAD spike is run.
