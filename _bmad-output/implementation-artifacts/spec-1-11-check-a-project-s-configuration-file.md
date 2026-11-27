---
title: 'Story 1.11: Check a Project''s configuration file'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: 'b33f2c234030bafee1e78395862d4006e2a3de2f'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A Project's rules live in `hephaestus.yml` on its Base Branch, but nothing reads or validates it, so the Owner cannot know that a Task will not run under rules nobody merged (FR-2, FR-3, FR-5, FR-7, FR-31, FR-61, FR-69, AD-16).

**Approach:** `project check <owner>/<repo>`. The gateway returns the raw bytes of `hephaestus.yml` at the tip of the Base Branch and that commit's id; Core parses and validates them against the stored Aliases, limits, and available Methodologies and returns every problem at once, each named by its setting. A reference page documents every key with two stock-image examples.

## Boundaries & Constraints

**Always:**
- Only the Base Branch is read: the gateway reads the branch tip, then the file at that commit id, so bytes and commit id belong together. It parses nothing. Core parses, validates, and owns every rule (AD-16).
- Keys are camelCase and the only ones allowed: `primaryAlias`, `fallbackAlias`, `effort`, `methodology`, `validation`, `correctionRounds`, `image`, `limits` (`taskDuration`, `cpus`, `memory`), `allowedEgressHosts`, `language`. Any other key, at any level, is an error that names it.
- `primaryAlias` and `fallbackAlias` name Aliases that set an Agent and a Model, `effort` names an Alias that sets an Effort; an unknown name is an error, and so is the wrong kind. Primary and Fallback resolving to the same Agent is an error. `methodology` must be one the image ships (a directory of `methodologies/` with a `manifest.yml`), `default` when absent. A limit above the installation's is an error; one the installation has not set has no ceiling. `language` is `en` or `pl`, else the installation's.
- `validation` must name at least one command; none is an error saying why. `image` is required: there is no Hephaestus base image to fall back on. `correctionRounds` is a whole number from 0 to 10, 2 when absent.
- Every problem is reported, not the first: each with its setting, the line in the file, and a message id with parameters. The `--json` document carries ids and parameters, never prose, so it is the same in every language. Text taken from the file is shortened and stripped of control characters before it is echoed.
- A check changes nothing: it is a read on the control API; nothing is stored. Exit code 0 only for a valid file. The check does not depend on the Project's stored name being current: it finds the Project as `project remove` does. No new package other than the pinned YamlDotNet in Core; line coverage stays at or above 80% overall and on new code.

**Never:**
- No snapshot onto a Task, no Task, no polling, no Dashboard view (Epics 2 and 3, Story 1.15). No change to `_bmad/`, skills, `SPEC.md`, the architecture, `epics.md`, or an ADR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Valid | registered Project, valid file on the Base Branch | "valid", names the commit; exit 0 |
| Missing | no `hephaestus.yml` on the Base Branch | "missing", names the commit; exit 1 |
| Several faults | many invalid settings | every one named with its reason; exit 1 |
| Other branch | file differs on another branch | only the Base Branch's bytes are read; the commit is the Base Branch's tip |
| Unknown key | `foo: 1`, `limits: {x: 1}` | each is an error naming the key |
| Aliases | unknown, wrong kind, same Agent for both | each an error |
| Methodology | `bmad` (no manifest), `nope` | not available; the available ones are named |
| Limits | memory above the installation's, malformed value | error each |
| Validation | absent, `[]`, blank item | error: Validation with no command passes without checking |
| Language | `fr` | error; `pl` accepted |
| Not YAML, not UTF-8, several documents, not a mapping, too large | | one file-level error; line and column for a syntax fault |
| Project not registered / removed | | "not a registered Project"; exit 1 |
| Base Branch deleted, App removed, GitHub down, not connected | | the same problems as `project add` |
| Renamed repository | stored name stale | the check still finds the Project |

</frozen-after-approval>

## Code Map

