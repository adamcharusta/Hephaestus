---
title: 'Story 3.13: Publish one pull request per Task'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: '5781ae5cc89038b58e7e1be45f3e20eaa90b52fc'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A Task whose branch is pushed ends nowhere: nothing opens its pull request, describes what was done and how it was checked, or moves the Task to `PullRequestOpen` (FR-36, AD-5, AD-19).

**Approach:** Publication is a journaled effect of the loop, through a gateway port only `Hephaestus.GitHub` implements. Core composes the description from the facts the intent carries, neutralising what an Agent wrote; the gateway finds the branch's pull request by its head branch before it opens one and masks secrets last.

## Boundaries & Constraints

**Always:**
- One pull request from the Task branch to the Base Branch, under Hephaestus's own identity, linked to the Task's issue; the result moves the Task `running` to `pull-request-open` with the intent of its label in the same transaction.
- Sections What changed, Task, How it was checked, Who did what, in that order; an empty one is left out; Agent, Model, Effort, and each Validation command's result are composed by Core from recorded data.
- Agent text: HTML comments escaped, mentions and closing keywords broken, commands fenced, stored secret values masked.
- An existing pull request of the head branch is updated, never duplicated, after a crash or a later publication.
- It is an effect of the loop, not a step run; it holds no slot.

**Never:**
- Add a step-run table, journal the intent from the loop's dispatch (Story 3.14), draft pull requests, Reviewer, Findings, plan, or Stack sections (Epics 4 and 11), or change the epics, the architecture, or the workflow.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| First publication | Task `running`, no pull request | Opened; Task `pull-request-open`; label intent journaled | N/A |
| Crash after open | Pull request exists, no result | Found by head branch, updated, result recorded; one pull request | N/A |
| Published again | Task `pull-request-open`, new intent | Same pull request updated; no move | N/A |
| Ended or waiting Task | Cancelled, Failed, Queued, WaitingForOwner... | `superseded`; nothing sent | N/A |
| Outage / rate limit | GitHub unreachable or asks to wait | Retry at bounded delays; a wait is no failure | At the bound: GaveUp and an Escalation |
| Refused | 422, 403 | Refused; Escalation names the pull request | N/A |
| Hostile text | Marker, mention, `closes #1`, `/hephaestus` in a summary | Escaped, broken, fenced | N/A |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/PullRequests/` (new) -- `PullRequestFacts`, `PullRequestIntent`, `PullRequestDescription`.
- `src/Hephaestus.Core/GitHub/PullRequestPublication.cs`, `Ports/IPullRequests.cs` -- the request, the result, the port.
- `src/Hephaestus.Core/Effects/Effect.cs` -- `EffectKind.PullRequest`, `Payload`, `TaskMove`; `Loop/OrchestratorLoop.Effects.cs` -- `AttemptPullRequestAsync`.
- `src/Hephaestus.GitHub/GitHubGateway.PullRequests.cs`; `src/Hephaestus.Persistence` -- the `Payload` column and the move in `RecordEffectAsync`.
- `docs/pull-requests.md`, `docs/adr/0029-*.md`, `README.md`, `EXPERIENCE.md`.

## Tasks & Acceptance

**Execution:**
- [x] Core types, the port, the loop effect, the gateway, the persistence change, tests.
- [x] Documentation, ADR, the closing-keyword default in `EXPERIENCE.md`, story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| Pull request from the Task branch to the Base Branch, Task moves, label follows | `OrchestratorLoopPullRequestTests`, `PullRequestJournalTests`, `GitHubPullRequestTests` |
| Description states what changed, Agent, Model, Effort, each command's result | `PullRequestDescriptionTests`, `PullRequestFactsTests` |
| Agent text neutralised, secrets masked | `PullRequestDescriptionTests`, `GitHubPullRequestTests` |
| Crash: found by head branch, no second pull request | `OrchestratorLoopPullRequestTests`, `GitHubPullRequestTests` |
| Published again updates the same pull request | `OrchestratorLoopPullRequestTests`, `GitHubPullRequestTests` |
| Effect of the loop, not a step run | `OrchestratorLoopPullRequestTests` |
| Sections and order; closing keyword decided and recorded | `PullRequestDescriptionTests`; `EXPERIENCE.md` |

## Implementation Notes

- Unattended run: the closing keyword could not be settled with the Owner. The default is no keyword (`Issue #7`), recorded in `EXPERIENCE.md` as an `[ASSUMPTION]`, in the memlog, and in ADR-0029; the pull request says so and asks the Owner to answer there. `SPEC.md` still lists the question as open and is derived, so it changes only through `bmad-spec`.
- The facts travel as the intent's payload (a nullable journal column, migration `AddPullRequestPayload`), so a restart composes the same text. Nothing journals the intent yet; Story 3.14 does when Validation has passed and the branch is pushed.
- `IPullRequests` is its own port, implemented by `GitHubGateway`, so the `ICodeHost` fakes did not change.
- The pull request's number is not stored; it is found by its head branch.
