---
title: 'Story 2.10: Answer on the issue to resume the Task'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '2f7c7a7'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A Task in `WaitingForOwner` with an open Escalation has no way back: a Trusted User's reply on the issue is applied with no change, the poller does not carry the comment's text, and nothing records which comments Hephaestus may use as context (FR-49, AD-1, AD-15, NFR-1).

**Approach:** The poller carries the text of a created comment on its Signal. The loop, deciding that Signal, admits the comment of a Trusted User on a Task's issue: it records the comment with its hash among the Task's admitted comments, and, when the Task waits for a `trusted-reply` and the comment is later than the Escalation, records it as the answer, closes the Escalation as answered, returns the Task to `Queued`, and journals the state label, all in one transaction. A comment whose first line begins with `/hephaestus` is a command (Story 2.11) and is never an answer or context here.

## Boundaries & Constraints

**Always:**
- Only the loop writes the admitted comment, the answer, and the move, through `IUnitOfWork`, in the transaction that applies the Signal.
- Trust is decided by the one admission function, by numeric id. A comment from anyone who is not a Trusted User is rejected, recorded, and not replied to (Story 2.5); the Task stays as it is. Hephaestus's own identity is never admitted.
- A Trusted User's comment on the issue of a Task that is not waiting for a reply is admitted as context and changes no state.
- A comment made before the Escalation was raised is context only, never the answer.

**Never:**
- Parse `/hephaestus` verbs, handle `confirm`, or detect edits (Story 2.11); withdraw or reopen (Story 2.3).
- Admit a comment on an issue that is no Task, or a command.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Answer | Task `WaitingForOwner`, open `trusted-reply` Escalation, Trusted User comments after it was raised | comment admitted as the answer, Escalation `answered`, Task `Queued`, label intent | N/A |
| Stranger | same state, comment by a user who is not trusted | Signal rejected `actor-not-trusted`, Task waiting, no comment admitted, no reply | N/A |
| Own identity | comment by Hephaestus's App account | applied, nothing admitted, Task waiting | N/A |
| Not waiting | Trusted User comments on a `Queued` or `Running` Task | admitted as context, state unchanged | N/A |
| Command | first line begins `/hephaestus` | applied, nothing admitted, Task unchanged | N/A |
| Stale | Trusted User comment older than the open Escalation | admitted as context, Task still waiting | N/A |
| No Task | Trusted User comments on an issue that is no Task | applied, nothing recorded | N/A |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Signals/Signal.cs` -- `CommentBody` on the Signal; `src/Hephaestus.GitHub/GitHubEvents.cs` reads `body`.
- `src/Hephaestus.Core/Tasks/` -- `AdmittedComment`, `CommentContent` (hash, command test), `TaskLifecycle` row for an answer.
- `src/Hephaestus.Core/Escalations/Escalation.cs` -- outcome `Answered`; `Ports/IUnitOfWork.cs` (`SignalApplication.AdmitComment`), `IReadStore.cs` (`ListAdmittedCommentsAsync`).
- `src/Hephaestus.Core/Loop/OrchestratorLoop.cs` -- `DecideComment`.
- `src/Hephaestus.Persistence/` -- `Signals.CommentBody`, `AdmittedComments` table, migration, unit of work, read store, signal inbox.
- `docs/escalation.md`, `docs/intake.md`, `README.md` if the reader needs it.

## Tasks & Acceptance

**Execution:**
- [x] Core: signal field, admitted comment, outcome, ports, loop decision, tests.
- [x] GitHub: read the comment body, test.
- [x] Persistence: column, table, migration, writes and reads, tests.
- [x] Host: end-to-end test, documentation.

**Acceptance Criteria:**
- Given each acceptance criterion of Story 2.10 in `epics.md`, then a test named for it passes.

## Implementation Notes

- Unattended run: no human gate. The answer is the comment itself, recorded with the id of the Escalation it answers; "the answer among its admitted comments" is `AdmittedComments` for the Task. Text of comments from users who are not trusted is held in the Signal inbox only, as every Signal's data is, and is never admitted.

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| README says the answer is "attached" though no Agent reads it yet | low | patch | Reworded to "recorded as the answer among its admitted comments". |
| Command hidden behind a zero-width character or BOM is read as an answer | medium | patch | `IsCommand` skips format characters; tests added. |
| Tautological assertions in the answer tests | low | patch | Removed. |
| Edited reply dropped with no word on the issue | medium | defer | Story 2.11 owns edits; `deferred-work.md`. |
| Reply compared with the host clock at `RaisedAt` | low | defer | Skew far above a human's reaction time only; `deferred-work.md`. |
| Empty or whitespace-only reply resumes the Task | false | none | GitHub refuses a comment with no text. |
| `/hephaestusfoo` or leading blank lines read as command | false | none | The criterion is "first line begins with `/hephaestus`"; GitHub trims comment bodies. |
| Move and end of Escalation unconditional in `ApplyAsync` | false | none | The loop is the only writer and applies one Signal at a time; same as Story 2.9. |
| Bodies of strangers' comments kept in the inbox; no cap | low | none | Every Signal's data is kept; the issue body is stored the same way; the note in the spec records it. |
| Trust judged when the Signal is applied | false | none | The same rule as every other Signal (AD-15). |
| Extra reads for comments that cannot be admitted | low | none | Two indexed reads per comment; guarding them adds branching for no user-visible gain. |
| Comments admitted on Tasks in an end state | low | none | Opting in again reuses the Task, so the context is still wanted. |
| Pre-upgrade pending comment Signal has no body | low | none | No release exists before this migration. |
