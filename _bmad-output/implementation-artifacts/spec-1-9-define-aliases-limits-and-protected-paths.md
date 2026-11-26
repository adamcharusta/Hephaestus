---
title: 'Story 1.9: Define Aliases, limits, and protected paths'
type: 'feature'
created: '2026-11-26'
status: 'done'
baseline_commit: 'b6cb93784192b66bfa7aa99320b9bd015a670a65'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Owner cannot yet name the Agent and Model combinations and Effort levels a Task may choose, set the ceilings no repository can raise, say which paths an Agent's change may never touch, or set the installation's language (FR-7, FR-3, FR-69, AD-10, AD-16, AD-19).

**Approach:** Installation Configuration gains Aliases, limits (Task duration, processor, memory), protected paths, and settings (one so far: `language`). Each is written by its own API handler in its own transaction (AD-16) and read through `IReadStore`; the command line gets `alias`, `limit`, `protected-path`, `setting`, and `config show`. Core owns every rule: the closed Agent and Effort sets, the shapes of names and values, the defaults.

## Boundaries & Constraints

**Always:**
- An Alias names an Agent (`claude-code` or `codex-cli`) and a Model, or an Effort from Core's closed set (`low`, `medium`, `high`, `xhigh`, `max`). A Model is any text of a safe shape, so a new Model needs no change to Hephaestus. Any other Agent or Effort is refused.
- An Alias name is label-safe (lower case letters, digits, `.`, `_`, `-`; it starts with a letter or digit; at most 40 characters), because Role Labels carry it.
- A new installation's protected paths are `.github/**` and `hephaestus.yml`, put there by the migration; the Owner may add and remove entries.
- Limits are unset (`none`, JSON `null`) until the Owner sets them; `limit set` changes only the options given. Values have one canonical form: a duration as `1h30m`, processors as a decimal, memory as `512m` or `4g`.
- The language is a setting stored in the database, `en` or `pl`; any other value or name is refused. `HEPHAESTUS_LANGUAGE` is only the language of a command that cannot ask the host, and of an installation that has stored none. The command line asks the host for the stored language before it parses, and sends it as `Accept-Language`.
- `config show` has Trusted Users, Aliases, limits, protected paths, settings, and secret names; no secret value is read or shown.
- Every sentence for a person is a message id in `en` and `pl`; command names, `--json`, and keys are not translated. Endpoints that change anything are on the control socket. Line coverage stays at or above 80% overall and on new code; no new package.

**Never:**
- No check of a Project Configuration against the limits, no use of a protected path by the gateway, no Role Label parsing: those are Stories 1.11, 2.6, and Epic 4.
- No change to `_bmad/`, skills, `SPEC.md`, the architecture, `epics.md`, or an ADR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Add Agent Alias | `alias add opus --agent claude-code --model claude-opus-5-5` | stored, listed; exit 0 |
| Same again | identical definition | says it is already defined; exit 0 |
| Redefine | name stored with another definition | refused, says to remove it first; exit 1 |
| Unknown Agent | `--agent gemini` | refused; nothing stored; exit 1 |
| Effort Alias | `alias add deep --effort high` | stored; `--effort ultra` refused |
| Incomplete | `--agent` without `--model`, both forms together, neither | refused; exit 1 |
| Bad name or Model | uppercase, space, too long, leading hyphen | refused |
| Remove | stored / unknown name | removed and named / "not an Alias", exit 1 |
| Limits | `limit set --task-duration 2h --cpus 2 --memory 4g` | stored and shown; a later `limit set --memory 8g` keeps the other two; no option, or a bad value, is refused and changes nothing |
| New installation | `limit show`, `protected-path list`, `setting list` | `none` for each limit; the two default paths; `language` is `en` |
| Protected paths | add / duplicate / remove / unknown | stored / already listed / removed / "not a protected path"; a glob with `..`, a leading `/`, a backslash, or a control character is refused |
| Language | `setting set language pl`, then any command | stored; the next command speaks Polish; `tlh` is refused; an unknown setting name is refused |
| Config | `config show` | every section; secrets by name only |
| Restart | database reopened | everything is unchanged |

