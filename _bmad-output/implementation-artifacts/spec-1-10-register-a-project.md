---
title: 'Story 1.10: Register a Project'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '9565a83af638fb4d150845252901f67ba411b8d8'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Owner cannot yet say which GitHub repository Hephaestus works on, so no later story has a Project to act on (FR-1, AD-19).

**Approach:** `project add <owner>/<repo> --base-branch <branch>`, `project list`, and `project remove <owner>/<repo>`. The gateway finds the repository among those the GitHub App is installed on and reads whether the Base Branch exists and is protected; Core owns the rules; a Project is stored with GitHub's numeric repository id as its identity, the name for display, and the Base Branch. Removing a Project marks it removed and keeps its record.

## Boundaries & Constraints

**Always:**
- Identity is GitHub's repository id; the `<owner>/<repo>` name is display only and is refreshed when the Owner registers the repository again under its new name.
- `add` fails and says which is wrong: the App is not installed on the repository (or the repository does not exist, which GitHub does not tell apart), or the Base Branch does not exist. Nothing is stored on a failure.
- `add` reports whether the Base Branch is protected and recommends protection when it is not. The App has no administration permission, so "protected" is what GitHub reports for the branch; the text claims nothing about what the protection holds.
- Registering a registered Project again is not an error: the same Base Branch says "already registered" (exit 0); another Base Branch changes it; a removed Project is registered again from its retained record.
- `remove` marks the Project removed (time kept) and deletes nothing. `list` shows Projects not removed; `--all` also shows removed ones. A Project is found by its stored name, else by asking GitHub which repository the name is now, so a renamed repository can be removed under its new name.
- Every sentence for a person is a message id in `en` and `pl`; command names, `--json`, and keys are not translated. Endpoints that change anything are on the control socket; reads go through `IReadStore`; writes through `IInstallationConfiguration` in their own transaction (AD-16). Line coverage stays at or above 80% overall and on new code; no new package.

**Never:**
- No read of `hephaestus.yml`, no check against limits, no polling or Task: Story 1.11 and Epic 2. No change to `_bmad/`, skills, `SPEC.md`, the architecture, `epics.md`, or an ADR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Register | `project add acme/shop --base-branch main`, App installed, branch exists | stored; says protected or not, recommends protection when not; exit 0 |
| Not installed | repository unknown to the App | fails: the App is not installed on it; nothing stored; exit 1 |
| No such branch | `--base-branch nope` | fails: the Base Branch does not exist; nothing stored; exit 1 |
| Same again | identical name and branch | "already registered"; exit 0 |
| New Base Branch | registered Project, other branch | Base Branch changed, said so |
| Bad name or branch | `shop`, `a/b/c`, branch with `..` or a space | refused, GitHub not asked; exit 1 |
| Not connected / unreachable | no App, key unreadable, GitHub down | the same problems as `trusted-user add` |
| Renamed | stored `acme/shop`, GitHub now `acme/store` | `list` still lists it, same repository id; `add acme/store` refreshes the name |
| Remove | registered / unknown / already removed | removed and named / "not a registered Project" / same |
| Retained | after remove | record, id, Base Branch, `removedAt` still stored; not in `list`; `list --all` shows it |
| Restart | database reopened | list unchanged |

</frozen-after-approval>

## Code Map

- Spine AD-4, AD-16, AD-17, AD-19, AD-21; epics Story 1.10; EXPERIENCE "Command tree" (`project add|list|remove`).
- `src/Hephaestus.Core/Projects/` -- `Project`, `ProjectName`, `BaseBranchName`, `ProjectService`; `Ports/ICodeHost` gains `FindRepositoryAsync`; `Ports/IReadStore` and `IInstallationConfiguration` gain the Project reads and writes.
- `src/Hephaestus.GitHub/GitHubGateway.cs` -- `FindRepositoryAsync`: installations of the owner, the installation token, the repository, the branch and its `Protected` flag.
- `src/Hephaestus.Persistence/` -- table `Projects`, one migration, `SqliteInstallationConfiguration`, `SqliteReadStore`.
- `src/Hephaestus.Host/` -- `Api/ProjectApi.cs`, `Documents`, `ApiJson`, `ControlHost` (service), `Cli/CommandTree.cs`, `CliApplication.Projects.cs`; messages in `Messages.{en,pl}.resx`, `MessageIds`.
- Reuse `TrustedUserService`'s shape and its problem mapping, `ProblemResult`, `ShowAsync`, `TableWriter`, `FakeCodeHost`, `FakeGitHub`, `TempSocket`, `RunningHost`, `CliRun`.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/**` -- Project, names, service, ports, messages ids
- [x] `src/Hephaestus.GitHub/**` -- find the repository and its Base Branch
- [x] `src/Hephaestus.Persistence/**` -- table, migration, writes, reads
- [x] `src/Hephaestus.Host/**` -- endpoints, commands, messages (`en`, `pl`)
- [x] `tests/**` -- Core rules, gateway, persistence incl. reopening and retention, API, CLI, architecture still green
- [x] `README.md` -- the commands

**Acceptance Criteria:**
- Given a repository on which the App is installed, when the Owner registers it with its Base Branch, then it is stored by repository id with its name and Base Branch, and the command reports whether the branch is protected and recommends protection when it is not.
- Given a repository without the App, or a missing Base Branch, then the command fails and says which.
- Given a renamed repository, then the Project is still listed with the same repository id.
- Given a removed Project, then it is not listed among the Projects Hephaestus works on and its record is retained.
- Given registered Projects and a restart, then the list is unchanged.

