---
title: 'Story 1.3: Solution skeleton with enforced boundaries'
type: 'feature'
created: '2026-11-26'
status: 'done'
route: 'dispatch'
baseline_commit: '8d4bacc3f9202afc91ff6cb7e1f26373006bc0ae'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** No code exists, so nothing stops a later change from coupling modules the architecture keeps apart, from merging below the coverage floor, or from adding a dependency that is not free and open-source (AD-3, NFR-15, NFR-18).

**Approach:** Scaffold the .NET 10 solution to the spine's source tree with the build rules set once, add an architecture test that reads the reference graph, and add the GitHub Actions workflow, coverage gate, license gate, and repository documents the story lists. No behaviour beyond that: every project is empty until its own story.

## Boundaries & Constraints

**Always:**
- Package versions only in `Directory.Packages.props`, at the spine's Stack table; free and open-source packages only.
- Nullable on and warnings as errors for every project, set in `Directory.Build.props`.
- Adapters (`Agents`, `GitHub`, `Workspaces`, `Persistence`) reference only Core; only Host references adapters; Core references no adapter project and, directly or transitively, no Octokit, Docker, EF Core, or ASP.NET Core package or framework.
- Coverage exclusions live in one file, each with a stated reason.
- All saved text is English.

**Never:**
- No change to `_bmad/`, the skills, `SPEC.md`, the architecture, `epics.md`, or an ADR; no SonarQube token or other credential; no Angular workspace (Story 1.14), Dockerfile or Compose file (1.4), release or image workflow (1.13), or SonarQube scanner step (1.16).
- No branch protection set from here; the Owner alone administers the repository.
- No domain code, ports, or entities.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Forbidden reference | Core gains a ProjectReference to an adapter, or an adapter to another adapter or to Host, or Core resolves an Octokit, Docker, EF Core, or ASP.NET Core package or framework | Architecture test fails and names the project and the reference | N/A |
| Warning or missing nullable | Any project overrides Nullable or warnings-as-errors | Test fails; a compiler warning fails the build | N/A |
| Coverage | Merged line coverage over all code below 80%, or an exclusion with no reason | Gate exits non-zero and says which | No coverable lines: pass with a notice |
| License | A direct or transitive package whose license is outside `eng/allowed-licenses.txt` | Check fails and names the package and license | A license given only as a URL needs a reasoned override entry |

</frozen-after-approval>

## Code Map

- `AGENTS.md` -- "Running and verifying" holds the TODO for Story 1.3 commands; fill it in.
- Spine "Source tree", "Stack", "Branches and checks" conventions; `docs/adr/0020-*.md` (branches, 80% gate, secretless analysis owned by Story 1.16).
- `.gitignore` already ignores `bin/`, `obj/`, `TestResults/`, `coverage/`; `.gitattributes` forces LF on `*.sh`.
- `README.md` holds only the SonarQube badge; `docs/` holds `adr`, `security`, `spikes`; no `.github/`, `deploy/`, `methodologies/`, `LICENSE` yet.
- Registry check (2026-11-26): every Stack version exists on nuget.org. Not pinned by the Stack, chosen here: `coverlet.mtp` (MIT) for coverage under xunit.v3's Microsoft.Testing.Platform runner.

## Tasks & Acceptance

**Execution:**
- [x] `global.json`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `NuGet.config`, `.editorconfig`, `Hephaestus.slnx` -- SDK pin, MTP runner, build rules, central versions at the Stack pins, nuget.org only
- [x] `src/Hephaestus.{Core,Agents,GitHub,Workspaces,Persistence,Host}/` -- six empty class libraries with the allowed references only
- [x] `tests/Hephaestus.{Core,Agents,GitHub,Workspaces,Persistence,Host}.Tests/`, `tests/Hephaestus.EndToEnd.Tests/` -- xunit.v3 projects; architecture, layout, and build-rule tests in `Host.Tests`
- [x] `eng/check-coverage.py`, `eng/coverage-exclusions.txt`, `eng/check-licenses.py`, `eng/allowed-licenses.txt` and their `unittest` tests -- the two gates
- [x] `.github/workflows/ci.yml` -- on pull requests to `develop` and `main`: build, test with coverage, coverage gate, license gate, gate tests; actions pinned by commit
- [x] `LICENSE`, `README.md`, `docs/branching.md`, `deploy/README.md`, `methodologies/default/manifest.yml`, `methodologies/bmad/README.md`, `AGENTS.md` -- MIT, build and test commands, how a feature, release, and hotfix reach each branch and what the Owner must protect, the known `default` Methodology

**Acceptance Criteria:**
- Given a fresh clone, when `dotnet build` runs with the .NET 10 SDK, then it succeeds with no warning and the six source and seven test projects exist.
- Given the architecture test, when a forbidden reference is modelled, then the test fails; given the real solution, it passes.
- Given a pull request, when CI runs, then build, tests, coverage gate, and license gate report on it.

## Implementation Notes