</frozen-after-approval>

## Code Map

- Spine AD-10, AD-16, AD-17, AD-19, AD-21; epics Story 1.9; EXPERIENCE "Command Line" (`alias`, `limit`, `protected-path`, `setting`, `config show`).
- `src/Hephaestus.Core/Aliases/`, `Limits/`, `Protection/`, `Settings/` -- types, shapes, services; `Ports/IInstallationConfiguration`, `Ports/IReadStore` gain the writes and reads; `Localization/LanguageResolver` gains the stored language.
- `src/Hephaestus.Persistence/` -- tables `Aliases`, `InstallationLimits`, `ProtectedPaths`, `Settings`, one migration that seeds the default paths; `SqliteInstallationConfiguration`, `SqliteReadStore`.
- `src/Hephaestus.Host/` -- `Api/{Alias,Limit,ProtectedPath,Setting,Configuration}Api.cs`, `ProblemResponses` (the stored language), `Cli/CommandTree.cs`, `CliApplication.cs`; messages in `Messages.{en,pl}.resx`, `MessageIds`.
- Reuse `ProblemResult`, `ApiJson`, `TrustedUserService`'s shape, `FakeStores`, `TempSocket`, `RunningHost`, `CliRun`; do not change the secret, `github`, or `trusted-user` commands beyond sharing the table writer.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/**` -- Agent, Effort, Alias, limits, protected paths, settings, services, ports
- [x] `src/Hephaestus.Persistence/**` -- tables, migration, writes, reads
- [x] `src/Hephaestus.Host/**` -- endpoints, commands, language resolution, messages (`en`, `pl`)
- [x] `tests/**` -- Core rules, persistence incl. reopening, API, CLI, architecture still green
- [x] `README.md`, `deploy/README.md`, `deploy/compose.yaml`, `.github/workflows/ci.yml` -- the commands and the language setting

**Acceptance Criteria:**
- Given a running installation, when the Owner adds an Alias for an Agent and a Model, then it is stored and listed, and an Agent other than Claude Code or Codex CLI is refused.
- Given an Effort Alias, when its value is outside Core's Effort set, then it is refused.
- Given limits set for duration, processor, and memory, then they are stored and shown.
- Given a new installation, then the protected paths are `.github/**` and `hephaestus.yml`, and the Owner can change the list.
- Given stored configuration, when `config show` runs, then Trusted Users, Aliases, limits, protected paths, and secret names are shown and no secret value is.
- Given any change, when the database is reopened, then it is unchanged.
- Given `setting set language pl`, then the next command speaks Polish; any other value is refused; a new installation is in English.

## Implementation Notes

