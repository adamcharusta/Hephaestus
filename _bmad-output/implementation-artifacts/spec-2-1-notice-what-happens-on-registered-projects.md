---
title: 'Story 2.1: Notice what happens on registered Projects'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: 'dcae3b8cf7b874fe2b5859abcd6e11e4b8683e46'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Nothing watches GitHub, so a label, a comment, or a close on a registered repository changes nothing in Hephaestus, and an installation behind a home router cannot receive webhooks (FR-1, FR-11, NFR-12, AD-1, AD-2, AD-4, AD-15, AR-43).

**Approach:** The gateway's poller, a background loop in `Hephaestus.GitHub`, asks GitHub for each registered Project's issue events and comments with conditional requests, appends one actor-attributed Signal per event to a persisted inbox, and only then advances the Project's cursor. It decides no trust and changes no Task.

## Boundaries & Constraints

**Always:**
- Events read: issue labeled, unlabeled, closed, reopened (issues, not pull requests); comment created and edited. Each is one Signal with a source (`github`), a source event id, GitHub's numeric id of the actor from the event data, never from content, and the repository id. A comment that is seen already edited still yields its creation Signal, so a creation is never lost. An edit names no actor, because the event data holds the author and not the editor (AD-15).
- The inbox refuses a second Signal with the same source and source event id without error; its order is a database-assigned integer sequence. The poller appends a batch first and advances the cursor second; the cursor and the entity tag of the first page are one record.
- A `304 Not Modified` leaves the cursor as it was. `X-Poll-Interval` and `Retry-After` are respected. A rate-limit answer (primary or secondary), from the poller or from any gateway call, closes one gate until the time GitHub states; no request is sent before it, the wait is temporary unavailability, and no event is lost.
- Only active Projects are read. A Project's streams start at the time Hephaestus began to watch it (registration), not at the beginning of the repository; removing a Project removes its cursors.
- The polling interval is the Installation setting `pollingInterval` (seconds, 10 to 3600, default 60), read on every cycle. The cycle is what FR-11 and FR-29 measure against; the documentation says so.
- The poller appends Signals and keeps cursors through `IPollCursorStore` and nothing else: it names no `IUnitOfWork`, no Trusted User, no Task. An architecture test asserts it. Line coverage stays at or above 80% overall and on new code. No new package.

**Never:**
- No Task, no admission, no Marker Label meaning (Story 2.2), no pull request events, no comment content or edit history (AD-15 and open question 4 belong to the stories that admit content), no webhook, no change to `ICodeHost`, `_bmad/`, skills, `SPEC.md`, the architecture, `epics.md`, or an ADR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Label, unlabel, close, reopen | issue events newer than the cursor | one Signal each, in id order, then the cursor moves to the newest id |
| Comment created or edited | comments updated since the cursor | creation Signal; edit Signal when `updated_at` differs; actor of an edit unknown |
| Seen again | same events returned again | appends are no-ops; one Signal each |
| Crash between append and cursor | Signals stored, cursor old | next cycle re-reads, appends nothing new, advances the cursor |
| Nothing changed | `304` | no append, cursor and entity tag unchanged |
| Pull request event | `issue.pull_request` present | no Signal; the cursor still moves past it |
| First poll, no cursor | Project just registered | only events from registration on are read |
| Not registered or removed | repository absent from the active list | never read; a removed Project's cursors are gone |
| GitHub unreachable | connection fault, 5xx | cycle ends, cursors intact, next cycle retries |
| Rate limit | 429, or 403 with remaining 0 or `Retry-After` | gate closed until the stated time; nothing sent before it; no Signal lost |
| Minimum poll interval | `X-Poll-Interval: 120` | that Project is not asked again before then |
| Not connected, App not installed | no credential, no installation holds it | nothing polled, nothing appended |

</frozen-after-approval>

## Code Map

