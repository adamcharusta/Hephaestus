---
id: SPEC-Hephaestus
companions:
  - glossary.md
  - requirements.md
  - user-journeys.md
  - release-scope.md
  - success-metrics.md
  - ../../planning-artifacts/architecture/architecture-Hephaestus-2026-11-25/ARCHITECTURE-SPINE.md
  - ../../../docs/security/threat-model.md
  - ../../planning-artifacts/ux-designs/ux-Hephaestus-2026-11-25/DESIGN.md
  - ../../planning-artifacts/ux-designs/ux-Hephaestus-2026-11-25/EXPERIENCE.md
sources:
  - ../../planning-artifacts/prds/prd-Hephaestus-2026-11-25/prd.md
  - ../../planning-artifacts/prds/prd-Hephaestus-2026-11-25/addendum.md
  - ../../planning-artifacts/sprint-change-proposal-2026-11-25.md
  - ../../planning-artifacts/sprint-change-proposal-2026-11-25-language-and-ci.md
  - ../../planning-artifacts/sprint-change-proposal-2026-11-26.md
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Hephaestus

*Forge software. Orchestrate intelligence.*

## Why

A pain and a vision. A developer who already pays for coding agents can delegate only one task at a time, and only while watching the agent work. Hephaestus is an open-source (MIT), self-hosted orchestrator that turns labelled GitHub issues into pull requests already built, tested, and reviewed by a second, independent Agent, driving Agents the Owner already has. It hands a Task to another Agent or waits when a Usage Limit is reached, asks when it needs a decision and moves on to other work, and applies one Methodology whichever Agent performs a step. The Owner keeps intent and acceptance: nothing merges without their explicit approval and nothing is ever deployed. It is built first for its owner's daily work across several personal projects, then for anyone who wants the same on their own machine.

Capitalized terms are defined in `glossary.md`. Each capability below is detailed, requirement by requirement, in `requirements.md`.

## Capabilities

- **CAP-1 — Project setup and configuration** (FR-1 to FR-5)
  - **intent:** The Owner registers a GitHub repository as a Project and governs how Hephaestus works on it through a version-controlled Project Configuration, while trust, secrets, Schedules, and limits stay in the Installation Configuration.
  - **success:** A Project in any stack that builds and tests in a Linux container is added by configuration alone; no change on a Task branch or by an outside contributor alters trust, credentials, Schedules, or limits; no credential value appears in any repository, GitHub text, or stored log.

- **CAP-2 — Task intake** (FR-6 to FR-11)
  - **intent:** A Trusted User turns an issue into a Task with the Marker Label, optionally choosing Agents, Models, and Effort with Role Labels; Hephaestus queues it, checks that it is clear enough to implement, and shows its state on the issue.
  - **success:** Only a Marker Label applied by a Trusted User creates a Task; Queue order follows Priority Label, then issue number, never ahead of an unsatisfied declared dependency; an unclear issue yields an Escalation, not code; the state on the issue is current within one polling cycle.

- **CAP-3 — Scheduling** (FR-12 to FR-16)
  - **intent:** The Owner gives each Project its own working days and hours, or starts it on demand, and Hephaestus works one Task at a time across the installation.
  - **success:** No new Task starts outside its Project's Schedule or after the end time; a running Task finishes past the end time; an interrupted Task is resumed before any new one starts; two Tasks never execute at once.

- **CAP-4 — Workspace and execution** (FR-17 to FR-23)
  - **intent:** Each Task is worked in its own isolated, bounded Workspace that keeps it away from the host, other Projects, and GitHub credentials, and no work is lost on interruption, restart, or cancellation.
  - **success:** A Task cannot read or modify anything outside its Workspace or reach the container runtime; every run has a time limit; after any interruption the Task branch holds all work done; a restart produces no duplicate pull request, comment, or Workspace.

- **CAP-5 — Agents** (FR-24 to FR-30)
  - **intent:** Hephaestus drives Claude Code and Codex CLI through their official non-interactive interfaces as interchangeable workers, moves a Task to the Fallback Agent or waits when one cannot continue, and records who performed each step.
  - **success:** Either Agent implements a Task and the other reviews it with no Project change; a Usage Limit leads to a Handoff built from durable artifacts, or to an automatic resume when capacity returns, with no Owner action; any commit in a pull request is attributable to an Agent, Model, and Effort.