- Spine AD-16, AD-14, AD-21; epics Story 1.11; EXPERIENCE "Command tree" (`project check`).
- `src/Hephaestus.Core/Projects/` -- add `ProjectConfiguration` (the parsed settings), `ProjectConfigurationParser` (YamlDotNet node model; pure function of bytes plus a `ConfigurationContext` of Aliases, limits, Methodologies), `ConfigurationIssue`, `ProjectConfigurationService.CheckAsync`; `ProjectService` gains a public `FindAsync` that `RemoveAsync` also uses. `ProjectFile`/`GitHubFileResult` types beside `GitHubRepository`.
- `src/Hephaestus.Core/Ports/` -- `ICodeHost.ReadFileAsync(repositoryId, branch, path)`; new `IMethodologyCatalog`.
- `src/Hephaestus.GitHub/GitHubGateway.Repositories.cs` -- the file read: installation holding the repository id, branch tip, contents at that commit id, size cap before decoding.
- `src/Hephaestus.Host/` -- `Api/ProjectApi.cs` (`GET /api/projects/check?name=`), `ApiJson`, `Documents`, `ControlHost` (service, catalog), `Hosting/DirectoryMethodologyCatalog.cs`, `Cli/CommandTree.cs`, `Cli/CliApplication.Projects.cs`; `Hephaestus.Host.csproj` copies `methodologies/` to the output; `deploy/Dockerfile` copies it into the build; messages in `Messages.{en,pl}.resx` and `MessageIds`.
- Reuse `ProjectService`'s problem mapping, `ShowAsync`, `FakeCodeHost`, `FakeGitHub`, `RunningHost`, `CliRun`, `TempSocket`.
- `docs/project-configuration.md` (new), linked from `README.md`.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/**` -- configuration model, parser and rules, service, ports, message ids
- [x] `src/Hephaestus.GitHub/**` -- read the file at the Base Branch tip
- [x] `src/Hephaestus.Host/**`, `deploy/Dockerfile` -- endpoint, command, catalog, messages (`en`, `pl`)
- [x] `docs/project-configuration.md`, `README.md` -- the reference, two examples, the command
- [x] `tests/**` -- parser rules per key, service, gateway, API, CLI, catalog, the documented examples validated by the parser

**Acceptance Criteria:**
- Given a registered Project with a valid file on its Base Branch, when the Owner checks it, then it is valid and names the commit it was read from.
- Given no file, then it is reported missing; given several invalid settings, then every one is named with its reason.
- Given a file that differs on another branch, then only the Base Branch's bytes are read, returned by the gateway with their commit id and parsed in Core.
- Given a file with a key outside the list, an unknown Alias, equal Agents, an unavailable Methodology, a limit above the installation's, no Validation command, or a language other than `en` or `pl`, then each is an error.
- Given the documentation, then every key is described and two complete examples with stock images validate.

## Implementation Notes

- 2026-11-27. Built directly in one unattended session (the implementation was not handed to a subagent: the Code Map above was drawn from this session's own investigation); no question was put to the Owner. The choices the story leaves open:
  - **Keys.** `primaryAlias`, `fallbackAlias`, `effort`, `methodology`, `validation`, `correctionRounds`, `image`, `limits` (`taskDuration`, `cpus`, `memory`), `allowedEgressHosts`, `language`. The planning documents name the settings but not the keys; these are camelCase, as the story asks. Stacking mode is in the Glossary's description of Project Configuration but not in this story's list, so it is an unknown key until a story adds it.
  - **Aliases for the Effort.** `effort` names an Alias that sets an Effort, as `effort:medium` does on a Task, so an Effort has to be defined as an Alias before a file can name it. This is the reading of "a file that names an unknown Alias" that keeps one meaning for a name.
  - **`image` is required**, because there is no Hephaestus base image and so no default for a Task to fall back on. The story requires it only by this reasoning; an Owner who wants it optional changes it with the architecture.
  - **An installation limit nobody set is no ceiling**, so a file may then name any limit that is well formed.
  - **The gateway** reads the repository by its id from the installation that holds it (a rename or a transfer changes the name, not the id), reads the branch tip, then asks for the file at that commit's id, so bytes and commit id are one thing and no other branch is ever named. A directory, a link, or a submodule at the path is "missing"; a file over 64 KiB is not decoded, and Core reports it as one file-level problem.
  - **Parsing** is two passes of YamlDotNet, so that nothing recursive runs on a hostile file: first the event stream (syntax, one document, depth at most 8, no alias, no key written twice in a mapping), then the node model. A YAML alias is an error, because an alias can make a key that contains itself, which the node model cannot hash; an anchor nothing refers to is harmless. The library's own messages are never shown: a syntax fault is "line, column".
  - **Everything echoed from the file** passes `ConfigurationText.Safe` (control, format, and separator characters become `?`, long values are cut), and the command line prints values of a valid file the same way.
  - **The answer** is one document for valid, invalid, and missing, with ids and parameters for the problems, so `--json` is the same in every language. The command exits 0 only for a valid file; a missing or invalid file is the answer to the question and goes to standard output.
  - **The check is a `GET`** on the control socket: it changes nothing. There is no dashboard listener yet (Story 1.14); when there is, this read can move to it.
  - **Methodologies** are a port (`IMethodologyCatalog`) implemented from the `methodologies/` directory the image ships beside the executable; a directory without `manifest.yml` (today `bmad`) is not available. The Host project copies the directory to its output, `deploy/Dockerfile` copies it into the build, and `.dockerignore` lets it into the build context. The image build itself was not run here (no Docker daemon); the publish output was checked to hold `methodologies/`.
  - **Not done, by design:** nothing is stored, no snapshot is made onto a Task (no Task exists), `language` is parsed and returned and its snapshot is for the story that starts a Task.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-27. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | `\d` matches digits of other scripts; `int.Parse` then throws and the check ends in a 500; the same flaw is in `LimitFormat` since Story 1.9 | high | BH, ECH; reproduced by a test with Arabic-Indic digits | patch: `[0-9]` in `LimitFormat` and the parser; tests for rounds, each limit, and an image port |
| 2 | `? &a [*a]` makes a key that contains itself; hashing it in the node model recurses without bound and would end the host | high | ECH, VG on the claim "nothing expands" | patch: an alias is an error found in the event pass; tests for four shapes, merge key included |
| 3 | Duplicate keys were found by matching the library's English message text | medium | BH, ECH | patch: found in the event pass per mapping, with the line; tests for nested mappings, flow mappings, and the same key in different mappings |
| 4 | The installation holding a repository was found with `GET /repositories/{id}`, which answers for any public repository, so an App that was removed was not reported | high | ECH | patch: the installation's own repository list is asked, as `FindRepositoryAsync` does |
| 5 | A `methodology` the file leaves out was never checked against the catalog | medium | BH, ECH | patch: the effective Methodology is checked; an installation that ships none says so (`methodology-none-shipped`) |
| 6 | Sanitising was tested at two of the call sites, so removing it from any other would pass | high | VG | patch: a theory over twelve places a value is echoed, and a command-line test |
| 7 | Multi-line commands printed with `?` for line breaks, misreporting what runs | medium | BH | patch: `ConfigurationText.Line` writes `\n`, `\r`, `\t` |
| 8 | A regex timeout would surface as a server error | low | ECH | patch: caught where the two patterns with nested groups are used |
| 9 | The reference claimed loopback and private addresses are never reachable, and "can never raise a limit", with no enforcement yet; `localhost` was both accepted and called unreachable; the no-ceiling rule and the JSON shape were undocumented | medium | BH | patch: reworded to what exists, the rule and the document described |
| 10 | The three lines that ship `methodologies/` into the image have no check | low | VG | patch: a test on `.dockerignore`, `Dockerfile`, and the project file; the image build itself is not run here |
| 11 | `{3} problem(s)` is not a plural in English | low | BH | patch: "Problems found: {3}." |
| 12 | The BOM was a raw invisible character in the source | low | BH | patch: `'\uFEFF'` |
| 13 | An Alias with an Agent and no Model would pass | none | ECH | rejected: the `Alias` type cannot hold one |
| 14 | `ReadFileAsync` repeats the credential and installation walk of `FindRepositoryAsync`, and mints a token per installation | low | BH | defer: the walk is the same cost and shape as the existing lookup; one shared helper is a refactor of Story 1.10's code |
| 15 | An error from one installation's repository list ends the read as `Rejected` | low | BH | rejected: a refused credential is what it says, and the same holds for the lookup |
| 16 | A test accepts either of two ids, or a line in a range | low | BH | patch: the line and column test is separate and exact where the library is deterministic (`line 4`); the generic syntax test keeps the range because the scanner's own position is the library's |
| 17 | Zero-width joiners in echoed values print as `?` | low | BH | rejected: a format character is exactly what must not reach a terminal |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `python3 eng/check-licenses.py` -- expected: pass

- Results, 2026-11-27: `dotnet build` 0 warnings, 0 errors; `dotnet test` 1328 passed; line coverage 95.5% overall, every new Core, GitHub, API, CLI, and catalog line covered but the three defensive catches (a fault of the second YAML pass, two regex timeouts) and one line of `ConfigurationText`; `check-licenses.py` passes.