- 2026-11-26. Implemented directly from this spec in the session that held the plan, not by a separate subagent. Written by the one session that resumed the story after the earlier run left no commits, branch, or pull request behind.
- The restore result (`obj/project.assets.json`) is what the architecture test reads; `Host.Tests` holds the checker, its modelled-graph tests, and the reader's fixture test. Host is exempt from the vendor-package rule except for packages it names itself, because it resolves every adapter's libraries transitively.
- The test runner is Microsoft.Testing.Platform (`global.json`), which xunit.v3 and `coverlet.mtp` (MIT, not in the Stack table, chosen here) both support. A test project that holds no test yet sets `AllowNoTests`, which `Directory.Build.targets` turns into `--ignore-exit-code 8`; `Host.Tests` does not, so a discovery failure there fails the build.
- The solution holds no feature code, so line coverage has no coverable lines and the gate passes with a notice. The gate still fails when a project under `src/` is missing from the reports, which keeps a run that collected nothing from passing. It was exercised once against real coverlet output with a temporary class (50% failed, 100% passed, deterministic CI paths handled); the class is not in the change.
- Not done here, by scope: SonarQube analysis (Story 1.16), the release and image workflow (1.13), Dockerfile and Compose (1.4), the Angular workspace (1.14). Branch protection and the required checks are the Owner's, listed in `docs/branching.md`. No code-owner rule is added for `eng/` and `.github/`: it needs a branch-protection setting only the Owner can make, so it is left to the Owner.
- `methodologies/default/manifest.yml` holds only `name` and `description`; the manifest's fuller schema belongs to the story that first reads it.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-26. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap. Findings that name the same defect are one row.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | Compiled `__pycache__` files are in the change | medium | BH, ECH, VG; `.gitignore` had no entry | patch: ignored, removed |
| 2 | The reader fails on a `src/` directory with no project (the Dashboard, Story 1.14), and its six-project assertion | medium | ECH, BH; `ReadSourceProjects` listed directories | patch: directories with a `.csproj` only |
| 3 | The reader is untested; a mutation of it left every test green | high | VG ran the mutation | patch: `ProjectGraphReaderTests` with a fixture |
| 4 | Framework references a package brings in, and a second target framework, are not read | medium | ECH; reader looked at the first framework's own list | patch |
| 5 | Host is exempt from every vendor-package rule | medium | BH, ECH; a direct `Octokit` reference in Host passed | patch: Host's own named packages are checked |
| 6 | Vendor packages under another prefix (`Microsoft.Data.Sqlite`, `Swashbuckle.AspNetCore`) pass | medium | ECH | patch: segment match, plus `Microsoft.Data.Sqlite` |
| 7 | `--ignore-exit-code 8` applies to every test project, `Host.Tests` included | high | VG ran a filter that selected nothing: exit 0 | patch: opt-in `AllowNoTests`, in the empty projects only |
| 8 | A source project missing from the reports, or the list of them in the workflow, drifts; `AGENTS.md` drops it | medium | BH | patch: the gate derives the list from `src/*/*.csproj` |
| 9 | Coverage paths under a CI build (`ContinuousIntegrationBuild`) | medium | ECH; run with `GITHUB_ACTIONS=true` gave a path without its leading slash | patch: both forms made relative; tests |
| 10 | Malformed report, a mistyped report path, backslash paths | low | ECH | patch |
| 11 | Pre-release version in the lowercase cache folder; malformed `.nuspec` or restore result | low | ECH | patch |
| 12 | A license override keyed by package id only hides a later license change; no overrides file | medium | BH, ECH | patch: keyed by id and version; file added |
| 13 | `allowed-licenses.txt` says copyleft is absent but lists MPL-2.0; its refusals are untested | low | BH, VG-style | patch: reason stated; test of the shipped list |
| 14 | `AnalysisLevel=latest` and NuGet audit make a build depend on the SDK and on the day | medium | BH, ECH | patch: `AnalysisLevel` pinned to 10.0; audit advisories stay warnings, stated |
| 15 | Build-rule test cannot see other props files, `WarningsNotAsErrors`, `#nullable disable`, versioned `PackageReference Update` | medium | BH, ECH | patch |
| 16 | The layout test scans the whole repository for projects, and `tests` matches `testsupport` | low | BH, ECH | patch: `src` and `tests` only; separator in the condition |
| 17 | `main` accepts a pull request from any branch although the rule says `develop` or `hotfix/*` | medium | BH | patch: `Branch policy` job; named in `docs/branching.md` |
| 18 | README omits the Python gates; coverage commands | low | BH | patch |
| 19 | Spec checkboxes, `.gitkeep` versus `README.md`, verification command differ from the change | low | BH, ECH | patch: spec updated outside the frozen block |
| 20 | Gates can be edited by the pull request they judge; no code-owner rule | medium | BH | defer: needs a branch-protection setting the Owner alone makes; stated in notes and `docs/branching.md` |
| 21 | New-code coverage is not computed by the gate | low | ECH | rejected: SonarQube Cloud's gate owns it (ADR-0020, Story 1.16) |
| 22 | The gate passes with no coverable lines | medium | ECH, VG | rejected: the skeleton holds no code, the matrix row asks for a notice; the missing-project check stops a vacuous run |
| 23 | Test code counts toward coverage | medium | BH | false: the reports list only the six source assemblies; coverlet excludes the test assembly |
| 24 | No update path for pinned actions; no coverage artifact; no Python setup; stacked branches get no CI | low | BH, ECH | rejected: outside the story; the policy names `develop` and `main` only |
| 25 | `Directory.Build.props` is brittle on the manifest line match, `StackPins` duplicates the spine | low | BH, ECH | patch for the manifest match; the pins duplicate on purpose, so a change to the table is a visible change here |
| 26 | LICENSE copyright holder chosen without a cited decision | low | BH | rejected: the repository's author is the Owner; the Owner can change the line |

## Design Notes

The reference graph is read from each project's `obj/project.assets.json` (full transitive closure, framework references included), so a package pulled in indirectly is caught. The checker is plain code in `Host.Tests`, exercised against modelled graphs for the failing cases.

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass
- `python3 eng/check-coverage.py TestResults` (after `dotnet test --coverlet`) and `python3 -m unittest discover -s eng -p "test_*.py"` -- expected: success
