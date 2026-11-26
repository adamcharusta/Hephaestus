---
title: 'Story 1.7: Connect Hephaestus''s own GitHub identity'
type: 'feature'
created: '2026-11-26'
status: 'done'
baseline_commit: 'b1c23f7731442a333f12fcaf22b8755e6dbe6d47'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Hephaestus has no GitHub identity yet. Without its own GitHub App, it cannot open a pull request the Owner may approve, and nothing shows that the App has the fewest permissions it needs (FR-4, AD-4, AD-19, NFR-6).

**Approach:** `github connect --app-id <id>` reads the App's private key from standard input, authenticates as the App through `Hephaestus.GitHub`, and stores the key as a secret and the App id as Installation Configuration only after GitHub accepted both. `github check` repeats the check with the stored credential. Both report the App, the repositories it is installed on, and each required permission as present or missing, and warn when `workflows` or administration is granted.

## Boundaries & Constraints

**Always:**
- `Hephaestus.GitHub` is the only project that references Octokit and the only one that reads the private key (AD-4); an architecture test pins both. Core gains the port `ICodeHost` and plain data types, no Octokit.
- The private key is a secret under Story 1.5 (`github-app-private-key`), never an argument, never in the database, never in a document the API answers. The masker covers each line of a multi-line value (deferred from Story 1.6).
- Required: contents, issues, pull requests at `write`; metadata, checks, statuses at `read`. A higher level than needed counts as present. `workflows` and administration (repository or organization) at any level are warned about, never required.
- A failure is one of two reported causes, GitHub unreachable or credential rejected (an unreadable key is a rejected credential); it stores nothing, and a failed write restores what was there before.
- Every sentence for a person is a message id in `en` and `pl`; command names, JSON, permission names, and levels are not translated. Installation documentation (`docs/github-app.md`, linked from the README) leads through creating the App and says why the identity is separate (AR-57).
- Line coverage stays at or above 80% overall and on new code; no new package (Octokit 14.0.0 is pinned in the Stack).

**Never:**
- No credential, installation token, or JWT is logged, printed, or placed in a Workspace. No GitHub write; this story reads only.
- No Trusted User, no Project registration, no polling (Stories 1.8, 1.10, Epic 2).
- No change to `_bmad/`, skills, `SPEC.md`, the architecture, `epics.md`, or an ADR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Connect | App id, a valid key on standard input, GitHub accepts | identity, installations with repositories, permission table; key and App id stored |
| Check | a stored credential | the same report, read live |
| Not connected | `github check` with nothing stored | failure that says to run `github connect`; exit 1 |
| Rejected | GitHub answers 401 | "credential rejected"; nothing stored, an earlier credential is kept |
| Unreachable | no answer, a timeout, a 5xx | "GitHub unreachable"; nothing stored |
| Unreadable key | input is not a PEM RSA private key | "credential rejected" without contacting GitHub; nothing stored |
| Missing permission | the App lacks one required permission | that row says missing; the report still prints |
| Forbidden permission | `workflows` or administration is granted | a warning naming it and its level |
| Key as argument | any extra token or unknown option on `github connect` | refused without echoing a token; exit 2 |
| Partial write | the App id cannot be saved after the key was | the earlier key is restored, the failure reported |
| Multi-line value | a stored PEM logged one line at a time | each line is masked |
| Architecture | the code base | only `Hephaestus.GitHub` references Octokit or names the credential |

</frozen-after-approval>

## Code Map

