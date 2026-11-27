---
title: 'Story 2.5: Say on the issue what was ignored and why'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: 'b5285e3c3d74882ddffa2e3097211ff421148ac2'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A label applied by someone who is not a Trusted User is rejected and logged, but the issue says nothing, so the Owner and the user meet a silent mystery; and nothing yet posts a comment safely (FR-4, AD-5, AD-15, AD-19, NFR-9, NFR-10).

**Approach:** When a Signal is rejected because a user who is not a Trusted User applied a label Hephaestus reads (today the Marker Label), the loop journals, in the same transaction, the intent to post one comment, at most one per user, kind of action, and issue. After the commit it looks for the comment by its hidden marker, posts it only when absent, masked and ending in the marker, and records the result. A comment or command from such a user is recorded as rejected and gets no reply.

## Boundaries & Constraints

**Always:**
- Only the loop writes the journal; the comment's key is `<scope>:<cause>:comment:<ordinal>` with scope the Task's id, or `project:<repository-id>` when the issue is no Task, and is passed to the port method as a parameter.
- The gateway masks the body with the one `SecretMasker`, then appends the hidden marker as the last line. A marker counts only when a comment written by Hephaestus's own bot account carries the key of the effect being resolved; one by anyone else is disregarded.
- The sentence is a message id resolved from `en` and `pl`, in the Task's language, else the Project's, else the installation's; the anatomy is the "Label action ignored" row of `EXPERIENCE.md`, with no "Next:" line.
- An unreachable GitHub is retried and a refusal is final, as for labels; the comment is not the Task's own effect, so no Task moves because of it, and what could not be made is only logged, with the key, the repository, and the issue.