- Spine AD-1, AD-2, AD-4, AD-15; epics Story 2.1; `epic-2-context.md`. Story 2.2 reads the inbox; this story only fills it.
- `src/Hephaestus.Core/Signals/Signal.cs` (new: `Signal`, `SignalKind`, `StoredSignal`), `Ports/ISignalInbox.cs`, `Ports/IPollCursorStore.cs` (new), `Settings/InstallationSettings.cs` (`pollingInterval`).
- `src/Hephaestus.Persistence/` -- `Signals` and `PollCursors` tables (one migration), `SqliteSignalInbox`, `SqlitePollCursorStore`; `SqliteInstallationConfiguration` writes the `since` cursor when a Project is registered and removes the cursors when it is removed; `PersistenceServiceCollectionExtensions` registers the two ports.
- `src/Hephaestus.GitHub/` -- `GitHubPoller` (cycle and loop), `GitHubInstallations` (installation tokens and the repositories each holds, cached), `GitHubEvents` (JSON to Signals), `GitHubRateLimit` (the shared gate); `GitHubGateway` consults the gate and closes it. Reuse `GitHubGateway.CreateClient`, `GitHubAppJwt`, `IsOutage`.
- `src/Hephaestus.Host/Hosting/` -- `ControlHost` registers the poller; `PollerService` runs it and logs the outcome of each cycle.
- `docs/polling.md` (new), `README.md`, `docs/github-app.md` if it describes permissions read.
- Tests beside each project; reuse `FakeGitHub`, `FakeStores`, `TempInstallation`, `RunningHost`.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/**` -- Signal types, ports, polling interval setting
- [x] `src/Hephaestus.Persistence/**` -- tables, migration, inbox, cursor store, cursors tied to registration and removal
- [x] `src/Hephaestus.GitHub/**` -- poller, installation cache, event mapping, rate-limit gate, gateway use of the gate
- [x] `src/Hephaestus.Host/**` -- host registration and the background service
- [x] `docs/polling.md`, `README.md` -- what is watched, the setting, the wait rules
- [x] `tests/**` -- one test group per matrix row, the architecture rule, the migration

**Acceptance Criteria:**
- Given a registered Project, when an issue is labelled, unlabelled, closed, or reopened or a comment is created or edited, then within one cycle exactly one Signal for it is in the inbox with its source, event id, and the actor's numeric id from GitHub's event data.
- Given an event already appended, then a second append is a no-op; given a crash after the append and before the cursor moved, then nothing is lost or doubled.
- Given nothing changed, then conditional requests answer `304`, the cursor stays, and a minimum interval GitHub returns is respected.
- Given a repository that is not registered or a removed Project, then it is never read; given GitHub unreachable or a rate limit, then the poller waits and loses no event.
- Given the Installation Configuration, then the polling interval is a setting with a documented default; given the code base, then an architecture test shows the poller only appends Signals and keeps cursors.

## Implementation Notes

- 2026-11-27. Built directly in one unattended session; no question was put to the Owner. The choices the story leaves open:
  - **Endpoints.** Issue events from `GET /repositories/{id}/issues/events` (newest first, paged until the cursor) and comments from `GET /repositories/{id}/issues/comments?sort=updated&direction=asc&since=` (the cursor less one second, because GitHub's times have a second's resolution). Both with `If-None-Match`; the entity tag of the first page is stored with the cursor in one record. Plain `HttpClient` over the gateway's transport factory, because Octokit's clients take no `If-None-Match`; installations, tokens, and repository lists still go through Octokit.
  - **Source event ids.** `issue-event:<id>`, `comment:<id>:created`, `comment:<id>:edited:<updated_at>`; the source is `github`.
  - **Comment edits name no actor.** The REST data holds the author and not the editor; AD-15 reads the editor from the edit history (open question 4), which is a later story. An edit's `ActorId` is null (never a Trusted User) and the author is kept as `CommentAuthorId`.
  - **Where reading starts.** A `since` cursor is written when a Project is registered, and every cursor of a Project is removed when it is removed; a Project registered before this release gets `since` from the migration, as the time of the upgrade. A stream without a cursor reads from there.
  - **Setting.** `pollingInterval`, whole seconds, 10 to 3600, default 60, read again at least every 15 seconds.
  - **Rate limit.** One `GitHubRateLimit` shared by the gateway and the poller, bounded at two hours; a gateway call made while it is closed answers `Unreachable` without a request.
  - **Pull requests.** Pull request events are skipped; a comment on a pull request is an issue comment to GitHub and is recorded like the rest.
  - **Migration.** Written by hand, in the format of the earlier ones (the EF design-time package is not a dependency); `The_migration_set_matches_the_model` checks it against the model.
  - **Not done, by design:** no Task, no admission, no trust decision, no retention of Signals, no pull request events (see `deferred-work.md`).

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-27. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | A changed interval was read only after the wait it was meant to end | medium | BH; the docs promised the next cycle | patch: the wait is taken in slices of at most 15 s that read the setting again; a test changes it mid-wait |
| 2 | A non-JSON 200, an issue number above `int.MaxValue`, or a failing stream aborted the cycle for every Project | high | BH, ECH; the cursor would never pass the bad item | patch: JSON fault and a refusal are that stream's trouble, the number is checked, counts survive; tests for each |
| 3 | A `X-RateLimit-Reset` too large for a date threw; `X-Poll-Interval` had no bound | medium | ECH | patch: both cut to two hours; tests |
| 4 | A gateway command that met a limit mid-cycle did not stop the cycle | medium | ECH | patch: the gate is checked before each request of the poller; test |
| 5 | Every `ApiException` was reported as a refused credential and forgot the tokens | low | BH | patch: the status is named, tokens are forgotten on 401 only |
| 6 | One refused installation stopped the load for all | medium | ECH | patch: skipped, the others are read; test |
| 7 | The failing exception was dropped from the log | medium | BH | patch: the report carries it, logged whole; tests |
| 8 | An edit from before registration gave an edit Signal (the one-second overlap) | low | ECH | patch: test |
| 9 | A Project registered before this release had no `since`; a re-registered one would read the gap | medium | ECH, VG | patch: the migration writes it for active Projects; test from the previous migration |
| 10 | The service test asserted nothing about the poller running; nothing tested that the host runs it or shares the gate | high | BH, VG | patch: counting store, hosted-service and shared-gate tests over the real host |
| 11 | The default handler was never disposed | low | BH | patch: owned when the poller made it |
| 12 | A comment on a pull request is a Signal while pull request events are skipped | low | BH | patch: documented; the Signal does not say which it is |
| 13 | Event ids may not become visible in order | maybe-false | BH, ECH; nothing in the repository settles it | defer: stated as an assumption in `docs/polling.md`, recorded in `deferred-work.md` |
| 14 | The gate is global and a limit shows to commands as "unreachable" | medium | BH | defer: the criteria treat the wait as unavailability; recorded in `deferred-work.md` |
| 15 | Signals have no retention or index | low | BH | defer: the loop's reads are not known yet; recorded |
| 16 | A removal during a cycle lets the cycle write cursors and Signals for the removed Project | low | ECH | rejected: registering again removes every cursor first, and a Signal for an event of a time it was registered is true |
| 17 | The comment entity tag is sent to a URL whose `since` has moved | low | BH | rejected: one full read per change, as the cursor moves; correct, and the fix adds state |
| 18 | The diff omitted the snapshot and Designer files | none | BH | rejected: excluded from the review file on purpose; the model-matches-migrations test covers them |
| 19 | `ReadAsync` does not check `limit`; the architecture rule is a regex over files | low | BH | rejected: no caller passes a bad limit; the rule is the form every other rule of the suite takes |
| 20 | The poll's `SignalsAppended` undercounted when comments failed after events appended | low | ECH | patch: counted where an exception cannot take it |
| 21 | A change is noticed later than one cycle when `X-Poll-Interval` is longer | none | ECH | rejected: AC and the documentation say a stated minimum interval is respected |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `python3 eng/check-licenses.py` -- expected: pass

- Results, 2026-11-27: `dotnet build` 0 warnings, 0 errors; `dotnet test` 1508 passed; line coverage 95.95% overall, and every new line of the poller, the gateway's gate, the inbox, the cursor store, and the host service covered but `Signal.cs` line 39 (an unreachable arm of a switch); `check-licenses.py` passes; the gates' own tests pass.
