---
title: 'Story 2.2: Opt an issue in with the Marker Label'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: 'be558276f4a6c4f55b3e821868595bc86b3fee04'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The inbox fills with Signals and nothing reads them, so a Trusted User's `hephaestus` label opts nothing in, and nothing keeps anyone else from trying (FR-6, AD-1, AD-2, AD-15, AR-42).

**Approach:** The orchestrator loop, the only writer of Task state, applies pending Signals in inbox order, one transaction each through `IUnitOfWork`. A Marker Label event whose actor one Core admission function finds trusted by numeric id creates a `Queued` Task with a snapshot of the issue and a branch name derived once; every other Signal is recorded with a reason and changes no Task. The API reports the loop's liveness.

## Boundaries & Constraints

**Always:**
- Intent comes from actor-attributed Signals only, never from the labels an issue carries now. Trust is decided by numeric id in one Core function; a login decides nothing. Hephaestus's own bot account is never admitted.
- One transaction per Signal marks it applied or rejected and writes the Task and snapshot it causes. A rejection keeps the acting user's id and a reason. A Signal left unapplied when the host stopped is applied by the first tick.
- The snapshot is the title and body the labelled event carried, with their hash. The branch is `hephaestus/<issue-number>-<slug>`, derived once and stored; the slug is lower-case ASCII letters, digits, and single hyphens, at most 40 long; no slug leaves `hephaestus/<issue-number>`.
- Only the loop names `IUnitOfWork`, which an architecture test asserts. `GET /api/health` and `hephaestus health` report whether the loop ticked within 30 seconds and when. Line coverage stays at or above 80% overall and on new code. No new package.

**Never:**
- No withdrawal, close, opt-in again, state label, comment, or reply on GitHub; no Role Label, Priority Label, dependency, dispatch, `Running` state, effect journal, or Dashboard view (Stories 2.3 to 2.12). No change to the poller's rule that it decides no trust, to `SPEC.md`, the architecture, `epics.md`, an ADR, `_bmad/`, or skills.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Trusted opt-in | `labeled` `hephaestus` by a Trusted User, issue not a Task | `Queued` Task, snapshot with hash, branch; Signal applied, names the Task |
| Untrusted opt-in | same, actor not in the list (even with a trusted login) | no Task; Signal rejected `actor-not-trusted`, actor id kept |
| No actor | actor null in the event data | no Task; rejected `actor-unknown` |
| Own identity | actor login is the App's bot account | no Task; rejected `own-identity` |
| Label with no event | issue carries the label, no Signal | no Task |
| Other Signals | other label, unlabel, close, reopen, comment | applied with no change |
| Already a Task | second marker event for the issue | no second Task; applied, names the Task |
| Not a Project | repository not registered or removed | rejected `project-not-registered` |
| No title | Signal without an issue title | rejected `snapshot-missing` |
| Title with no slug | `!!!`, or only non-ASCII | branch `hephaestus/<n>` |
| Restart | Signal appended, host stopped before applying | first tick applies it |
| Apply fails | the database refuses the write | the Signal stays pending, tried again; after three failures over five minutes set aside as `apply-failed` |
| Health | loop ticking / stopped / not yet ticked | alive and time / not alive and time / not alive, no time; CLI exit 1 when not alive |

</frozen-after-approval>

## Code Map

