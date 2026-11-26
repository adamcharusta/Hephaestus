# Release Scope

Three releases, accepted by the Owner. Each is usable on its own and builds on the previous one. Requirement ids refer to `requirements.md`.

## v0.1 — Ignite the Forge: one issue to one reviewed pull request

One Project, started on demand. A labelled issue becomes a validated, independently reviewed pull request, and the Owner follows it on the issue and in the Dashboard.

| Area | Requirements |
| --- | --- |
| Project setup and configuration | FR-1 to FR-5 |
| Task intake | FR-6 to FR-11 |
| Execution | FR-14, FR-16 to FR-22 |
| Agents | FR-24 to FR-26, FR-30 |
| Validation, Review, Correction | FR-31 to FR-35 |
| Pull request | FR-36 |
| Escalation by comment | FR-48, FR-49 |
| Visibility | FR-52, FR-53 |
| Distribution | FR-58 to FR-60 |
| Methodology, including BMAD Method support and issues from Stories | FR-61 to FR-66 |
| Dashboard, read-only seed: Projects, configuration, Tasks, Task history | FR-67, FR-68 |
| Language: English by default, Polish selectable | FR-69 |

## v0.2 — Unattended: the forge runs on its own

| Area | Requirements |
| --- | --- |
| Schedules and several Projects | FR-12, FR-13, FR-15 |
| Usage Limits | FR-27 to FR-29 |
| Notifications | FR-50, FR-51 |
| Control and insight | FR-23, FR-54, FR-55 |
| Dashboard gains Schedules and windows, Agent capacity and Handoffs, status, the statistics page | FR-67 |

## v0.3 — Closing the loop: stacks, merge, rework

| Area | Requirements |
| --- | --- |
| Stacks | FR-37 to FR-40 |
| Approval and merge | FR-41 to FR-44 |
| Rework | FR-45 to FR-47 |
| Task authoring aid | FR-56, FR-57 |
| Dashboard gains pull requests awaiting approval and Stacks | FR-67 |

## Cross-cutting requirements

NFR-1 to NFR-18 hold from v0.1, with these timings:

- **NFR-4.** Egress is open in v0.1 unless an allowlist is set; the allowlist becomes the default before Hephaestus is announced for general use. The enforcing mechanism, an internal network behind an egress proxy, is built in the v0.1 execution epic.
- **NFR-5.** Duration, processor, and memory limits apply from v0.1. Spend limit accounting is decided in v0.2.
- **NFR-18.** The branch rules and the coverage threshold in the build apply from the first story that adds code. The SonarQube Cloud quality gate applies from the story that sets it up, early in v0.1.

## Ordering

- **Injection spike** was the first story of v0.1. It is done and confirmed how Agent CLIs reach a step container (AD-7, AD-8).
- **BMAD spike** comes before the Methodology epic. v0.1 depends on it; schedule it first.
- **Usage Limit spike** comes before FR-27 to FR-29 are designed in v0.2.
- Recommended when epics are written: carve a proof-of-concept milestone inside v0.1.
- **Design of the Dashboard, the command line, and text on GitHub** comes before the first story that adds a command. No dashboard work is on the proof-of-concept path.

## Requirements that reach across releases

- FR-35 and FR-48 (v0.1) say the Owner is notified, but Notifications (FR-50) arrive in v0.2. Until then the notifier is a no-op and the Owner learns through issue comments (FR-52).
- FR-10 ships in v0.1 in a narrow form: without Stacks, a dependency is satisfied only when its pull request has been merged into the Base Branch, because a dependent Task branches from the Base Branch. v0.3 relaxes this to "has produced a branch to build on".
- FR-7 (v0.1) lets a Task name a Fallback Agent, but Handoff (FR-28) arrives in v0.2. In v0.1 the Task's second Agent serves as the Reviewer.
- FR-20 (v0.1) preserves work on a Usage Limit interruption; detecting the limit as such (FR-27) arrives in v0.2.
- The command verb set grows by release: `confirm` in v0.1; `rework` in v0.3.
- FR-67 ships in v0.1 as a seed and gains views in every release, when that release gives the Owner something new to see. The statistics page of FR-55 arrives with the metrics in v0.2. The Dashboard is read-only in all three releases.

## Deferred beyond these releases

The architecture spine's Deferred table lists each postponed design decision with the epic that settles it. Everything out of scope for all three releases is in `SPEC.md` under Non-goals.