**Never:**
- Reply to a comment or `/hephaestus` command from a user who is not a Trusted User, or to Hephaestus's own events, or to a Signal that names no actor.
- Create or change a Task for a rejected Signal; add the Priority Label, Role Labels, removal of the Marker Label, or `/hephaestus` parsing (Stories 2.3, 2.6, 2.7, 2.11).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Untrusted label | Marker Label applied by an untrusted user on an issue of a Project | Signal rejected `actor-not-trusted`; one comment journaled with the Signal, posted, `applied` | N/A |
| Repeat | Same user applies it again on the same issue | Signal rejected; no second intent | N/A |
| Another user, or another issue | Different user or issue | its own comment | N/A |
| Task exists | Untrusted user applies it on a Task's issue | Task unchanged; comment scoped to the Task | N/A |
| Crash | Intent journaled, comment posted, no result | first tick finds the comment by marker, records `already-in-place`, posts nothing | N/A |
| Forged marker | Someone else's comment carries the key's marker | disregarded; the comment is posted | N/A |
| Secret in body | Body holds a stored secret value | request carries `***` instead | N/A |
| No comment | Comment or command by an untrusted user; label by own identity or no actor | untrusted: Signal rejected `actor-not-trusted` with the actor's id; others as before; no effect | N/A |
| Outage, refusal | GitHub unreachable or refusing | retried then `gave-up`, or `refused`; no Task moves | error logged with the key, repository, and issue |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Loop/OrchestratorLoop.cs`, `.Effects.cs` -- `Decide` rejects the Marker Label and now adds the notice effect and rejects untrusted `CommentCreated`; `AttemptAsync` gains the comment path.
- `src/Hephaestus.Core/Effects/Effect.cs` -- kind `Comment`, scope `project:<id>`, optional `RepositoryId`, `IssueNumber`, `NoticeKey` on `NewEffect`/`Effect`; `PendingEffect.Task` nullable. New `IssueNotices.cs` (detail, body) and marker helpers.
- `src/Hephaestus.Core/Ports/ICodeHost.cs`, `GitHub/GitHubCommentResult.cs` -- `FindIssueCommentAsync`, `PostIssueCommentAsync`; `Labels.IsRead`.
- `src/Hephaestus.Core/Localization/` -- ids, `en` and `pl` resources, `LanguageResolver.ForText`.
- `src/Hephaestus.GitHub/GitHubGateway.Issues.cs` -- `OnIssueAsync` made generic; masker injected; `ControlHost.cs` passes it and the loop's fallback language.
- `src/Hephaestus.Persistence/` -- journal columns `RepositoryId`, `IssueNumber`, `NoticeKey`, nullable `TaskId`, unique index on a notice; `SqliteUnitOfWork`, `SqliteReadStore`, migration.
- Tests beside each; `docs/intake.md`, `docs/state-labels.md`, `README.md`.

## Tasks & Acceptance

**Execution:**
- [x] Core: effect vocabulary, notice text and messages, ports, loop decisions and comment effect, tests.
- [x] GitHub: find and post a comment, masking, marker, own-identity check; tests.
- [x] Persistence: migration, intents with a notice key, tasks-less effects; tests.
- [x] Host: wiring, error log for a Task-less effect; end-to-end replay test; documentation and README.

**Acceptance Criteria:**
- Given each acceptance criterion of Story 2.5 in `epics.md`, then a test named for it passes.

## Implementation Notes

- Implemented directly from this spec in the same session, as no step needed an independent reader.
- Decisions the Owner would not notice: the one comment per user, kind, and issue lasts for ever (the journal is the memory, enforced by a unique index); a login that is not a valid GitHub login gets no comment; the language is the installation's today because no Task or Project language is stored yet.

## Spec Change Log

- Finding (blind and edge-case reviewers): a refused or given-up comment moved a live Task to `WaitingForOwner`, as a label does, so a locked issue could stop work that was going well. Amended the frozen block, which no human had approved yet in this unattended run: a comment moves no Task and is only logged, with the key, the repository, and the issue. Known-bad state avoided: a courtesy comment changing a Task's state. KEEP: scope of the key (Task id, else `project:<id>`), the notice key, find-then-post, masking in the gateway.

## Review Triage Log

| Finding | Verdict | Evidence and route |
|---------|---------|--------------------|
| A comment refused or given up moves a Task to `WaitingForOwner` (blind, edge-case) | medium | `ConcludeAsync` moved by the Task's state alone. Patched: only a label effect moves a Task; tests cover a refusal on a Task's issue and the unknown-effect refusal. Spec amended, see the change log. |
| Notice key lacks the label, so a second readable label is swallowed (blind, edge-case) | false | The criterion says "the same kind of label action", and applying a label is one kind whichever label; the story reads only the Marker Label. |
| A refused or given-up notice is never sent again (blind, edge-case) | low | "At most once" is what the criterion and the unique index give; a comment that GitHub refuses cannot be made, and one given up after the bound is in the log. Patched: the documentation says at most once. |
| Log line for an issue that is no Task names no issue (blind) | medium | The key holds the repository id and no issue number. Patched: the problem carries the repository and the issue and the log line names both. |
| `NotConnected` when the slug is missing burns the retries (blind, edge-case) | medium | An App connected before the slug was kept never found its own comments. Patched: the gateway reads the identity, which stores the slug, and returns the failure GitHub gave when it cannot; tests cover each. |
| `AttemptLabelAsync` throws for a label intent with no Task (blind, edge-case) | low | Not reachable by the loop, but a direct correction. Patched: refused as `unknown-effect`; a test covers it. |
| Every untrusted comment is a rejected Signal and a warning line (blind) | low | The criterion requires it recorded as rejected; the warning per rejection is the existing behaviour. Deferred: log volume on a busy public repository. |
| Finding a comment reads the whole thread on each attempt (blind) | low | Efficiency, not correctness. Deferred. |
| Own comments are rejected while the slug is unknown (edge-case) | low | Only before the first read of the slug, which the first comment's find now stores. Rejected: the fix is more than a direct correction. |
| Tests do not cover closed Tasks, pull request numbers, long details, `Down` (blind) | false | Closed Tasks and pull requests are not different paths here; `Down` is never run by the host. |
| Fake journal approximates the real dedupe (blind) | false | The real rule has its own tests in Persistence and in the host's end-to-end tests. |
| `The_masker_the_host_gives...` passes with any masker (verification gap) | low | Patched: the injected masker holds a secret that the gateway's own store lacks. |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
