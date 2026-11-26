# Hephaestus PRD — Addendum

Depth captured during discovery that belongs in downstream documents (architecture, ADRs, threat model) rather than the PRD.

## Execution environment

- Owner decision: Docker with Linux containers is the foundation, not a later deployment mode. Both the orchestrator and each agent execution run in containers; the developer machine (currently Windows 11) is only the Docker host.
- Owner wants multi-platform images and Windows images for Windows-only stacks (e.g. WPF).
- Notes for architecture:
  - "Multi-platform" in Docker normally means multi-architecture Linux images (amd64 + arm64). Windows container images are a separate image family, run only on Windows hosts, and Docker Desktop runs either Linux or Windows containers at a time.
  - Modern .NET WPF/WinForms projects can be *compiled* on Linux with `EnableWindowsTargeting`; they cannot be run or UI-tested there. .NET Framework projects need Windows.
  - UNVERIFIED: whether Claude Code and Codex CLI are officially supported inside Windows containers. Must be checked before committing to Windows images.
  - Implication: the execution environment should be an abstraction (image + platform per project) so a Windows execution image can be added without touching the orchestration core.

## Agent selection through issue labels

- Owner's example labels: `hephaestus` (opt-in marker), `opus5.5` (primary), `sol6.2` (fallback), `medium` (level).
- Mechanism candidate: labels are opaque aliases resolved through configuration to (agent, model, options); the orchestrator never hardcodes model names. Per-repository defaults apply when optional labels are absent.

## Usage limits and auth

- Owner's mental model is subscription-style limits that reset over time ("wait for tokens to return, then continue"). Auth mode (API key vs subscription login) is chosen by the user per agent.
- UNVERIFIED: what each provider officially permits for unattended/automated use under subscription login, and how subscription credentials are provisioned inside a container.

## Notifications

- Owner wants email and/or phone push for "I am stuck, come help".
- Free-product constraint applies. Candidates to evaluate in architecture: SMTP (user-supplied server), ntfy (open source, self-hostable, has mobile apps), GitHub's own notifications via issue/PR mentions (zero extra infrastructure).

## GitHub identity

- GitHub does not let a pull request author approve their own pull request. If Hephaestus opened PRs with the owner's personal token, the owner could not submit an approving review, which breaks the approve-then-merge flow.
- Consequence: Hephaestus needs its own GitHub identity. Candidates: a GitHub App registered by the user (free, fine-grained permissions, bot identity) or a machine user account with a token. Decide in architecture; setup effort must be covered in installation docs.

## Stacked (cascading) branches

- Owner's model: each new task branches from the previous task's branch so the queue keeps moving while PRs await review; approved PRs are merged at the start of the next scheduled run.
- Risks to resolve in architecture: a rejected or reworked PR invalidates everything stacked above it; squash merges cause conflicts in dependent branches; a stacked PR must target its parent branch for a readable diff and be retargeted to the base branch after the parent merges; merge order must follow stack order.
- Alternative under discussion: dependency-aware stacking (branch from the base branch unless the issue declares a dependency on an unmerged task).

## Configuration trust boundary

- Owner decision: project configuration is a version-controlled file in the project's repository, keeping Hephaestus's database minimal.
- Security consequence: an agent working on a task can edit files in its branch, and a public contributor can open a pull request that edits them. If Hephaestus read configuration from a task branch, a change there could alter validation commands or weaken rules for the very run that introduced it.
- Proposed rule: configuration is read only from the base branch. Anything that defines *who is trusted* or *what secrets exist* (registered projects, trusted users, agent credentials, notification channels) lives on the Hephaestus side, never in the repository.

## Labels are unordered

- GitHub labels on an issue are a set with no order. "First model label is primary, second is fallback" cannot be expressed by label order.
- Candidates: role-prefixed labels (`primary:opus5.5`, `fallback:sol6.2`); a single profile alias that names an ordered chain; or preference order fixed in project configuration with labels only narrowing the choice.

## Versioning

- Owner decision: SemVer for Hephaestus releases.
- The versioned public surface needs defining: project configuration schema, label and command conventions, CLI, Docker image tags. While the version is 0.x, breaking changes are allowed in minor releases by SemVer convention.

## Persistence (owner input, round 5)