- 2026-11-27. Built directly in one unattended session; no question was put to the Owner. The choices the story leaves open:
  - Core's closed Effort set is `low`, `medium`, `high`, `xhigh`, `max`: the values Claude Code's `--effort` takes, all of which Codex CLI's service also accepted in the injection spike. `none` and `minimal` are left out; an adapter may still declare any value unsupported (AD-10). The Agents are named `claude-code` and `codex-cli`.
  - An Alias is never silently redefined: the same definition again is "already defined" (exit 0); a different one is refused until the Owner removes the name. A name is label-safe, because a Role Label carries it (`primary:<alias>` must fit GitHub's 50 characters).
  - A Model is only checked for shape (it starts with a letter or digit, so a CLI never takes it for an option), so a new Model needs no change to Hephaestus.
  - Limits are unset until the Owner sets them (`none`, JSON `null`). The story names no defaults and inventing numbers would be a decision for the Owner; Story 1.11 decides what a missing ceiling means. Ranges: 1 minute to 7 days, 0.1 to 1024 processors, 64m to 4096g. A limit once set can be changed, not cleared; raising it to its maximum is the way back to "no ceiling".
  - The two default protected paths are seeded by the migration, so an installation upgraded from an earlier release gets them too. The Owner may remove both: the list is the Owner's.
  - `setting list` and `setting set` are the UX's generic pair; `language` is the only setting so far, and the table is where later ones go.
  - Language: the stored setting wins when the host can be asked. `HEPHAESTUS_LANGUAGE` stays as the language of an installation that has stored none and of a command that cannot reach the host, which keeps an installation that already sets it, and the CI smoke test, working. The command line asks the host for it before it parses (2 s at most), and sends it as `Accept-Language`; the host's own problems use the stored language when the request names none.
  - A protected path travels in a query value and an Alias in a JSON body, because a glob holds slashes and asterisks and an address would mangle them.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-27. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | `RemoveAliasAsync` reads and then writes in a deferred transaction; in WAL mode a writer that committed in between makes the upgrade fail at once | medium | BH, ECH; Story 1.8's add writes first for this reason | patch: the transaction writes first (an update that takes the lock); tests of concurrent removals and of removals racing additions |
| 2 | `..` as a name is normalised out of the address by the HTTP client | low | BH; the answer is still a failure with a problem, exit 1 | rejected: the fix is a second way of sending a name |
| 3 | A limit cannot be cleared | low | BH, ECH; no acceptance criterion asks for it and the Owner can raise a limit to its maximum | rejected: new syntax and a failure case beyond the story |
| 4 | Memory parsing multiplies before the range check and is right only because the product wraps negative | low | BH, VG; `9999999999g` was never tested | patch: the count is compared with the ceiling before multiplying; tests |
| 5 | A glob may start with `!` or `#`, or have unbalanced brackets | low | BH; what a glob means is decided by the matcher of the story that enforces the list | rejected |
| 6 | A glob with a zero-width or direction character looks like another glob | low | ECH | patch: Unicode format characters are refused; tests |
| 7 | Protected paths are case-sensitive, so `Hephaestus.yml` is not `hephaestus.yml` | medium | BH; real on a case-insensitive checkout, and the enforcing story decides the matcher | defer: `deferred-work.md` |
| 8 | Every command makes a language lookup first; a host that accepts and never answers costs the client's 30 s | medium | BH, ECH, VG | patch: the lookup stops after 2 s; a test with a host that never answers |
| 9 | `IReadStore` and `IInstallationConfiguration` grow wide | low | BH; the spine says each story adds its method to them | rejected |
| 10 | `HostSettings.LanguageFromEnvironment` defaulted to true | false | BH; the flag is gone with finding 12 | rejected |
| 11 | Taking `HEPHAESTUS_LANGUAGE` out of compose breaks an installation that sets it | medium | BH, ECH | patch: with finding 12 the compose entry stays as the language of an installation that set none |
| 12 | The environment's language overrode the stored one although the spec says it only serves an installation that stored none | high | ECH (claim); the code asked the host only when the variable was unset | patch: the host is always asked, the setting reports whether it was set (`isSet`), the stored language wins; tests |
| 13 | `ProblemResponses` falls back to the environment's language when the database cannot be read, and nothing tests it | medium | VG | patch: a test that removes the database under a running host |
| 14 | Only `version` and the new commands are tested in the stored language | medium | VG | patch: tests for `secret list`, `trusted-user list` and `remove`, `github check`, and a client-side failure |
| 15 | A corrupt stored Alias makes `alias list` and `config show` fail | low | BH, ECH; Core writes the tags and a test pins the failure | rejected |
| 16 | `config show` reads its sections one after another, not in one snapshot | low | BH, ECH; a read, and a mixed view across a concurrent change is harmless | rejected |
| 17 | The migration repeats the two default globs instead of using `ProtectedPath.Defaults` | low | ECH; a persistence test compares the seeded list with `Defaults` | rejected |
| 18 | The spec artifact had unchecked tasks and empty logs | low | BH | patch: filled in |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `python3 eng/check-licenses.py` -- expected: pass

- Results, 2026-11-27: `dotnet build` 0 warnings, 0 errors; `dotnet test` 889 passed; line coverage 96.9% overall, 98.6% over the new Core, API, CLI, and Persistence code; `check-licenses.py` and the gates' own tests pass.
