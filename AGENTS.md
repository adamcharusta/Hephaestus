<!-- bmad:context -->
<!-- Verified 2026-11-25 against 0731346. Managed by bmad-project-context; edits inside this block are replaced on refresh. Keep anything you want preserved outside the markers. -->

## Hephaestus

A self-hosted orchestrator that turns labelled GitHub issues into validated, independently reviewed pull requests by driving Claude Code and Codex CLI in hardened containers. .NET 10 and C# 14 as a modular monolith over SQLite, an Angular 22 Dashboard, Docker Compose. The repository holds the plan, written with BMAD Method, and an empty solution skeleton (Story 1.3); no feature code exists yet. Planning lives in `_bmad-output/`; the architecture overview and decision records rendered from it live in `docs/`.

## Policy

- Never push to `develop` or `main`; open a pull request into `develop`. `main` receives only `develop` or a `hotfix/*` branch.
- Build one story at a time with `bmad-build`, taking it from `_bmad-output/implementation-artifacts/sprint-status.yaml`. Never implement work that has no story.
- Never change scope, a requirement, or an architecture decision while implementing. Stop and run `bmad-correct-course`; each document is then updated by its own skill (`bmad-prd`, `bmad-architecture`, `bmad-spec`, `bmad-ux`, `bmad-create-epics-and-stories`).
- Never hand-edit `SPEC.md` or its companions under `_bmad-output/specs/`; they are derived, so change them through `bmad-spec`. Change `sprint-status.yaml` only through `bmad-build` and `bmad-sprint-planning`.
- Never edit `_bmad/`, `.claude/skills/`, or `.agents/skills/`; they are the installed BMAD. Customise through `bmad-customize`.
- Never close Story 1.1, 1.16, or 7.1 alone; each needs the Owner's accounts or credentials.
- Never add a SonarQube token or any other credential to the repository or its secrets; analysis is secretless (`docs/adr/0020-branches-quality-gate-and-image-channels.md`).
- Add only free and open-source dependencies, at the versions the spine's Stack table pins. Check the registry before proposing a different version.
- Never report a pull request as ready before its SonarQube Cloud check has finished. Read the result with `gh pr checks <number>` after opening the pull request and after every push to it, and fix what it reports on the new code. Name any issue left open, and why.
- Keep line coverage at or above 80%, over all code and over the code a change adds. Add tests with the change; an exclusion needs a stated reason.
- Write everything that is saved in English, whatever language the conversation is in: code, comments, commit messages, pull request text, issues, and documents. The only Polish text in the repository is the `pl` resource files.
- Update `README.md` in the same pull request whenever a change alters what its reader needs: how to install, run, or configure Hephaestus, a command, a requirement of the host, or what a release can do. Keep it to what exists; link to `docs/` for the rest.

## Where things are

- What to build: `_bmad-output/specs/spec-Hephaestus/SPEC.md` and the files in its `companions:` list.
- How to build it: `_bmad-output/planning-artifacts/architecture/architecture-Hephaestus-2026-11-25/ARCHITECTURE-SPINE.md` (AD-1 to AD-21, conventions, pinned Stack). It wins over `docs/`.
- Stories and acceptance criteria: `_bmad-output/planning-artifacts/epics.md`. Take the order of work from its "Proof-of-concept path" section, not from story numbers.
- Adding a command, a Dashboard view, or text posted on GitHub? Read `EXPERIENCE.md` and `DESIGN.md` in `_bmad-output/planning-artifacts/ux-designs/ux-Hephaestus-2026-11-25/` first.
- Writing a security test or a security claim? Read section 8 of `docs/security/threat-model.md` first.
- Why a decision was made: the `.memlog.md` beside each planning document, and `docs/adr/`.

## Running and verifying

- .NET solution (Story 1.3), from the repository root: `dotnet build Hephaestus.slnx` (warnings are errors), `dotnet test --solution Hephaestus.slnx`. Coverage and its gate, as CI runs them: `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults`, then `python3 eng/check-coverage.py TestResults`. The license gate: `python3 eng/check-licenses.py` after a restore. The gates' own tests: `python3 -m unittest discover -s eng -p "test_*.py"`. TODO (Story 1.14): the same for the Angular workspace in `src/Hephaestus.Dashboard`; it does not exist yet, do not guess an invocation.
- Run BMAD scripts with `uv run` from the repository root, for example `uv run _bmad/scripts/resolve_config.py --project-root .`.

## Conventions that differ from defaults

- Only the orchestrator loop writes Task state, through `IUnitOfWork`. Everything else appends a Signal; every endpoint reads through `IReadStore`.
- `Hephaestus.Core` references no adapter project and no Octokit, Docker, EF Core, or ASP.NET Core package. Adapters reference only Core.
- Only `Hephaestus.GitHub` talks to GitHub or pushes. No GitHub credential ever enters a Workspace.
- Map an endpoint that changes anything on the Unix-socket control API. The dashboard listener carries reads only.
- Write every sentence meant for a person as a message id resolved from the `en` and `pl` resources. Never translate command names, `--json` output, labels, state names, or configuration keys.
- Name types and members with the Glossary's terms (`Task`, `Workspace`, `Finding`, `Escalation`).
- Make no security claim in user-facing text without a passing test behind it.

## Known pitfalls

- Search the `.memlog.md` files before saying that something was or was not decided. A session claimed the dashboard had never been discussed when the spec's log recorded it as parked.

<!-- /bmad:context -->