- **CAP-6 — Validation, Review, and Correction** (FR-31 to FR-35)
  - **intent:** Work counts as acceptable only when the Project's Validation commands pass and a different Agent has reviewed it; blocking Findings and failed Validation return to the implementer within a bounded Correction Loop.
  - **success:** A Task with failing Validation is never presented as successful; no Task is reviewed by the Agent that wrote its latest changes; when the bound (two rounds by default) is exhausted, a draft pull request states what was attempted, what still fails, and which Findings remain open.

- **CAP-7 — Pull requests and Stacks** (FR-36 to FR-40)
  - **intent:** Each Task ends in one pull request that says what was done and how it was checked, and a Project can let Tasks build on one another in a Stack so work continues while pull requests await review.
  - **success:** Each pull request names implementer, Reviewer, Validation results, and non-blocking Findings, and says whether it is stacked or independent and why; Stack links always point to actual neighbors; a pull request built on a failed Task stays a draft.

- **CAP-8 — Approval and merge** (FR-41 to FR-44)
  - **intent:** The Owner's approval is the only way into the Base Branch, and Hephaestus merges approved pull requests, in Stack order, at the start of the Project's next window.
  - **success:** A pull request merges only with a Trusted User's approval covering its latest commit and passing repository checks; nothing above an unapproved pull request merges; no label, comment, or issue content causes a merge, a deploy, or a history rewrite on the Base Branch.

- **CAP-9 — Rework** (FR-45 to FR-47)
  - **intent:** A Trusted User starts a revision of an open pull request with an explicit Rework Command, as many times as wanted, and the Stack above follows the change.
  - **success:** Comments without the Rework Command start no work; a reworked pull request shows new Validation results and a new Review before the Owner is asked again; a Stack update that cannot complete cleanly raises an Escalation.

- **CAP-10 — Escalation and Notification** (FR-48 to FR-51)
  - **intent:** When Hephaestus needs a decision it asks a specific question on GitHub, alerts the Owner by email or phone push, and works on other Tasks until a Trusted User answers.
  - **success:** The question names the decision and the options; other ready Tasks continue meanwhile; a Trusted User's reply returns the Task to the Queue with the answer in context; an undeliverable Notification changes no Task's outcome.

- **CAP-11 — Visibility and history** (FR-52 to FR-55)
  - **intent:** The Owner follows a Task on its issue, asks from the command line what is running, queued, and waiting, and can reconstruct afterwards what happened and why.
  - **success:** A comment marks each significant step and says what comes next; history survives restarts and Workspace removal; every metric traces to the Tasks it was computed from, and a metric with no data is shown as none, not zero.

- **CAP-12 — Task authoring aid** (FR-56, FR-57)
  - **intent:** The Owner gets help writing issues that pass the Readiness Check: an issue template and a helper skill for their coding agent.
  - **success:** An issue written with the template contains the sections the Readiness Check looks for; the skill works with both Agents, produces a draft the Owner reviews, and never applies the Marker Label.

- **CAP-13 — Distribution and versioning** (FR-58 to FR-60)
  - **intent:** Anyone can install and run Hephaestus with Docker from published container images, rely on Semantic Versioning of its public surface, and upgrade without losing configuration or history.
  - **success:** A default installation runs on one machine with Docker and no paid or cloud service; every release has a changelog grouped by kind; an upgrade that cannot carry stored data over stops and says why.

- **CAP-14 — Methodology** (FR-61 to FR-66)
  - **intent:** A Project selects a Methodology so that every Agent performs the same step to the same instructions; BMAD Method is supported beside a built-in default, including planning a single Task and creating issues from Stories.
  - **success:** The same instructions reach whichever Agent performs a step, and each step records which were applied; a Project with no Methodology files completes a Task with the default; issues created from Stories link to their Story, carry no Marker Label, and a second run creates none twice.

- **CAP-15 — Dashboard** (FR-67, FR-68)
  - **intent:** The Owner follows Hephaestus in a browser on the machine that runs it: Projects, configuration, Tasks, and Task history from v0.1, and a new view with each release that gives the Owner something new to see. The Dashboard shows and never changes anything.
  - **success:** Nothing the Dashboard does changes a Task, a Project, or configuration; it shows the same records as the command line, no secret value, and GitHub and Agent text as text; a change appears without reloading the page; without a valid Dashboard Token, from another machine, or from a Workspace, nothing is returned.

