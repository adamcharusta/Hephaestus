---
title: 'Story 1.8: Manage Trusted Users from the command line'
type: 'feature'
created: '2026-11-26'
status: 'done'
baseline_commit: '24f28a477be8bbddc986d74cadea17a5fd06cc33'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Hephaestus has no way to know whose labels, answers, and commands to act on. Trust must be held by GitHub's numeric user id, because a login can change hands (FR-3, AD-15).

**Approach:** `trusted-user add <login>` resolves the login through the gateway to GitHub's numeric user id and stores the id, with the login for display only. `list` shows the stored users, and `remove <login>` deletes one. The App's own bot account is refused. The list lives in SQLite, so it survives a restart.

## Boundaries & Constraints

**Always:**
- The id is the identity: the primary key, the only thing a later admission reads. The login is a display column, refreshed whenever `add` resolves the user again. A renamed user is still listed under the same id.
- Only `Hephaestus.GitHub` talks to GitHub: Core gains the `ICodeHost` read `FindUserAsync` and plain data types, and Core's `TrustedUserService` holds the rules (login shape, own-identity refusal, matching for removal). No Octokit or EF Core in Core.
- Adding needs a connected GitHub identity (Story 1.7). The own identity is the App's bot account, `<slug>[bot]`, found from the App as GitHub reports it. It is refused as the Owner's own identity, not as a bad login.
- `remove` works from what `list` shows: a stored login (ignoring case) matches first, with no call to GitHub, so trust can be revoked while GitHub is down. Only when no stored login matches, or several do, is the login resolved through GitHub and matched by id, which finds a user who changed their login.
- Every sentence for a person is a message id in `en` and `pl`. Command names, `--json`, and keys are not translated. A change writes through `IInstallationConfiguration` and reads go through `IReadStore`. The endpoints are mapped on the control socket.
- Line coverage stays at or above 80% overall and on new code; no new package.

**Never:**
- No admission function, no Signal, no use of trust by the loop (Epic 2). No Trusted User added from a login without GitHub's answer, and no trust attached to a login.
- No change to `_bmad/`, skills, `SPEC.md`, the architecture, `epics.md`, or an ADR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Add | a connected App; a login GitHub knows | id and canonical login stored; the command prints both; exit 0 |
| Add again | the id is already stored | nothing is duplicated, the login is refreshed, the command says it was already trusted; exit 0 |
| Renamed user | stored `old` with id N; GitHub now says N is `new` | `add new` keeps the one row, shows `new`; `list` shows the id N either way |
| Unknown login | GitHub answers 404, or the account is an organization | "user not found", nothing stored; exit 1 |
| Own identity | the login is `<slug>[bot]` | refused, nothing stored; exit 1 |
| Not connected | no App stored | the failure that says to run `github connect`; exit 1 |
| GitHub failing | unreachable, credential rejected | the failure of Story 1.7's wording, nothing stored; exit 1 |
| Bad login | not a GitHub login shape | refused before GitHub is asked; exit 1 |
| Remove | a stored login | the user is removed and the command says who; a later `list` omits them |
| Remove, renamed | a login that is stored under no row, GitHub resolves it to a stored id | that id is removed |
| Remove, unknown | matches nothing stored | "not a Trusted User"; exit 1 |
| List | none stored / some | `none` row text / a table of login, id, added (UTC); `--json` has `userId`, `login`, `addedAt` |
| Restart | users stored, the database reopened | the list is unchanged |

</frozen-after-approval>

## Code Map

