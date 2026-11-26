---
title: 'Story 1.4: Run Hephaestus with Docker Compose'
type: 'feature'
created: '2026-11-26'
status: 'done'
baseline_commit: '7534ebd4586662e5016bc9a62615a98947784601'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** There is nothing to run: no host process, no image, no Compose file, no command line, and no message mechanism for the two languages, so the Owner cannot start Hephaestus or talk to it (FR-58, NFR-16, AD-17, AD-21).

**Approach:** Build the smallest vertical slice. One executable, `hephaestus`, is the host (`serve`) and the command line (every other argument). The host listens on a Unix domain socket only and answers `GET /api/version`; the `version` command calls it and prints the answer. Add the message mechanism in Core, JSON logs on stdout, `deploy/Dockerfile` for both architectures, and `deploy/compose.yaml`. No domain code.

## Boundaries & Constraints

**Always:**
- Every sentence for a person is a message id with parameters, resolved in Core from `en` and `pl` resource files; handlers and the client pass ids. A test fails when an id lacks an entry in a language, or when the two files differ.
- The CLI only calls the API and renders its answer. `--json` prints the API's document and nothing else on standard output; errors go to standard error as title, then detail; exit 0, 1 (a failure, including an unreachable API), 2 (not understood). `--help` and `-h` everywhere; no prompt.
- A failed API request is an RFC 9457 problem document (also 404, 405, and unhandled exceptions); `type` is never translated.
- The control API listens on a Unix socket and on no network address, whatever `ASPNETCORE_*` says. The container publishes no port.
- Logs: one JSON object per line on stdout through `Microsoft.Extensions.Logging`.
- Packages only at the Stack pins (System.CommandLine 2.0.15, YamlDotNet 18.1.0); Core references no ASP.NET Core, Docker, EF Core, or Octokit package; line coverage stays at or above 80%.

**Never:**
- No dashboard listener, port, or token (1.14); no persistence, secrets, Docker socket mount, or egress proxy; no image publishing workflow (1.13); no SonarQube step (1.16); no installation-language setting (1.9; here the language comes from `HEPHAESTUS_LANGUAGE`, default `en`).
- No change to `_bmad/`, skills, `SPEC.md`, the architecture, `epics.md`, or an ADR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Version | `hephaestus version`, host running | stdout: one sentence with the version; exit 0 | N/A |
| Version, JSON | `hephaestus version --json` | stdout: exactly `{"version":"x.y.z"}`; same in `en` and `pl` | N/A |
| Polish | `HEPHAESTUS_LANGUAGE=pl` | Sentence differs from English; command, options, JSON, exit code identical | Unknown value falls back to `en` |
| Host down | no socket or nobody listening | stderr: title, then detail naming the socket; exit 1 | N/A |
| API failure | unknown path, wrong method, unhandled exception | problem document with `type`, `title`, `detail`, `status`; CLI prints title and detail to stderr; exit 1 | Non-problem error body: generic message, exit 1 |
| Not understood | unknown command or option | stderr: title, detail; exit 2 | N/A |
| Help | `--help`, `-h`, `version --help` | Usage, commands, options, in the chosen language; exit 0 | N/A |

</frozen-after-approval>

## Code Map