- Owner wants a small, simple SQL database; named MySQL as an example and ruled out full MSSQL for the default. Later: providers for MSSQL, PostgreSQL and others, and a choice between a database owned by Hephaestus and an existing database server.
- Note for architecture: MySQL is a separate server process and therefore an extra container to run, back up, and upgrade. SQLite is embedded, needs no server, is free, and is a single file on a Docker volume; with one task executing at a time its single-writer model is not a constraint. EF Core providers keep PostgreSQL, MySQL, and MSSQL available later behind the same data access code. Recommendation: SQLite as the default engine. To be decided in an ADR.
- Configuration storage: after the trust problem with in-repo configuration was raised, the owner chose to keep configuration in the database. With no dashboard in early releases, the editing surface has to be the CLI, possibly with import/export of a file for bulk edits and backup.

## Label conventions

- Role-prefixed labels chosen by owner: `primary:<alias>`, `fallback:<alias>`. Proposed by analogy and not yet confirmed: `effort:<level>`, `priority:<level>`.

## Versioning (round 5)

- Owner wants a fourth version level for finer-grained change description.
- Constraint: strict SemVer has exactly three numeric parts. Finer granularity is expressed with pre-release identifiers (`0.3.0-alpha.2`, `0.3.0-rc.1`) and build metadata (`0.3.0+build.45`). A fourth numeric part (`0.3.0.45`) is valid for .NET assembly/file versions and Docker tags but is rejected by SemVer tooling and NuGet's SemVer 2.0 rules.

## Decisions delegated to the assistant (round 6)

- **Configuration split.** Repository file (read from the base branch only): default agents and effort, validation commands, base branch, stacking mode, correction loop bound, execution environment. Installation: registered projects, schedules, trusted users, credentials, aliases, limits, notification channels. Rejected alternative: everything in the database — loses version control and review of behaviour changes and needs a full editing surface in the CLI before any dashboard exists. Residual risk for the threat model: a merged change to validation commands runs with the workspace's privileges, exactly as merged test code already does.
- **Versioning.** Strict SemVer. Rejected alternative: fourth numeric part — incompatible with SemVer tooling and NuGet SemVer 2.0.
- **Retention.** Raw agent output default 30 days; structured history permanent.

## Security mechanisms to design (from round 6)

- Agents never hold GitHub credentials: the workspace has no token; a component outside the workspace pushes the branch and calls the GitHub API.
- Egress control: per-project allowlist (package registries, provider APIs). Mechanism open: proxy container, Docker network policy, or DNS-based filtering.
- Issue body snapshot: record the body (or its hash) at the moment a trusted user applies the marker label, to detect later edits by others.
- Spend limit depends on usage data the agent reports; where an agent reports none (subscription mode), the limit cannot be enforced and this must be stated to the user.

## Research outcome (2026-11-25)

Full report: `_bmad-output/planning-artifacts/research/technical-agent-clis-and-github-integration-for-he-2026-11-25/research.md`. Status of the items marked UNVERIFIED above:

- **Subscription auth for unattended use in a container** — technically supported by both vendors (`claude setup-token` → `CLAUDE_CODE_OAUTH_TOKEN`; Codex `auth.json` persisted and refreshed). Anthropic's terms reserve subscription sign-in for ordinary individual use and allow enforcement without notice; an owner signing in to the unmodified binary with their own account fits the stated carve-out. Policy risk, not a settled permission.
- **Windows containers** — no documentation or published image for either CLI. Stays out of scope.
- **Integration surface** — CLI child processes (`claude -p --output-format stream-json`, `codex exec --json`). The Claude Agent SDK ships for Python and TypeScript only. ACP is adapter-only for both tools and not recommended for early releases.
- **Security finding** — a non-bare `claude -p` run executes hooks and connects MCP servers defined in the repository with no trust prompt; `--bare` avoids this but requires an API key. In subscription mode the repository is executable configuration inside the workspace.
  - Superseded by the injection spike (2026-11-26): `--bare` and `--safe-mode` both still apply the `env` block of the repository's settings; `--safe-mode --setting-sources user` leaves nothing and works in both authentication modes. See `docs/spikes/injection/README.md`.
- **Usage-limit detection** — depends on an undocumented Claude Code event and an unconfirmed Codex error shape; a spike against real limits is required before designing FR-27 to FR-29 in detail.
- **GitHub** — a pull request author cannot approve it (separate identity required); installation tokens get at least 5,000 requests/hour; conditional requests make polling cheap; the issue timeline `labeled` event names the user who applied a label.
- **Closest comparable** — OpenAI Symphony (open spec + Elixir reference implementation). Read its SPEC before the architecture.

## Methodology and BMAD (owner input, 2026-11-25)