- **CAP-16 — Language** (FR-69)
  - **intent:** The Owner chooses whether Hephaestus speaks English, the default, or Polish, for everything the Owner reads; a Project can name its own language for what is written on its issues and pull requests.
  - **success:** With Polish chosen, the Dashboard, text on GitHub, Notifications, and the command line's sentences are in Polish, and every command, option, label, and machine-readable output is identical to the English one; no sentence is missing in either language.

## Constraints

- Linux containers under Docker are the only execution platform; the orchestrator and every step run in containers, on one machine, with no additional server (FR-58, NFR-16).
- Agents are driven only through officially documented non-interactive CLI interfaces and authentication. Hephaestus implements no sign-in flow and no circumvention of provider restrictions (FR-24, FR-25).
- No GitHub credential ever enters a Workspace. Every push, pull request, comment, label change, and merge is made by Hephaestus outside it, under its own GitHub App identity — a pull request author cannot approve their own pull request. Installation documentation covers the App setup (FR-18, NFR-2, NFR-6).
- Only Trusted Users' labels, approvals, answers, and commands are acted on, and only content they wrote or vouched for reaches an Agent (FR-6, NFR-1).
- Project Configuration and Methodology files are read only from the Base Branch; a running Task keeps the settings it started with. Nothing in a repository can add a Trusted User, alter a credential, change a Schedule, or raise a limit (FR-2, FR-3).
- Never merge without an explicit Trusted User approval covering the latest commit; never deploy; never rewrite or delete Base Branch history; never update or redeploy itself (FR-44).
- At most one Task executes at a time across the installation, and the Task lifecycle is fixed (FR-14).
- Validation results and the Reviewer's Findings decide acceptability; an Agent's claim never does. The Reviewer is never the author of the latest changes (FR-31, FR-32).
- External actions are idempotent across failure and restart, and every automatic retry, loop, and wait has a bound or an explicit resume condition (NFR-9, NFR-10).
- A failure that cannot be classified is recorded as unknown and never treated as a Usage Limit (FR-27, NFR-11).
- A Handoff is carried by durable artifacts and repository state, never by conversation history (FR-28).
- Credentials never appear in a repository, GitHub text, or stored logs; Agent output is masked before it is stored or posted (FR-4).
- Only free and open-source dependencies; no paid Hephaestus service or mandatory cloud service; SQLite is the only database engine in v1. This governs what an installation needs to run, not the services that build Hephaestus's own repository (NFR-15, FR-58).
- Strict three-part Semantic Versioning over the public surface: configuration format, label and comment-command conventions, command-line interface, image tags. A fourth numeric part is rejected. A merge to `main` publishes the version the repository states; the `develop` image is a preview channel outside the versioned surface (FR-59).
- GitHub is polled; no public webhook endpoint is required.
- The Dashboard is read-only, reachable only on the host's loopback address, and only with a Dashboard Token issued from the command line. Everything that changes state stays on GitHub and the command line. The Dashboard and the HTTP API are outside the versioned public surface (FR-67, FR-68).
- Source code, documentation, and public materials are in English, professional in tone with restrained references to the forge and the mythological namesake. Hephaestus is never described as secure beyond what has been tested (NFR-17, NFR-8).
- What a program reads is never translated: command names and options, machine-readable output, labels and state names, comment commands, dependency declarations, configuration keys, and Glossary terms are the same in every language (FR-69).
- A change reaches `develop` or `main` only through a pull request that passes the build, the tests, static analysis, and 80% test coverage over all code and over new code, including changes Hephaestus makes to itself (NFR-18).
- The architecture spine (AD-1 to AD-21, consistency conventions, stack) binds how every capability is built.
- `DESIGN.md` binds how the Dashboard looks; `EXPERIENCE.md` binds how the Dashboard behaves, the command grammar and output conventions, and the shape of text Hephaestus writes on GitHub.

## Non-goals

