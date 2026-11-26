# Epic 1 Context: Install Hephaestus and register a Project

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

The Owner runs Hephaestus with Docker Compose, connects its GitHub App identity, manages Trusted Users, Aliases, limits, and secrets from the command line, registers a repository as a Project, and is told exactly which setting is wrong when `hephaestus.yml` is missing or invalid. Two spikes gate the release and run first, so their findings shape everything built after them: one proved that the Agent CLIs can be injected into a hardened Project container behind an egress proxy (done, both decisions confirmed), the other proves which BMAD workflows run unattended. The epic also sets the rules for Hephaestus's own repository (branches, coverage, quality gate, release channels) and adds the first Dashboard views.

## Stories

- Story 1.1: Injection spike — Agent CLIs in a hardened container behind a proxy
- Story 1.2: BMAD spike — unattended workflows under both Agents
- Story 1.3: Solution skeleton with enforced boundaries
- Story 1.4: Run Hephaestus with Docker Compose
- Story 1.5: Store secrets outside the database
- Story 1.6: Mask secret values wherever text is written
- Story 1.7: Connect Hephaestus's own GitHub identity
- Story 1.8: Manage Trusted Users from the command line
- Story 1.9: Define Aliases, limits, and protected paths
- Story 1.10: Register a Project
- Story 1.11: Check a Project's configuration file
- Story 1.12: Upgrade without losing configuration or history
- Story 1.13: Publish versioned releases
- Story 1.14: Open the Dashboard on my own machine
- Story 1.15: See Projects and configuration in the Dashboard
- Story 1.16: Hold every pull request to the quality gate

## Requirements & Constraints

- Installation is Docker Compose only; one long-running `hephaestus` container. Nothing beyond GitHub, Anthropic, and OpenAI is needed at runtime.
- Only free and open-source dependencies, at the pinned versions of the architecture's Stack table. The project is MIT-licensed; vendor Agent binaries are never redistributed.
- Secrets are entered through standard input only, live as files on a volume mounted only into the `hephaestus` container, and never appear in the database, logs, labels, or output. A masker covers plain, base64, and URL-encoded forms.
- Hephaestus implements no sign-in flow. The Owner signs in with the vendor's own tool and supplies the credential. Each Agent supports an API key; subscription sign-in is offered only where it is proven safe. Claude Code supports it through a `claude setup-token` token. Codex CLI is API-key only: its subscription credential is a full account credential file that must be kept and refreshed between runs.
- Project Configuration is `hephaestus.yml` on the Base Branch only; a repository value can never raise an installation limit. Validation errors name the exact setting.
- Every sentence for a person is a message id resolved from `en` and `pl` resources; command names, `--json` output, labels, state names, and configuration keys are never translated.
- Line coverage stays at or above 80% overall and on new code. Every change arrives by pull request into `develop`; `main` receives only `develop` or `hotfix/*`. SonarQube Cloud analysis is secretless.
- No security claim appears in user-facing text without a passing test behind it.
- Stories 1.1 and 1.16 need the Owner's accounts or credentials and are not closed by a dev agent alone.

## Technical Decisions

- Hexagonal modular monolith on .NET 10 / C# 14 over SQLite. `Hephaestus.Core` references no adapter and no Octokit, Docker, EF Core, or ASP.NET Core package; adapters (`Agents`, `GitHub`, `Workspaces`, `Persistence`) reference only Core; only `Host` references adapters. The Dashboard is an Angular 22 workspace in `src/Hephaestus.Dashboard`, built into static files.
- Only the orchestrator loop writes Task state, through `IUnitOfWork`. Everything else appends a Signal. Reads go through `IReadStore` on a read-only connection.
- Two listeners: a Unix-socket control API that carries every changing endpoint (the CLI is the same executable in client mode, run with `docker exec`), and a TCP dashboard listener on `127.0.0.1:8717` that is read-only and token-protected.
- Step container contract (confirmed by the injection spike on linux/amd64): Task volume at `/workspace`, mounted `volume-nocopy`; context at `/hephaestus/context`; Methodology at `/hephaestus/methodology`; Agent binaries read-only at `/hephaestus/agents`, with a CA bundle at `certs/ca-certificates.crt` beside them; throwaway `HOME`, a tmpfs, at `/hephaestus/home`; user `10001:10001`; all capabilities dropped; `no-new-privileges`; default seccomp; process, time, CPU, and memory limits. The Agent's own sandbox and permission prompts are off inside it.
- The workspace network is a Docker internal network whose only exit is a separate, unprivileged egress proxy with no credential, volume, or Docker socket. The runtime sets the generic proxy variables in both cases and rewrites `github.com` SSH remotes to HTTPS; ecosystem-specific proxy settings belong to the Project image (Gradle and Maven need them), which is why the proxy is reached at one fixed, documented host name and port. Egress hosts an Agent needs are declared by its adapter per authentication mode.
- Agent CLIs are pinned per release in `deploy/agents.lock`: one version per CLI and a SHA-256 per executable, per architecture and C library (Claude Code in a glibc and a musl build; Codex CLI as `codex` and `codex-code-mode-host`). They are downloaded from the vendor's official channel at startup, verified, and mounted read-only into the Project's own image; the runtime mounts the build that matches the image's C library. Project image contract: Linux, glibc or musl, `bash` present, nothing else. Self-update is off. There is no Hephaestus base image.
- Every invocation must disable account-linked tools that act outside the container and repository-supplied executable Agent configuration (hooks, MCP servers, settings, plugins); a mode where the planted-configuration test cannot show this is declared unsupported. An invocation takes Agent settings only from the throwaway `HOME` and offers a closed tool list: hardened Claude Code has `Bash`, `Read`, `Edit`, and `Write`, with no `Skill` and no `Task` tool. Nothing is claimed about repository plugins.
- Neither Agent is given the repository's own instruction file (`CLAUDE.md`, `AGENTS.md`; Codex runs with `project_doc_max_bytes=0`). No step depends on a CLI's skill, sub-agent, or slash-command mechanism: instructions reach an Agent as files it reads. Codex CLI still offers a repository's skills to the model.
- The hardened invocations that held, and the harness that ran them, are in `docs/spikes/injection/`; later spikes and the adapters start from them.
- Build conventions: nullable on, warnings as errors, central package management. Entity ids are UUID v7 as text; time is UTC ISO 8601 text read from `TimeProvider`; JSON and YAML use camelCase keys and kebab-case enums; API errors are RFC 9457 problem details.
- Persistence: one `DbContext`, one migration set, SQLite in WAL mode; the database is backed up before migrations run at startup.

## UX & Interaction Patterns

- The command line is the primary surface in this epic; consult the UX documents (`EXPERIENCE.md`, `DESIGN.md`) before adding a command or a Dashboard view.
- The Dashboard is read-only, loads nothing from another origin, and renders GitHub and Agent text as text, never as markup.

## Cross-Story Dependencies

- Story 1.1 is done and its findings are in the plan. Story 1.2 depends only on 1.1 and reuses its container setup and hardened invocations; a workflow that needs a tool or setting those invocations switch off is raised with the Owner, not worked around.
- Story 1.3 precedes all code stories. Story 1.16 depends only on 1.3 and should land early.
- The proof-of-concept path through this epic is 1.1, 1.3 to 1.11; stories 1.2 and 1.12 to 1.16 follow or run beside it. Dashboard stories 1.14 and 1.15 need 1.5.