- Spine AD-4, AD-16, AD-17, AD-19; epics Story 1.7; EXPERIENCE "Command Line" (`github connect`, `github check`); threat model T23 (never `workflows`).
- `src/Hephaestus.Core/GitHub/` -- identity data types, the permission policy (`GitHubPermissionPolicy`), the credential's secret name; `Ports/ICodeHost`, `Ports/IInstallationConfiguration`, and `IReadStore.GetGitHubAppIdAsync`.
- `src/Hephaestus.Core/Secrets/SecretMatcher.cs` -- masks each line of a multi-line value.
- `src/Hephaestus.GitHub/` -- `GitHubGateway` (JWT, Octokit calls, mapping, rollback), `GitHubAppJwt`.
- `src/Hephaestus.Persistence/` -- one table for the App id, `IInstallationConfiguration`, read through `SqliteReadStore`; migration.
- `src/Hephaestus.Host/` -- `/api/github/...` on the control socket, `github` commands, report rendering.
- Reuse `SecretPolicy`, `ProblemResult`, `ApiJson`, `CliApplication.ReadValueAsync`; do not change the secret commands.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/**` -- types, policy, ports, masker lines
- [x] `src/Hephaestus.GitHub/**` -- gateway on Octokit; registration
- [x] `src/Hephaestus.Persistence/**` -- App id table, migration, port, read
- [x] `src/Hephaestus.Host/**` -- endpoints, commands, messages (`en`, `pl`)
- [x] `tests/**` -- policy, JWT, gateway against a fake GitHub, persistence, API, CLI, architecture, masker lines
- [x] `docs/github-app.md`, `README.md` -- App setup and the commands

**Acceptance Criteria:**
- Given an App id and its key on standard input, when `github connect` runs, then Hephaestus authenticates as the App, reports its identity and repositories, and holds the key as a secret.
- Given the connected App, when its permissions are read, then each required permission is reported present or missing and `workflows` or administration is warned about.
- Given GitHub is unreachable or the credential is rejected, then the command says which and stores nothing partial.
- Given the code base, then an architecture test shows `Hephaestus.GitHub` alone references Octokit or reads the credential.
- Given the installation documentation, then it leads through creating the App with exactly the required permissions and explains the separate identity.

## Implementation Notes

- 2026-11-26. Built directly in one unattended session; no question was put to the Owner. The choices the story leaves open:
  - The key is the secret `github-app-private-key`; the App id is one row of a new table, `GitHubAppConnections`, written through a new Core port `IInstallationConfiguration` and read through `IReadStore.GetGitHubAppIdAsync`. The spine says handlers write Installation Configuration directly and names no port for it, so the port is the smallest way to keep EF Core out of the gateway.
  - `ICodeHost` (a port the spine already names) gets the two reads of the identity. The gateway, not the host, stores the credential, so the host never reads the key back and a failed second write can restore the first (AD-4).
  - Permissions are judged on the App (`GET /app`), not on each installation: that is what AD-19 says is granted. A change the installation owner has not yet accepted is not seen; the documentation says to accept it and run `github check`.
  - `connect` and `check` exit 0 when GitHub answered, even with a permission missing: the report says it, and `--json` carries `ready`. Exit 1 is a failure the API reported (EXPERIENCE "Output").
  - Problem statuses: 404 not connected, 400 unreadable key (reported as a rejected credential), 422 rejected, 502 unreachable. A server error or a rate limit is "unreachable"; any other refusal is "rejected".
  - The permission names in `--json` are kebab-case (`pull-requests`) and levels are `none`, `read`, `write`, `admin`.
  - A permission counts as granted at the lowest level that the App and each of its installations hold, and a forbidden one is warned about if any holds it (review finding 1).
  - A server error, a 408, a 429, a primary or secondary rate limit, or a read that outlasts 25 seconds (the command line gives up at 30) is "unreachable"; every other refusal is "rejected".
  - The masker now also looks for each line of a multi-line value, not counting the PEM frame lines (finding 13 of Story 1.6, deferred to this story).
  - `Hephaestus.GitHub` signs its own token with the framework's RSA (RS256, nine minutes), so no package is added beyond the pinned Octokit 14.0.0.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-26. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | Permissions read from the App only; an installation that has not accepted a change, or still holds `workflows`, is not seen | medium | BH, VG; `GET /app` carries the App's, `GET /app/installations` each installation's | patch: the lowest level across the App and installations, the highest for forbidden; tests |
| 2 | A 403 rate limit (primary or secondary) and a 408 were reported as a rejected credential | medium | BH, ECH, VG; Octokit raises `RateLimitExceededException`, `SecondaryRateLimitExceededException`, `AbuseException` as 403 | patch: classed as unreachable; tests |
| 3 | A read that outlasts the command line's 30 seconds is reported failed while the host goes on to store | medium | ECH; 2 + 2N requests of up to 20 seconds | patch: a 25-second deadline on one read; test |
| 4 | Two connects at once, or a check between the two writes, see a key with another's id; the upsert could insert twice | medium | BH, ECH | patch: one gate in the gateway, one upsert statement; tests |
| 5 | A transport per client, never disposed | low | BH, ECH; one adapter per read now, disposed | patch |
| 6 | The architecture rule missed `_secrets.ReadAsync(` and `ReadAllAsync(` | medium | VG, ECH, BH; `\b` does not fall after an underscore | patch: any member; a test of the rule on samples |
| 7 | `github connect --help <key>` echoed the key | medium | ECH; `NotUnderstoodError` names the first unmatched token | patch: refused without a token beside `--help` too; test |
| 8 | A malformed 200 answer to `github check` was untested | low | VG; `StubApi` tests of the sibling commands | patch: tests |
| 9 | The assertions that the key is not in the output could not fail; the real stores were not exercised | low | VG; `FakeCodeHost` never holds the key | patch: an end-to-end test over the real stores and the real gateway |
| 10 | A null owner, permissions, or account in GitHub's answer would crash | low | ECH | patch: null-safe |
| 11 | The clock, rotating the key, `docker exec` without `-i`, and the exit code were undocumented | low | BH | patch: `docs/github-app.md`, README, the rejected detail names the clock |
| 12 | A failure of one installation fails the whole report as rejected | low | BH, ECH | rejected: unlikely, and the fix adds a partial-report state the story does not ask for |
| 13 | The key's title is "credential rejected" while its detail says GitHub was not asked; a stored unreadable key on `check` answers 400 | low | BH, ECH | rejected: the story counts an unreadable key as a rejected credential; the detail says what happened |
| 14 | The secret policy's wording ("secret value") for a key | low | BH | rejected: the key is a secret under Story 1.5 |
| 15 | The undo's own failure is swallowed | low | BH, ECH | rejected: the first failure is the one to report, a repeat settles the rest, and the gateway has no logger |
| 16 | An unknown level for a forbidden permission is not warned about | low | BH, ECH | rejected: GitHub names three levels |
| 17 | Exit code 0 with a missing permission | low | BH | rejected: EXPERIENCE gives 1 to a failure the API reported; `ready` and the documentation say the rest |
| 18 | `GET /api/github/identity` does not read through `IReadStore` | low | BH | rejected: it reads GitHub, not stored data |
| 19 | The generic `secret set` and `secret remove` can change `github-app-private-key` | low | ECH | rejected: the Owner's own act on a named secret; `check` then says what is wrong |
| 20 | Multi-line masking too broad, or missing a re-wrapped or joined key | low | BH, ECH | rejected: T16 names exact, base64, and URL-encoded forms; a line below 8 characters is never masked |
| 21 | The installation token carries every permission of the installation | low | BH | rejected: Octokit 14 has no overload that narrows it |
| 22 | Pagination, a non-`ApiException` from Octokit, and the cancellation of `check` are untested | low | BH | rejected: Octokit pages, and an unexpected exception is the generic 500 |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `python3 eng/check-licenses.py` -- expected: pass