- Not a coding agent, chatbot, IDE, or model provider; it requires Agents the user already has.
- Not a hosted service: no Hephaestus cloud, account, or subscription, and no feature depends on one.
- Not a way around provider rules: no unofficial authentication, credential extraction, or circumvention of usage restrictions.
- Not an autonomous releaser: no merge without approval, no deployment, no self-update.
- Not a manager of an Agent's internals: sub-agents an Agent starts are that Agent's business.
- Not a team product in v1: no multiple user accounts, roles, or permissions.
- Not a general-purpose workflow engine in v1: no user-defined workflows.
- Not a methodology of its own: it ships only a minimal default; BMAD Method is supported, never required; no other Methodologies in v1.
- Not a product planning tool: briefs, product requirements, architecture, and the backlog stay with the Owner, including product-level planning with BMAD Method. Hephaestus plans only how to carry out a single Task.
- Not a clone of existing orchestrators; matching their feature sets is not a goal.
- Not a monetized product: no billing, licensing tiers, or marketplace.
- Out of scope for v0.1 to v0.3: actions from the Dashboard; Dashboard access from another machine or by more than one user; languages other than English and Polish; Agents other than Claude Code and Codex CLI; Task sources other than GitHub issues; Windows execution environments and Windows-only projects; parallel execution of Tasks; distributed workers; other database engines or an existing database server; random Reviewer selection among three or more Agents.

## Success signal

The Owner labels well-described issues in a registered Project and, with no further action, finds for each one a pull request that passed the Project's Validation and a Review by the other Agent — or a draft pull request or an Escalation that says exactly why not. Nothing reached the Base Branch without the Owner's approving review, and Hephaestus goes on to produce its own pull requests. The Owner sets no numeric targets; the measures they judge by are in `success-metrics.md`.

## Assumptions

- Invoked with no input, this spec took the only PRD on disk (with its addendum) and the architecture spine as its sources, as the architecture run recommended.
- Where the spine refines a PRD requirement, the refinement is carried as a note on that requirement in `requirements.md` and the spine governs: FR-11 (additional Task states), FR-25 (Codex CLI with an API key only), FR-35 (a failed Task with no commit gets an issue comment, not a draft pull request), FR-65 (the plan is rendered into the pull request description, not committed), FR-67 (changes arrive as a stream of ids; step output is not shown while a step runs), FR-68 (the Dashboard's listener maps reads only; the token travels in a request header).
- Before v0.2 the notifier is a no-op, so the "Owner is notified" consequences of FR-35 and FR-48 (both v0.1) are met through issue comments (FR-52) until FR-50 ships.
- `docs/architecture.md`, `docs/adr/`, and `docs/architecture-c4.md` are rendered from the spine and add no contract, so they are not companions. The research report is reference only; where it and the spine differ, the spine wins.

## Open Questions

- **BMAD spike (before the Methodology epic; v0.1 depends on it).** Which BMAD workflows complete unattended under `claude -p` and `codex exec` with their files at `/hephaestus/methodology`, what do they need in the image, which workflow serves each step kind, and how does an issue map to the artifacts BMAD works from?
- **Unsatisfiable dependencies (v0.1 intake epic).** What happens to a Task whose declared dependency fails, is closed without a merge, or is not a Task at all? FR-40 answers this only for Stacks. Also open: what opting in again means for a Task cancelled because someone who is not a Trusted User merged its pull request.
- **GitHub edit history (to confirm by test).** Does GraphQL `userContentEdits` give a GitHub App's token the editor of an issue body and of a comment? The fail-closed behavior when it does not is settled (NFR-1).
- **Usage Limit signals (before v0.2 design of FR-27 to FR-29).** What do Claude Code and Codex CLI emit when a real subscription Usage Limit is reached?
- **Codex subscription sign-in.** Unsupported: Codex CLI is supported with an API key only. Its subscription credential is a full account credential that must be kept and refreshed between runs, and the tools the account brings cannot be listed from the CLI. The per-run reasoning-effort override is confirmed. Open only as a condition for ever supporting it: a safe refresh path, and Models that differ by authentication mode.
- **Independence detection (v0.3).** How is "this Task does not depend on pending work" determined when the issue declares no dependency?
- **Design assumptions awaiting the Owner's review (before the first story that adds a command).** `DESIGN.md` and `EXPERIENCE.md` are drafts. The Owner chose the themes, the ember accent, Angular Material, and the noun-then-verb command grammar; the passages marked `[ASSUMPTION]` (colour values, the surface list, the command tree, comment templates, key flows) stand until the Owner changes them. Also open: whether a pull request names its Task's issue with a closing keyword.
- **Story identity (Methodology epic).** How is a Story matched to its issue across runs, and what happens when a Story changes after its issue exists?