- Spine AD-15, AD-16, AD-17; epics Story 1.8; EXPERIENCE "Command Line" (`trusted-user add|list|remove`).
- `src/Hephaestus.Core/Trust/` -- `TrustedUser`, `GitHubLogin` (shape), `TrustedUserService` and its result types; `Ports/ICodeHost.FindUserAsync`, `Ports/IReadStore.ListTrustedUsersAsync`, `Ports/IInstallationConfiguration.{Add,Remove}TrustedUserAsync`.
- `src/Hephaestus.GitHub/GitHubGateway.cs` -- `FindUserAsync`: reads the App (JWT) for its slug, an installation token when one exists, `GET /users/{login}`; reuse `IsOutage`, `CreateClient`.
- `src/Hephaestus.Persistence/` -- `TrustedUsers` table (user id key), migration, `SqliteInstallationConfiguration`, `SqliteReadStore`.
- `src/Hephaestus.Host/` -- `Api/TrustedUserApi.cs` on the control socket; `Cli/CommandTree.cs`, `CliApplication.cs`; messages in `Messages.{en,pl}.resx`, `MessageIds`.
- Reuse `ProblemResult`, `ApiJson`, the GitHub problem messages of Story 1.7, `FakeStores`, `FakeGitHub`, `FakeCodeHost`; do not change the secret or `github` commands.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/**` -- types, login shape, service, ports
- [x] `src/Hephaestus.GitHub/GitHubGateway.cs` -- `FindUserAsync`
- [x] `src/Hephaestus.Persistence/**` -- table, migration, write and read
- [x] `src/Hephaestus.Host/**` -- endpoints, commands, messages (`en`, `pl`)
- [x] `tests/**` -- login shape, service, gateway against the fake GitHub, persistence incl. reopening, API, CLI, architecture still green
- [x] `README.md` -- the commands

**Acceptance Criteria:**
- Given a connected identity, when the Owner adds a login, then the numeric id is stored and the login is display only.
- Given a stored user who changed their login, when the Owner lists, then the user is listed under the same id.
- Given a login GitHub does not know, when it is added, then the command fails and says the user was not found.
- Given the App's own identity, when it is added, then the command refuses.
- Given a stored user, when removed and listed, then they are gone.
- Given stored users, when the database is reopened, then the list is unchanged.

## Implementation Notes

- 2026-11-26. Built directly in one unattended session; no question was put to the Owner. The choices the story leaves open:
  - A Trusted User's key is GitHub's numeric id (table `TrustedUsers`, `UserId`), with the login and the time added; the spine says a user is identified by numeric id, so the id is not a UUID. The login is not unique: a stored login can be stale beside a current one.
  - `add` is `PUT /api/trusted-users/{login}` (201 when new, 200 when already stored, the same document), `remove` is `DELETE`, `list` is `GET`; the three are on the control socket. Adding the same user again refreshes only the login.
  - The lookup is made as the connected App, so adding needs Story 1.7's connection. It uses an installation token when the App has a live installation (the higher rate limit) and none when it has not. Reuse of Story 1.7's problems and wording: not connected, credential rejected, GitHub unreachable.
  - The App's own identity is its bot account, `<slug>[bot]`. GitHub's own `[bot]` suffix is reserved to Apps, so a login equal to it, as GitHub spells it, is the App. Other bot accounts may be trusted: AD-15 and the story refuse only Hephaestus's own identity.
  - An organization is not a user that can act, so adding one is "user not found".
  - `remove` matches a stored login first (ignoring case) and asks GitHub only when none matches or several do, so that revoking works while GitHub is down, what `list` shows is what is removed, and a renamed user can be removed by the new login. An empty list answers "not a Trusted User" without asking.
  - `list` reads only the database; it shows the login as of the last `add`, so a user who changed their login is listed under the same id with the earlier login until they are added again.
  - Whole-sentence outputs and column headers are message ids in `en` and `pl`; the table is aligned with two spaces, like `github check`.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-26. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | The Designer of the earlier migration `AddGitHubAppConnection` gained the `TrustedUserEntry` entity | medium | BH, ECH, VG; a script edit that included the old file | patch: reverted; only the new migration's Designer and the snapshot hold it |
| 2 | README says Hephaestus "acts only on" the named users while nothing reads the list | medium | BH; AGENTS.md: no security claim without a passing test | patch: reworded to say what the list is for and that nothing reads it yet |
| 3 | `GitHubLogin` accepts `a--b`; its comment said single hyphens | low | BH, ECH, VG; GitHub's rule bars it for new accounts but older ones exist | patch: the comment now says what the shape accepts; the shape stays lenient |
| 4 | `RegexOptions.IgnoreCase` lets U+212A and other lookalikes match `[A-Za-z]` | medium | BH, ECH; .NET case folding | patch: the flag is dropped and only the `[bot]` suffix is case-insensitive; tests |
| 5 | `AddTrustedUserAsync` is three statements outside a transaction; a removal between them makes `SingleAsync` throw, a 500 | medium | BH, ECH; the comment said atomic | patch: one transaction; a test of adds and removals at once |
| 6 | Own identity is judged by login, not id | low | BH; a bot's login ends in the reserved `[bot]` and is unique, so it cannot belong to anyone else | rejected: the extra id lookup adds a request and no safety |
| 7 | A rate limit on the lookup is reported as a rejected credential | false | BH; `IsOutage` catches `RateLimitExceededException` before `ApiException`, and 429/5xx are tested | rejected |
| 8 | A failing installation-token request is a rejected credential | low | BH, ECH; only when an installation vanishes between two calls | rejected: unlikely, and the fix adds a fallback branch |
| 9 | Each lookup costs three or four requests and a new installation token | low | BH | rejected: an Owner's occasional command |
| 10 | `remove` of an unlisted login while GitHub fails answers the failure, not "not trusted" | medium | BH, ECH; for an empty list it is wrong | patch: an empty list answers "not a Trusted User" at once; with other users stored, "GitHub unreachable" is the honest answer, because the login may be a renamed one; the test is renamed |
| 11 | No test that the dashboard listener does not map the changing endpoints | low | BH, VG; the dashboard listener is Story 1.14 | rejected: nothing exists to test yet |
| 12 | The CLI's answer-not-readable branches are untested | medium | BH, VG | patch: a `StubApi` test for `list`, `add`, and `remove` |
| 13 | Another App's bot can be a Trusted User | low | BH, ECH; AD-15 and the story refuse only Hephaestus's own identity | rejected: the Owner decides whom to trust; noted in Implementation Notes |
| 14 | The spec and `sprint-status.yaml` disagreed while in review; the logs were empty | low | BH | patch: both are `done` and the notes and results are filled in |
| 15 | HTTP statuses (404 for not connected and for an unknown user) are ambiguous | low | BH | rejected: Story 1.7's convention, and the problem `type` tells them apart |
| 16 | `FindUserAsync` does not validate its argument | low | ECH | rejected: its one caller validates, and Octokit escapes the segment (a test sends `[bot]`) |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `python3 eng/check-licenses.py` -- expected: pass

- Results, 2026-11-26: `dotnet build` 0 warnings, 0 errors; `dotnet test` 593 passed; line coverage 95.7% overall, the new Core, API, CLI, and Persistence code covered by its tests; `check-licenses.py` and the gates' own tests pass.
