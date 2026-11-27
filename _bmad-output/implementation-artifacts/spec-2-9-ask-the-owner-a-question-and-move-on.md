---
title: 'Story 2.9: Ask the Owner a question and move on'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '6578846'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** When Hephaestus cannot decide, or an effect on an issue ends at its retry bound, the Task moves to `WaitingForOwner` and the Owner is told nothing on the issue; there is no Escalation to answer, no record of what is asked, and nothing that lets a waiting Task leave the state by a Trusted User's removal of the label or close of the issue (FR-48, AR-63, AD-1, AD-5, AD-19, UX-DR-18).

**Approach:** One path in Core raises an Escalation for a Task: it records the Escalation and its resume condition, moves the Task to `WaitingForOwner`, and journals, in the same transaction, the intents of the state label, the comment, and the call to `INotifier`. The comment has the fixed shape of `EXPERIENCE.md`, with the Agent's text neutralised. An effect that reached its retry bound raises the same Escalation, saying which effect it was. A trusted removal of the Marker Label cancels a waiting Task and a trusted close closes it.

## Boundaries & Constraints

**Always:**
- Only the loop writes the Escalation and the Task's move, through `IUnitOfWork`; the comment and the notification are journaled effects with keys `<task>:<cause>:<kind>:<ordinal>`, attempted after the commit; the comment is found by its marker before it is posted.
- One Escalation asks for one decision. A Task that is already `WaitingForOwner`, or is in no state that moves, is not escalated again.
- The comment is: **Hephaestus needs a decision.**, the decision in one sentence, the options lettered one per line, how to answer; the sentences are message ids in `en` and `pl`. Text that came from an Agent has HTML comments escaped, mentions and issue-closing keywords broken, and command keywords fenced, then the gateway masks secrets.
- The `INotifier` call is journaled, at most once, and its failure or silence changes no Task. The Host implementation does nothing.
- Only a waiting Task leaves `WaitingForOwner` by a trusted removal of the Marker Label (to `Cancelled`) or a trusted close (to `Closed`); its open Escalation is closed with that outcome. Anyone else's removal or close changes nothing.

**Never:**
- Resume a Task on an answer, parse `/hephaestus` commands, or withdraw or close Tasks in other states (Stories 2.10, 2.11, 2.3), or order dispatch (Story 2.7).
- Let a Task in `WaitingForOwner` hold back another eligible Task.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Decision | Running Task, a decision and options | Escalation recorded, Task `WaitingForOwner` with resume condition `trusted-reply`, intents of label, comment, notification | N/A |
| Already waiting | Task in `WaitingForOwner` or an ended state | nothing recorded | returns false |
| Retry bound | Label effect gave up or was refused | same path; the comment names the label and the reason | comment itself failing is logged, Task keeps `WaitingForOwner` |
| Agent text | HTML comment, `@user`, `Fixes #3`, `/hephaestus confirm` in a decision or option | escaped, broken, fenced | N/A |
| Notifier throws | `INotifier` fails | result `gave-up`, logged, no Task change | N/A |
| Crash after notifying | intent started, no result | not sent again; ended as `gave-up` `interrupted` | N/A |
| Removal or close | Trusted User removes the Marker Label or closes a waiting Task's issue | `Cancelled` or `Closed`, Escalation closed | others: no change |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Escalations/` -- `Escalation`, `EscalationRequest`, `EscalationPlan`, `EscalationPath` (the single path), `EscalationComment` (shape and text).
- `src/Hephaestus.Core/Effects/AgentText.cs` -- neutralises Agent text (AD-19); `Effect.cs` gains `Notification`, `EffectRecording.Escalation`.
- `src/Hephaestus.Core/Ports/INotifier.cs`, `IUnitOfWork.cs` (`EscalateAsync`, `SignalApplication.MoveTaskTo`), `IReadStore.cs` (Escalations).
- `src/Hephaestus.Core/Loop/` -- `RaiseEscalationAsync`, effect conclusion through the path, comment and notification attempts, removal and close of a waiting Task.
- `src/Hephaestus.Core/Tasks/` -- `DispatchPolicy` (a waiting Task is not eligible and holds nobody back), lifecycle rows for removal and close.
- `src/Hephaestus.Persistence/` -- `Escalations` table, migration, unit of work, read store.
- `src/Hephaestus.Host/Hosting/NoOpNotifier.cs`, wiring; `docs/escalation.md`, `docs/intake.md`, `docs/state-labels.md`, `README.md`.

## Tasks & Acceptance

**Execution:**
- [x] Core: Escalation types and path, neutraliser, comment text and messages, ports, loop, dispatch policy, tests.
- [x] Persistence: table, migration, writes and reads, tests.
- [x] Host: notifier, wiring, end-to-end test, documentation and README.

**Acceptance Criteria:**
- Given each acceptance criterion of Story 2.9 in `epics.md`, then a test named for it passes.

## Implementation Notes

- Unattended run: no human gate; choices the Owner would not notice are decided here. The resume condition is recorded on the Escalation as `trusted-reply`. The Dispatch policy added here is only the eligibility rule (`Queued` is eligible, nothing else), which Stories 2.7 and 2.8 extend with order and dependencies.

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