- Owner requirement: Hephaestus supports BMAD to standardise how agents work, especially when a task moves between providers.
- Observed in this repository's own BMAD installation (v6.12.4, modules `core` and `bmm`): the installer was run for two tools, `claude-code` and `codex`, and wrote the same 29 skills to `.claude/skills/` and `.agents/skills/`. The same workflow definitions are therefore available to both agents from one install, which is the standardisation the owner is after.
- The install contains `bmad-build-auto`, described as "one iteration of an unattended development loop", and several skills document a headless mode (`bmad-prd`, `bmad-architecture`, `bmad-spec`, `bmad-retrospective`, among others). `bmad-build` itself starts by clarifying intent with the user.
- UNVERIFIED: which BMAD workflows complete without human input when invoked through `claude -p` and `codex exec`, and what they expect as input (BMAD works from specs, stories and a sprint status file; Hephaestus works from GitHub issues). Needs a spike.
- Architecture notes:
  - Model the methodology as data, not code: per lifecycle step (readiness check, implementation, review, correction, rework), the instruction given to the agent. The built-in default is plain role prompts; the BMAD methodology invokes the BMAD skill for that step.
  - Claude Code's `--bare` mode skips skill auto-discovery, and bare runs are not told which skills exist. A BMAD step in API-key mode therefore needs the skill named explicitly in the prompt (`/skill-name`) and the skills directory passed with `--add-dir`, or a non-bare run. To verify.
  - Mapping between a GitHub issue and BMAD's artifacts (spec or story file, sprint status) is undecided: generate the artifact from the issue and commit it in the pull request, or have the issue reference an existing story.
  - BMAD skills call `uv` and Python scripts under `_bmad/`; the execution image for a BMAD project must provide them.

## BMAD scope and the issue bridge (owner input, round 8)

- BMAD is in v0.1. Scope: implementation, review, and planning scoped to a single task. Product-level planning (brief, PRD, architecture, epics) stays interactive with the owner.
- Bridge chosen by owner: Hephaestus creates GitHub issues from the project's BMAD documents. Proposed shape: an on-demand command reads stories from the base branch, creates one issue per story with a link back to the story file, copies requirements and acceptance criteria, and turns story order within an epic into declared dependencies (which feeds stacking). The story file stays the artifact the BMAD build workflow works from; the issue is the trigger and the conversation thread.
- Open design points: where BMAD keeps story identity so re-runs are idempotent (issue number written back into the story or sprint status file vs a mapping held by Hephaestus); what happens when a story changes after its issue exists; whether `bmad-spec` or `bmad-build-auto` is the right workflow for task-scoped planning.
- v0.1 now depends on the BMAD spike (which workflows run unattended under `claude -p` and `codex exec`). Schedule it first.

## Dashboard (owner input, 2026-11-25)

Reverses the earlier position that the dashboard arrives after the three releases. Source: `sprint-change-proposal-2026-11-25.md`.

- Owner decision: a dashboard seed ships in v0.1 and grows with each epic that gives the Owner something new to see. It is read-only in v0.1 to v0.3; actions stay on GitHub and the command line.
- Owner decision: the front end is built with Angular. This supersedes React, which `init.md` named for the future dashboard.
- Owner decision on access: the Hephaestus process serves the dashboard on a second, TCP listener published only on the host's loopback address, behind a token issued from the command line. Rejected alternatives: a separate, unprivileged dashboard container (keeps browser traffic out of the process that holds the Docker socket, at the price of another component; still possible later without changing the API); access from the local network with sign-in and TLS (pulls authentication and remote access, deferred to Server Mode, into v0.1).
- Owner decision on live updates: Server-Sent Events, a one-way stream that says what changed; the dashboard then reads through the ordinary read endpoints. Rejected alternatives: asking again on a timer (a view could be silently stale); SignalR (its browser transport carries the token in the address, its negotiation is a write request, and its channel is two-way, which the read-only rule would have to make three exceptions for). SignalR becomes relevant if the dashboard ever gains actions.
- For the architecture: the listener must serve reads only by construction, so that a stolen token cannot change anything; the token travels in a request header, never in a cookie or an address; the dashboard loads nothing from another origin; the orchestrator container stays off the workspace network.
- For the threat model: the process that holds the Docker socket now answers HTTP on loopback. New threats: another local process reading the dashboard, a web page attacking it through the browser, script injected through issue text or Agent output shown in it, a stolen token, exhaustion through open streams.
- Not live: the output of a step while it runs. The architecture parses a step's stream after its container exits.
- Consequence for earlier notes: "with no dashboard in early releases, the editing surface has to be the CLI" still holds, because the dashboard edits nothing.