- Spine AD-1, AD-2, AD-15; epics Story 2.2; `epic-2-context.md`; UX `EXPERIENCE.md` command table (`health`, Story 2.2).
- `src/Hephaestus.Core/` -- `Tasks/` (`TaskState`, `TaskRecord`, `IssueSnapshot`, `IssueContent`, `BranchName`), `Trust/Admission.cs`, `Signals/SignalStatus.cs` (status and rejection reasons), `Signal` (title, body), `GitHub/Labels.cs`, `Ports/IUnitOfWork.cs`, `ISignalInbox.ReadPendingAsync`, `IReadStore` (`FindTaskAsync`, `FindIssueSnapshotAsync`, `GetGitHubAppSlugAsync`), `IInstallationConfiguration.SetGitHubAppIdAsync` (slug), `Loop/` (`OrchestratorLoop`, `LoopHeartbeat`), message ids for `health`.
- `src/Hephaestus.Persistence/` -- `Tasks`, `IssueSnapshots`, Signal status columns and index, `AppSlug`; migration `AddTasks`; `SqliteUnitOfWork`; inbox, read store, configuration changes.
- `src/Hephaestus.GitHub/` -- `GitHubEvents` carries title and body on labelled events; the gateway stores the slug on connect and refreshes it on check.
- `src/Hephaestus.Host/` -- `OrchestratorService`, `ControlHost`, `Api/HealthApi`, `health` command.
- `docs/intake.md` (new), `docs/polling.md`, `README.md`. Tests beside each project.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/**` -- Task model, admission, branch name, hash, `IUnitOfWork`, the loop, the heartbeat
- [x] `src/Hephaestus.Persistence/**` -- tables, migration, unit of work, reads, pending read
- [x] `src/Hephaestus.GitHub/**` -- title and body on labelled events, App slug
- [x] `src/Hephaestus.Host/**` -- loop service, health endpoint and command
- [x] `docs/intake.md`, `docs/polling.md`, `README.md` -- what the loop does, the snapshot, the branch, health
- [x] `tests/**` -- one test group per matrix row, the `IUnitOfWork` architecture rule, the migration, an end-to-end test over the real database

**Acceptance Criteria:**
- Given a Trusted User applies the Marker Label to an issue of a registered Project, when the loop applies the Signal, then one transaction creates a `Queued` Task for the Project and issue number, with the title and body snapshotted with their hash and the branch stored.
- Given a user who is not a Trusted User applies it, then no Task is created and the Signal is rejected with their id; given an issue that carries the label with no labelled event behind it, then no Task exists.
- Given an event whose actor is Hephaestus's own identity, then no Task is created or changed.
- Given the code base, then an architecture test shows nothing but the loop depends on `IUnitOfWork`.
- Given a running container, when the Owner asks for its health, then the API reports that the loop is alive and when it last ticked.

## Implementation Notes

- 2026-11-27. Built directly in one unattended session; no question was put to the Owner. The choices the story leaves open:
  - **Snapshot content.** The poller already reads `issues/events`, whose elements carry the issue. A labelled Signal now carries `issue.title` and `issue.body` as GitHub showed them when the event was read. This is within one cycle of the label, not at the instant of it: an edit in between is in the snapshot. Story 2.11 detects edits by the hash; recorded in `deferred-work.md`. A Signal with no title (appended before this release) is rejected `snapshot-missing`.
  - **Own identity.** The loop needs the login of Hephaestus's bot account. The App's slug is stored with its id (`AppSlug`), written by `github connect` and, for an App connected earlier, by the next `github check`. The loop compares the actor's login with `<slug>[bot]` before it looks at the id. Trust is by numeric id; this login test only refuses.
  - **Signals the story does not act on** (unlabel, close, reopen, comments, other labels) are applied with no change, so they cannot stall the inbox; the stories that act on them start from their own Signals.
  - **Unit of work.** One method, `ApplyAsync(SignalApplication)`, commits the status change and the new Task and snapshot together, and does nothing for a Signal that is no longer pending.
  - **Loop timing.** One-second tick, at most 500 Signals a tick in batches of 50, and a Signal is set aside only after three failures spanning five minutes. They are constants of the loop, not Installation settings.
  - **Liveness.** `LoopHeartbeat` in memory: beat after each applied Signal and at the end of a tick with no failure; alive within 30 seconds. `health` exits 1 when not alive.
  - **Migration.** Written by hand like the earlier ones; `The_migration_set_matches_the_model` checks it. Signals from before it are `pending`.
  - **Not done, by design:** state labels, replies, withdrawal, the change feed, a Dashboard view.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-27. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap (no findings).

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | Three failures in about three seconds of a locked or unavailable database rejected a valid opt-in as `apply-failed` | high | BH, ECH; the count was per tick with a one-second tick | patch: the failures must also span five minutes; tests for a short outage and for the window |
| 2 | The heartbeat was taken at the start of a tick, so a loop that failed on every tick was reported alive | medium | BH, ECH | patch: it beats after each applied Signal and at the end of a tick with no failure; tests |
| 3 | The write transaction began with a read, so a Signal appended meanwhile by the poller made SQLite refuse the upgrade at once | medium | ECH; deferred transaction, snapshot upgrade | patch: the first statement is the claiming `UPDATE ... WHERE Status = 'pending'` |
| 4 | A Signal set aside as `apply-failed` was not in the report, the rejected count, or the log | medium | ECH | patch: it is, and a test |
| 5 | A failure in a later batch dropped the counts and rejections of the earlier batch | low | BH | patch: the failure is caught inside the tick and the partial report returned; a test |
| 6 | A report that throws would end the loop | low | ECH | patch: the loop ignores what the report does; a test |
| 7 | A stored slug that GitHub changed was never refreshed; the check wrote without the credential lock and could undo a connection | medium | BH, ECH | patch: the check replaces a differing slug, under the lock and only for the same App id; tests |
| 8 | Slug folding mangled accented letters and split a word written with combining marks | low | BH, ECH | patch: combining marks are skipped and Latin letters with marks are mapped (normalization is a no-op under invariant globalization, tried); tests |
| 9 | The snapshot can hold an edit made between the label and the poll, and the comments called it vouched for | medium | BH | patch: wording; defer: closing the window belongs to Story 2.11, recorded in `deferred-work.md` |
| 10 | Nothing but a text rule keeps other Persistence code from writing Tasks | low | BH | patch: a rule over the writes of `Tasks` and `IssueSnapshots` |
| 11 | The container health check does not use the loop's health | low | BH | defer: recorded in `deferred-work.md` |
| 12 | A Marker Label on a closed issue creates a Task | maybe-false | BH; AD-2 reads intent from events, not from state | defer: recorded in `deferred-work.md`, stated in `docs/intake.md` |
| 13 | Rejection lines are English sentences built in Core and logged as one string | none | BH | rejected: log lines are machine surface and never translated (AD-21); named fields are a refinement |
| 14 | No foreign keys or CHECK constraints on the new tables | low | BH | rejected: one writer, and reads fail on a bad stored value; a constraint adds a migration risk for no demonstrated harm |
| 15 | Trust and Projects are read once per batch of 50, so a removal mid-batch is seen a batch late | low | ECH | rejected: AD-16 has the loop read configuration at the start of each tick; a batch is shorter than a tick |
| 16 | `health` exits 1 both when the loop is down and when the host cannot be reached | none | BH | rejected: the command line has three exit codes (UX "Output"); the sentence tells the cases apart |
| 17 | A backwards step of the clock reads as alive; the service test slept 100 ms | low | BH | patch for the test (waits for the tick); rejected for the clock: a step back is bounded by the next beat |
| 18 | No retention for Signals, which now carry an issue body | low | BH | defer: recorded in `deferred-work.md` |

Verification after the patches: `dotnet build` 0 warnings, 0 errors; `dotnet test` all passed; line coverage 95%+ overall.

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `python3 eng/check-licenses.py` -- expected: pass