## Implementation Notes

- 2026-11-27. Built directly in one unattended session; no question was put to the Owner. The choices the story leaves open:
  - The CLI is `project add <owner>/<repo> --base-branch <branch>`, `project list [--all]`, and `project remove <owner>/<repo>`, as the UX command tree has them; `--all` is the only addition, so that a removed Project can still be seen.
  - "Protected against direct pushes" is read from the `protected` flag GitHub gives the branch. The App has no administration permission (AD-19), so what a protection requires is not readable; the text says "GitHub reports ... as protected" and claims nothing about it. A ruleset-only protection may not set the flag; that is unverified against live GitHub and is in `deferred-work.md`.
  - The gateway finds the repository among those of the installation(s) on the owner's account (a suspended installation reaches nothing), so "the App is not installed" and "the repository does not exist" are one answer, as GitHub gives one to an App.
  - Registering again is an upsert by repository id: the same name and Base Branch is "already registered" (exit 0), another Base Branch (or a new name after a rename) says "updated", and a removed Project is registered again from its record, keeping its first registration time.
  - `project remove` follows `trusted-user remove`: it works from the stored names without asking GitHub, and asks GitHub which repository a name is now only when no stored name matches (or several do), so a renamed repository is removed under its new name. Removing a Project that was already removed says it is not a registered Project.
  - A removal is a time in `RemovedAt` on the same row; nothing is deleted. `IReadStore.ListProjectsAsync(includeRemoved)` serves both the CLI and the later stories that need the Projects Hephaestus works on.
  - The name travels in the `name` query value, not the path: a path would let the client resolve `..` into another endpoint (`project remove ../secrets/x` reached `DELETE /api/secrets/x`), and a name that is not `owner/repo` would be an unknown address instead of a refusal. The Base Branch travels in a JSON body.
  - An owner may contain `_`: an Enterprise Managed User's login has one.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-27. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | The CLI built the address from the typed name, so `project remove ../secrets/x` was resolved by the client to `DELETE /api/secrets/x` | high | BH, ECH; `Uri` collapses dot segments before sending | patch: the name travels in the `name` query value; tests for `.`, `..`, `../secrets/token`, `acme/..` that also check the secret survives |
| 2 | Removal trusts a unique stored-name match that may be stale (the name now belongs to another repository) | low | BH, ECH; the list shows what is removed, as `trusted-user remove` does and its tests accept | rejected: documented design, a lookup would make revoking depend on GitHub |
| 3 | The removal path with a stale name beside the current one was untested | medium | VG, demonstrated by changing `Count == 1` to `FirstOrDefault()` with every test still green | patch: a service test with two stored Projects of one name |
| 4 | `#`, `%`, `+` in a Base Branch might be mangled in the branch address | maybe-false | BH, ECH, VG; VG's probe showed Octokit escapes `#` and `+` correctly | patch: gateway tests for `a#b` and `a+b` |
| 5 | The `protected` flag may not cover rulesets; "protected" may read as an assurance | low | BH; the text names the flag, claims nothing about what it covers, and the App cannot read more | defer: `deferred-work.md`; README names the flag |
| 6 | README: the `--all` comment sat on the `remove` line | low | BH | patch |
| 7 | A raw zero-width character in a test's `InlineData` | low | BH; found in `ProjectNameTests` | patch: written as `\u200B`, plus a direction mark |
| 8 | Owner shape refuses `_`, which an Enterprise Managed User's login has | low | BH, ECH | patch: underscore allowed inside an owner; tests |
| 9 | The lookup pages through every repository of the installation | low | BH; one call per page, a spurious `Unreachable` only past the 25 s deadline on thousands of repositories | rejected: a direct `GET /repos` would find a public repository the App is not installed on |
| 10 | An archived or disabled repository is accepted | low | BH; no acceptance criterion asks for it | defer: `deferred-work.md` |
| 11 | A gateway answer of `None` with no branch maps to `Unreachable`; an unknown `change` prints "Nothing was changed" | low | BH; unreachable by the gateway and the API of this change | rejected |
| 12 | A 404 while the installation is read mid-lookup is "rejected", not "not installed" | low | ECH; needs an installation deleted during one call | rejected |
| 13 | `project list --all` shows the Removed column only when a removed Project exists | low | ECH; the table shape followed the data | patch: the flag decides; a test |
| 14 | A malformed name with a bad body answers "request not understood" first | low | ECH | rejected: both are refusals and nothing is stored |
| 15 | Missing tests: an empty or missing name, an invalid `all` | low | BH | patch: the missing-name test; `all=maybe` is the framework's binding |
| 16 | Fakes of the gateway are repeated in three test projects | low | BH | rejected: each project owns its own fake by the architecture rules |
| 17 | Double blank line in the migration, a missing blank line before a test member, a `param` tag without `Failure` | low | BH; the double blank line is the style of every generated migration | rejected |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `python3 eng/check-licenses.py` -- expected: pass

- Results, 2026-11-27: `dotnet build` 0 warnings, 0 errors; `dotnet test` 1079 passed; line coverage 97.1% overall, every new Core, GitHub, API, CLI, and Persistence line but five covered; `check-licenses.py` passes.