- Spine AD-17 (listeners, `docker exec`), AD-21 (message ids, one language function), Consistency Conventions (Logging, API errors, Public surface), "Deployment and operations"; `EXPERIENCE.md` "Command Line" (grammar, output, exit codes); epics Story 1.4.
- `src/Hephaestus.Host/Hephaestus.Host.csproj` -- empty class library today; becomes the Web SDK executable with assembly name `hephaestus`.
- `src/Hephaestus.Core/` -- empty; gains `Localization/` (language, resolver, catalog, ids, `Messages.resx`, `Messages.pl.resx`) and the version.
- `tests/Hephaestus.Host.Tests/Architecture/` -- boundary tests must keep passing (Host may name ASP.NET Core).
- `tests/Hephaestus.EndToEnd.Tests/` -- `AllowNoTests` is removed when its first test lands.
- `.github/workflows/ci.yml`, `eng/check-coverage.py`, `eng/coverage-exclusions.txt` -- CI gains an image job; the gate maps reports to `src/` projects.
- `deploy/README.md` lists `compose.yaml` and `Dockerfile` as Story 1.4; `README.md` has only the badge.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/Localization/*`, `Messages.resx`, `Messages.pl.resx`, `HephaestusVersion.cs` -- message ids, catalog, language resolver, both languages, version
- [x] `src/Hephaestus.Host/` -- `Program`, serve (Kestrel on a Unix socket, problem details, JSON logging), `GET /api/version`, CLI (System.CommandLine tree, help, Unix-socket client, output rules)
- [x] `tests/` -- Core message-completeness and resolver tests; Host in-process tests (API, problem documents, client, CLI output and exit codes, logging, listener); compose-file contract test; end-to-end subprocess test
- [x] `deploy/Dockerfile`, `deploy/compose.yaml`, `.dockerignore` -- multi-arch image, one hardened `hephaestus` service with a health check, no ports
- [x] `.github/workflows/ci.yml` -- image job on amd64 and arm64: build, `compose up --wait`, version command, no published port
- [x] `README.md`, `deploy/README.md` -- how to start Hephaestus and run the command line

**Acceptance Criteria:**
- Given Docker 29 and `deploy/compose.yaml`, when `docker compose up -d` runs, then one container `hephaestus` becomes healthy and `docker exec hephaestus hephaestus version` prints the version.
- Given the container, when inspected, then the API listens on a Unix socket only and no port is published.
- Given the image build on linux/amd64 and linux/arm64, then both succeed and the container starts (CI image job).

## Implementation Notes

- 2026-11-26. Implemented directly in the session that held the plan. Message resources are `Messages.en.resx` and `Messages.pl.resx`, embedded as two resource sets (`WithCulture="false"`) and looked up by name: the build runs in globalization-invariant mode, which cannot create the `pl` culture a satellite assembly needs.
- The assembly keeps the project name `Hephaestus.Host` (the coverage gate maps reports to `src/` projects by it); the image publishes the executable as `hephaestus` (`deploy/Dockerfile`).
- The version is `VersionPrefix` 0.1.0 in `Directory.Build.props` until Story 1.13 names the one file that states it.
- No Docker daemon was available where this was built, so the Dockerfile and Compose file are checked by `docker compose config`, contract tests over their text, and the CI `Image` job on amd64 and arm64, which builds the image, starts Compose with `--wait`, and checks the command line, the published ports, the listeners, the user, the socket mode, the language, and the JSON logs.
- A configured Kestrel endpoint (`Kestrel__Endpoints__*`) adds a network listener that the endpoints given in code do not replace; the host refuses to start with one.
- The `Image` checks are not required until the Owner adds them to branch protection (`docs/branching.md`).

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-26. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | A 2xx answer that is not the version document crashes the CLI | medium | BH, ECH; `JsonException` or null dereference escaped | patch: bad-answer message, exit 1; tests |
| 2 | Caller cancellation and timeout reported as "cannot reach" | low | BH, ECH, VG | patch: cancellation propagates; a timeout still says nothing answered |
| 3 | Socket mode set after bind; directory created with default mode | medium | BH, ECH, VG | patch: directory created 0700; the image's directory is 0700; mode set at start |
| 4 | A second `serve` unlinks a live host's socket; any file at the path is deleted | medium | BH, ECH | patch: refuse when the socket answers; test |
| 5 | A Kestrel endpoint from configuration adds a network listener | high | ECH; reproduced with `Kestrel:Endpoints:Http:Url` | patch: host refuses to start; test |
| 6 | `StubApi` leaks a temp directory | low | ECH | patch |
| 7 | `.editorconfig` missing from the image build context | medium | BH | patch: copied |
| 8 | Dead `.dockerignore` entries | low | BH | patch |
| 9 | `latest` tag plus `build` runs a stale local image | medium | BH, ECH | patch: `pull_policy: build` |
| 10 | Health check floods the log; no log rotation | medium | ECH | patch: `Microsoft.AspNetCore` at Warning; json-file rotation |
| 11 | UDP listeners not checked in CI | low | ECH | patch |
| 12 | Language passthrough, non-root user, socket mode, and restart untested at runtime | medium | VG | patch: compose test and CI steps; restart not added (rejected: the stale-socket path is covered in process) |
| 13 | Stale comment naming `Messages.resx` | low | VG | patch |
| 14 | Host stderr not drained in the end-to-end test | low | BH, ECH | patch |
| 15 | Problem media type compared case-sensitively | low | ECH | patch |
| 16 | `default(Message)` has null arguments | low | BH, ECH | patch |
| 17 | `Accept-Language` q-values ignored | low | BH, ECH | rejected: the only client names one tag; a fix adds parsing |
| 18 | Unknown `HEPHAESTUS_LANGUAGE` falls back silently | low | BH | rejected: stated in the Matrix; Story 1.9 refuses bad values |
| 19 | Base images not pinned by digest | low | BH | rejected: Story 1.13 owns the release pipeline |
| 20 | Docker Engine 29 not asserted | low | BH | rejected: the Compose file uses no 29-only feature; the requirement is the story's premise |
| 21 | New CI checks are advisory until the Owner requires them | medium | BH | defer: only the Owner administers branch protection; listed in `docs/branching.md` and the pull request |
| 22 | Dockerfile asserted as text; Kestrel log text asserted | low | BH | rejected: CI runs the real build; the log text is stable |
| 23 | Reflected request path in the 404 detail; `RequestAborted` token in the handler; empty informational version | low | BH, ECH | rejected: JSON-encoded, no markup; no harm shown |

## Design Notes

- The server resolves problem text in the language the client sends in `Accept-Language` (the CLI's own language), else the installation's, through the one Core function; so the CLI prints the server's title and detail verbatim and both agree.
- Server mode is the first argument `serve`, absent from the public command tree and from `--help`.
- Parse-error text from System.CommandLine is never shown (it is library prose); the CLI prints its own message ids, naming the unmatched token.

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `docker compose -f deploy/compose.yaml config` -- expected: valid
