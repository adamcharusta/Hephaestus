---
status: final
stepsCompleted: ['step-01-validate-prerequisites', 'step-02-design-epics', 'step-03-create-stories', 'step-04-final-validation']
inputDocuments:
  - _bmad-output/planning-artifacts/prds/prd-Hephaestus-2026-11-25/prd.md
  - _bmad-output/planning-artifacts/prds/prd-Hephaestus-2026-11-25/addendum.md
  - _bmad-output/planning-artifacts/architecture/architecture-Hephaestus-2026-11-25/ARCHITECTURE-SPINE.md
  - _bmad-output/specs/spec-Hephaestus/SPEC.md
  - _bmad-output/specs/spec-Hephaestus/requirements.md
  - _bmad-output/specs/spec-Hephaestus/glossary.md
  - _bmad-output/specs/spec-Hephaestus/user-journeys.md
  - _bmad-output/specs/spec-Hephaestus/release-scope.md
  - _bmad-output/specs/spec-Hephaestus/success-metrics.md
  - docs/architecture.md
  - docs/architecture-c4.md
  - docs/adr/
  - docs/security/threat-model.md
  - _bmad-output/planning-artifacts/research/technical-agent-clis-and-github-integration-for-he-2026-11-25/research.md
  - _bmad-output/planning-artifacts/sprint-change-proposal-2026-11-25.md
  - _bmad-output/planning-artifacts/sprint-change-proposal-2026-11-25-language-and-ci.md
  - _bmad-output/planning-artifacts/sprint-change-proposal-2026-11-26.md
  - docs/spikes/injection/README.md
  - _bmad-output/planning-artifacts/ux-designs/ux-Hephaestus-2026-11-25/DESIGN.md
  - _bmad-output/planning-artifacts/ux-designs/ux-Hephaestus-2026-11-25/EXPERIENCE.md
---

# Hephaestus - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for Hephaestus, decomposing the requirements from the PRD, UX Design if it exists, and Architecture requirements into implementable stories.

**Reading notes**

- Requirement ids are the PRD's own (`FR-1` to `FR-69`, `NFR-1` to `NFR-18`). The architecture spine binds to them, so they are kept unchanged.
- Each FR carries its release in brackets, taken from PRD §7 and `release-scope.md`.
- Where the architecture spine refines a PRD requirement, the refinement is folded into the requirement text and marked *(spine)*. The spine governs where the two differ.
- Capitalized terms are defined in the PRD Glossary (§3) and `glossary.md`.
- Additional requirements carry ids `AR-n` so that stories can cite them. UX design requirements carry ids `UX-DR-n`.
- The Dashboard was added on 2026-11-25 by an approved change (`sprint-change-proposal-2026-11-25.md`): FR-67, FR-68, AR-72 to AR-74, the UX design requirements, and ten stories, one at the end of each epic that gives the Owner something new to see. No existing story was renumbered.
- A second approved change of the same day (`sprint-change-proposal-2026-11-25-language-and-ci.md`) added language (FR-69, AR-75, UX-DR-20) and the rules for Hephaestus's own repository (NFR-18, AR-76, AR-77): one new story, 1.16, and criteria added to Stories 1.2, 1.3, 1.4, 1.9, 1.11, 1.13, 1.14, 2.5, 3.11, and 8.2.
- A third approved change (`sprint-change-proposal-2026-11-26.md`) absorbed what the injection spike, Story 1.1, found (`docs/spikes/injection/README.md`): AD-7 and AD-8 are confirmed and the fallback of a Hephaestus base image is dropped. No story was added, removed, or moved. Thirteen were amended: 1.2, 3.1, 3.2, 3.4, 3.5, 3.8, 3.9, 3.10, 3.11, 3.16, 3.21, 7.1, and 9.4.

## Requirements Inventory

### Functional Requirements

**Project setup and configuration (CAP-1)**

FR-1 [v0.1]: The Owner can register a GitHub repository as a Project. Issues in repositories that are not registered are never acted on, whatever labels they carry. Removing a Project stops new Tasks from starting there and retains the history of past Tasks. *(spine)* The Base Branch is set at registration, and registration reports whether the Base Branch is protected against direct pushes.

FR-2 [v0.1]: Each Project defines its Project Configuration in a version-controlled file in its repository (`hephaestus.yml` at the repository root). It is read only from the Base Branch; a change on a Task's branch has no effect until merged. A Task already running keeps the settings it started with. When the file is missing or invalid, no Task starts in that Project and the Owner is told which setting is wrong and why.

FR-3 [v0.1]: The Owner can view and change the Installation Configuration from the command line. No change to any repository can add a Trusted User, alter a credential, change a Schedule, or raise a limit. Installation Configuration survives restarts and upgrades.

FR-4 [v0.1]: Credentials are never written to a repository, an issue or pull request comment, a pull request description, or stored logs. A search of those places for any configured credential value finds nothing. Agent output that contains a credential value is masked before it is stored or posted.

FR-5 [v0.1]: A Project in any language or framework that can be built and tested in a Linux container can be added through configuration alone, with no change to Hephaestus's code. The environment a Project's Tasks run in is chosen per Project (image and platform). Windows-only execution environments are out of scope.

**Task intake (CAP-2)**

FR-6 [v0.1]: An issue becomes a Task only when the Marker Label was applied by a Trusted User. The Marker Label applied by anyone else creates no Task, and the attempt is recorded. Removing the Marker Label before work starts withdraws the Task.

FR-7 [v0.1]: A Trusted User can choose a Task's Primary Agent and Fallback Agent, with their Models, through Role Labels (`primary:<alias>`, `fallback:<alias>`), and the Effort the same way (`effort:<alias>`). A Task with only the Marker Label runs with the Project's defaults. A new Model becomes selectable by adding an Alias to configuration. A Role Label naming an unknown Alias, or two Role Labels for the same role, stops the Task from starting and is reported on the issue. *(spine)* `primary` and `fallback` must resolve to different Agents or the Task does not start. In v0.1 the Task's second Agent serves as the Reviewer, because Handoff arrives in v0.2.

FR-8 [v0.1]: Tasks carrying a Priority Label are worked first; otherwise the lower issue number comes first. A Task whose declared dependency is not yet satisfied is passed over, never started out of order.

FR-9 [v0.1]: Before implementation, Hephaestus assesses whether the issue is clear enough to implement (Readiness Check). When it is, implementation starts without waiting for the Owner. When it is not, Hephaestus raises an Escalation with specific questions and takes the next Task.

FR-10 [v0.1, relaxed in v0.3]: An issue can declare that its Task depends on another Task, with a line `Depends-On: #<issue-number>` in the issue body, one per dependency, naming an issue of the same Project. A Task is not started before every Task it depends on is satisfied. Until v0.3 a dependency is satisfied only when its pull request has been merged into the Base Branch by Hephaestus after an approval, or by a Trusted User; from v0.3, when it has produced a branch to build on, unless the Project's stacking mode is off (FR-37). A declared dependency overrides any automatic judgement about independence.

FR-11 [v0.1]: The current state of a Task is visible on its issue without opening Hephaestus, as a `hephaestus:<state>` label. The state shown changes within one polling cycle of the event that caused it, and the Owner can list all Tasks in a given state with GitHub's own issue filters. *(spine)* The full state set is queued, running, waiting-for-owner, waiting-for-capacity, pull-request-open, failed, cancelled, withdrawn, merged, closed. State labels are a one-way projection of the database and are re-asserted when changed by hand.

**Scheduling (CAP-3)**

FR-12 [v0.2]: The Owner can set, per Project, the days of the week and the start time at which Hephaestus works on it, with an optional end time. Outside a Project's Schedule no new Task from that Project starts. Without an end time, work continues until the Queue is empty, every remaining Task is waiting, or no Agent has capacity.

FR-13 [v0.2]: A Task running when the end time passes is finished; no new Task starts after it.

FR-14 [v0.1]: At most one Task executes at any moment across the whole installation. With Tasks ready in two Projects whose windows overlap, one runs and the other starts only after it ends.

FR-15 [v0.2]: A Task that was interrupted is resumed and finished before a new Task is started, including when another Project's Schedule has begun in the meantime.

FR-16 [v0.1]: The Owner can start work on a Project outside its Schedule. A start on demand works the Project's Queue under the same rules as a scheduled window and ends when the Queue is empty or the Owner stops it.

**Workspace and execution (CAP-4)**

FR-17 [v0.1]: Each Task runs in a dedicated Workspace created from a clean state of its starting branch. A Task cannot read or modify files outside its Workspace, including other Projects and the host's personal files, and has no access to the container runtime's control interface.

FR-18 [v0.1]: A Workspace receives only the credentials its Agent needs to run, and never a GitHub credential. Every push, pull request, comment, label change, and merge is performed by Hephaestus outside the Workspace.

FR-19 [v0.1]: Every Agent run and every Validation command has a time limit. A run that exceeds its limit is stopped and recorded as a failure with its output preserved.

FR-20 [v0.1]: When a Task is interrupted by a Usage Limit, a timeout, a crash, or a restart, the work done so far is kept and remains available for continuation and inspection. After any such interruption the Task's branch holds every change made before it, and a continuing Agent starts from that state, not from the starting branch.

FR-21 [v0.1]: After Hephaestus restarts, every Task continues from its recorded state. A restart never produces a duplicate pull request, a duplicate comment, or a second Workspace for the same Task.

FR-22 [v0.1]: A Workspace is removed only after its Task has ended and its work is safely in the repository. A Workspace holding work that is not yet in the repository is never removed automatically. Removing a Workspace affects nothing outside it.

FR-23 [v0.2]: The Owner can cancel a Task that is queued or in progress. A cancelled Task stops within a bounded time, its work so far is preserved, and its issue shows it as cancelled. It does not start again unless a Trusted User opts it in again.

**Agents (CAP-5)**

FR-24 [v0.1]: Hephaestus supports Claude Code and Codex CLI, using only officially documented execution and authentication mechanisms (`claude -p --output-format stream-json`, `codex exec --json`). A Task can be implemented by either Agent and reviewed by the other with no change to the Project. A capability that one Agent lacks is reported as unavailable for that Agent; it is not imitated silently.

FR-25 [v0.1]: For each Agent, the Owner chooses whether it authenticates with an API key or with a subscription login, within what the provider officially permits. Sign-in always completes through the Agent's own tool; Hephaestus never presents a provider's sign-in itself and only uses the credential the Owner supplies. Installation documentation presents both modes on an equal footing and states, for each, the provider terms that apply and the risk of using a subscription for automated work. *(spine)* Claude Code is supported with an API key and with a subscription sign-in, the latter through a token from the vendor's `claude setup-token`. Codex CLI is supported with an API key only: its subscription credential is a full account credential that must be kept and refreshed between runs, and the tools the account brings cannot be listed from the CLI.

FR-26 [v0.1]: Before using an Agent, Hephaestus verifies that it is installed and authenticated. An unavailable Agent is reported to the Owner with the reason and is treated as having no capacity.

FR-27 [v0.2]: Hephaestus recognizes when an Agent stopped because of a Usage Limit and distinguishes this from other failures. A failure that cannot be classified is recorded as unknown and is never treated as a Usage Limit. Where the Agent reports when the limit resets, Hephaestus records that time and uses it to decide when to resume.

FR-28 [v0.2]: When the working Agent cannot continue, Hephaestus hands the Task to the Fallback Agent with the requirements, acceptance criteria, plan, current changes, completed and remaining work, and Validation and Review results so far. The receiving Agent inspects and validates the partial work before continuing. A Handoff does not depend on transferring conversation history between providers. Each Handoff is recorded with its cause.

FR-29 [v0.2]: When no Agent has capacity, the Task waits and resumes automatically once capacity returns, with no action from the Owner. The waiting Task's issue shows that it is waiting for capacity and, where known, until when. Work resumes within one polling cycle after capacity returns. *(spine)* A Task waiting for capacity does not hold the single slot and does not block other Tasks.

FR-30 [v0.1]: For every step of a Task, Hephaestus records which Agent, Model, and Effort performed it, and usage information where the Agent provides it. From a Task's history the Owner can tell, for any commit in its pull request, which Agent and Model produced it.

**Validation, Review, and Correction (CAP-6)**

FR-31 [v0.1]: The Project's Validation commands are run on the Task's changes, and their results determine whether the work is acceptable. A Task whose Validation fails is never presented as successful, whatever the Agent reported.

FR-32 [v0.1]: Every Task's changes are reviewed by an Agent other than the one that wrote the latest changes. With two Agents, authorship and Review alternate between them. When the only other Agent has no capacity, the Task waits; it is not reviewed by its own author.

FR-33 [v0.1]: Each Finding is classified as blocking or non-blocking. Non-blocking Findings are reported in the pull request description and do not hold the Task.

FR-34 [v0.1]: Blocking Findings and failed Validation are returned to the implementing Agent for correction, for at most a configured number of rounds, two by default. When the bound is reached with blocking Findings or failing Validation remaining, the loop stops and FR-35 applies. Each round is recorded with what was returned to the Agent and what changed.

FR-35 [v0.1]: When the Correction Loop ends without passing Validation and Review, Hephaestus opens a draft pull request describing what was attempted, what still fails, and which Findings remain open. The draft is linked to its issue, the Task's state shows as failed, and the Owner is notified. *(spine)* A Task that fails with no commit ahead of its start commit gets an issue comment and no pull request. Before v0.2 "notified" is met through issue comments.

**Pull requests and Stacks (CAP-7)**

FR-36 [v0.1]: Hephaestus opens one pull request per Task, linked to its issue. The description states what changed, which Agent implemented and which reviewed, the Validation results, and any non-blocking Findings.

FR-37 [v0.3]: Each Project chooses one of three stacking modes: Tasks stack in Queue order, Tasks stack only on declared dependencies, or Tasks do not stack at all. Changing the mode affects Tasks started afterwards and leaves existing Stacks as they are. A Project that sets no mode stacks only on declared dependencies. With stacking off, a dependent Task waits until the Task it depends on is merged, as in v0.1 and v0.2.

FR-38 [v0.3]: In Queue-order mode, a Task that does not depend on pending work gets its own branch from the Base Branch and a pull request against it. The pull request states whether the Task was stacked or independent, and why.

FR-39 [v0.3]: Every pull request in a Stack links to the previous and the next pull request in that Stack. After a pull request joins, is merged from, or leaves a Stack, every remaining pull request's links point to its actual neighbors.

FR-40 [v0.3]: When a Task in a Stack fails, Hephaestus decides for each dependent Task whether it can still be done. A dependent Task that can proceed by standing in for the missing piece does so, and its pull request states what was stood in for. A pull request built on a failed Task stays a draft until the Task below it is fixed. Otherwise work on that Stack stops and the Owner is notified; unrelated Tasks continue.

**Approval and merge (CAP-8)**

FR-41 [v0.3]: At the start of a Project's scheduled window, Hephaestus merges each pull request that a Trusted User approved. A pull request is merged only if the approval covers its latest commit and the repository's checks pass. A pull request that fails any condition is left open and the Owner is told which condition failed.

FR-42 [v0.3]: Pull requests in a Stack are merged in Stack order, and nothing above an unapproved pull request is merged; the Owner is told which pull request awaits approval.

FR-43 [v0.3]: After a pull request in a Stack is merged, the remaining pull requests show only their own changes and their issue links and navigation remain correct. After the first pull request of a Stack is merged, the second targets the Base Branch and its diff contains only its own Task's changes.

FR-44 [v0.3 as a feature; the prohibitions hold from v0.1]: Hephaestus never merges without an explicit approval from a Trusted User, never deploys, and never rewrites or deletes history on the Base Branch. No combination of labels, comments, or issue content causes a merge without an approving review from a Trusted User. Hephaestus's GitHub identity is never used to force-push to or delete the Base Branch.

**Rework (CAP-9)**

FR-45 [v0.3]: A Trusted User can start a Rework of an open pull request by issuing the Rework Command: a comment whose first line is `/hephaestus rework`. Comments without the command start no work. A command from anyone who is not a Trusted User is ignored.

FR-46 [v0.3]: Reworked changes pass Validation and independent Review before the pull request is presented again. There is no limit on the number of Reworks. A reworked pull request shows new Validation results and a new Review of the reworked changes before the Owner is asked to look again.

FR-47 [v0.3]: After a Rework, every pull request above it in the Stack is updated to include the change. When an update cannot be completed cleanly, Hephaestus raises an Escalation instead of guessing.

**Escalation and Notification (CAP-10)**

FR-48 [v0.1]: When Hephaestus cannot proceed with a Task, it posts a specific question as a comment on the issue or pull request, marks the Task as waiting for the Owner, and takes the next Task. The question names the decision needed and the options Hephaestus sees, and the Owner is notified. While one Task waits, other ready Tasks continue to be worked.

FR-49 [v0.1]: A reply from a Trusted User returns the Task to the Queue with the answer as part of its context. A reply from anyone who is not a Trusted User does not resume the Task.

FR-50 [v0.2]: The Owner can be notified by email, by phone push, or both. A Notification is sent when an Escalation is raised and when a Task fails. A summary Notification is sent when a scheduled window ends: pull requests opened, Tasks failed, Escalations waiting. A Notification links to the place where the Owner acts.

FR-51 [v0.2]: When a Notification cannot be delivered, the failure is recorded and work continues. A Task's outcome is the same whether or not its Notifications were delivered.

**Visibility and history (CAP-11)**

FR-52 [v0.1]: Hephaestus reports a Task's progress as comments on its issue at each significant step: when work starts, when Validation and Review complete, on each Handoff and Escalation, and when the pull request opens or the Task fails. Each comment says what happened and what comes next.

FR-53 [v0.1]: For every Task, Hephaestus retains each step, its Agent, timing, Validation results, Findings, Handoffs, Escalations, and the Agents' output. The history survives restarts and remains available after the Workspace is removed.

FR-54 [v0.2]: The Owner can ask Hephaestus, from the command line, what it is doing now, what is queued, and what is waiting. The answer names the running Task and its current step, each waiting Task with what it waits for, and the order of queued Tasks.

FR-55 [v0.2]: Hephaestus derives the success metrics (SM-1 to SM-7, SM-C1, SM-C2) from actual executions only and shows them to the Owner from the command line and on the Dashboard's statistics page (FR-67). Each figure can be traced to the Tasks it was computed from. A metric with no data is shown as having none, not as zero.

**Task authoring aid (CAP-12)**

FR-56 [v0.3]: A Project can adopt an issue template that prompts for the business requirements and acceptance criteria a Task needs. An issue written with the template contains the sections the Readiness Check looks for.

FR-57 [v0.3]: The Owner can use a helper skill with their coding agent to turn a rough idea into a well-formed issue. The skill works with both supported Agents. Its result is an issue draft the Owner reviews before it is created; the skill never applies the Marker Label.

**Distribution and versioning (CAP-13)**

FR-58 [v0.1]: Hephaestus is distributed as container images and runs with Docker on Linux containers, on a developer machine or a server. Running it requires no paid Hephaestus service and no mandatory cloud service. Images are built for linux/amd64 and linux/arm64; a Windows machine is only the Docker host.

FR-59 [v0.1]: Hephaestus releases follow Semantic Versioning (strict MAJOR.MINOR.PATCH). The versioned public surface is the configuration format, the label and comment-command conventions, the command-line interface, and the container image tags. Steps before a release are published as pre-release versions (`0.3.0-alpha.2`, `0.3.0-rc.1`). Every release has a changelog entry grouped by kind: added, changed, fixed, removed, security. While the version is 0.x, a minor release may contain breaking changes, and its changelog says so. A merge to `main` publishes the version the repository states; a pull request to `main` cannot merge with a version that is not valid, not higher than the last one, or missing from the changelog. A merge to `develop` publishes a moving `develop` image, a preview channel that is not a release and not part of the versioned public surface.

FR-60 [v0.1]: Upgrading Hephaestus preserves stored configuration and execution history. When stored data cannot be carried over to the new version, the upgrade stops and says why; data is never half-converted.

**Methodology (CAP-14)**

FR-61 [v0.1]: Each Project selects its Methodology in its Project Configuration. A Project that selects none uses the built-in default Methodology. Selecting a Methodology that is unknown or not available in the Project stops Tasks from starting and is reported to the Owner.

FR-62 [v0.1]: For a given step in a given Project, every Agent receives the same Methodology instructions. After a Handoff, the receiving Agent continues under the same Methodology and from the same shared artifacts. The Reviewer assesses changes against the same Methodology the implementer worked to. Each step's record states which Methodology and which of its instructions were applied.

FR-63 [v0.1]: A Project that uses BMAD Method can have its Tasks planned, implemented, and reviewed with BMAD's workflows. The same BMAD workflow is used for a step whether Claude Code or Codex CLI performs it. Only workflows that can run without human input are used; where one needs a decision, Hephaestus raises an Escalation. The execution image of a BMAD Project must provide `uv` and Python. Depends on the BMAD spike.

FR-64 [v0.1]: Hephaestus works on a Project that has no Methodology installed, and adding support for another Methodology requires no change to how Tasks are scheduled, validated, or approved. A Project with no Methodology files in its repository completes a Task from issue to pull request using the built-in default (plain role prompts per step).

FR-65 [v0.1]: Before implementing a Task, Hephaestus can have an Agent plan that one Task with the Methodology's planning workflow. The Readiness Check decides: a Task that is clear but large is planned first; a small one goes straight to implementation. The plan is part of the Task's pull request. Planning never reaches beyond the Task. A planning question only the Owner can answer becomes an Escalation. *(spine)* The plan is stored in the database and rendered into the pull request description; it is not committed.

FR-66 [v0.1]: The Owner can have Hephaestus create GitHub issues from the Stories in a Project's Methodology documents. Each created issue links to its Story and carries the Story's requirements and acceptance criteria. Order and dependencies between Stories become declared dependencies between the issues. Stories are read from the Base Branch only. A created issue does not carry the Marker Label. Running it again creates issues only for Stories that have none. *(spine)* Issue creation is a Project-scoped journaled effect; a hidden marker carries a stable Story identity declared by the Methodology manifest; nothing is written back to the Base Branch.

**Dashboard (CAP-15)**

FR-67 [v0.1; grows with each release]: The Owner can open a Dashboard in a browser and follow Hephaestus there. In v0.1 it shows the registered Projects, the Installation Configuration without any secret value, each Project's Tasks with their current state, and each Task's history. Each release adds views for what that release introduces. No view and no request the Dashboard makes changes a Task, a Project, or any configuration. Everything shown is read from the same records the command line reads. No secret value is shown, and Agent output is shown as stored, that is, masked. Text that came from GitHub or from an Agent is shown as text and is never interpreted as markup or script. A value that does not exist is shown as none, not as zero. A change to what is on screen appears without the Owner reloading the page. The Dashboard works with no connection to any host other than Hephaestus itself, and it is not part of the versioned public surface. *(spine)* Changes arrive as one stream of Server-Sent Events that names what changed by scope and id; output of a step is not shown while the step runs.

FR-68 [v0.1]: The Dashboard can be reached only from the machine that runs Hephaestus, and only with a Dashboard Token. The Owner issues, lists, and revokes tokens from the command line. A request without a valid token returns no data. A revoked token stops working at once. The Dashboard is not reachable from another machine or from a Workspace. The token value is shown once, when issued, and is not stored in readable form. Access from other machines, encryption in transit, and more than one user are out of scope. *(spine)* The listener the Dashboard uses maps reads only; the token travels in a request header, never in a cookie or an address; revoking a token closes the streams opened with it.

**Language (CAP-16)**

FR-69 [v0.1]: Hephaestus speaks English by default, and the Owner can choose Polish. The Owner sets the installation's language from the command line; the Dashboard, the command line's sentences and table headers, Notifications, and the text Hephaestus writes on GitHub follow it. A Project can name its own language for the text Hephaestus writes on its issues and pull requests. The Dashboard's language can also be switched in the browser. Text an Agent writes for the Owner to read is written in the Task's language. Nothing a program reads changes with the language: command names and options, machine-readable output, labels, comment commands, dependency declarations, and configuration keys are the same in every language. No sentence is missing in a supported language: a build that lacks a translation fails. Languages other than English and Polish, and translating the Glossary's terms, are out of scope.

### NonFunctional Requirements

**Security**

NFR-1: Trusted input only. An Agent receives the issue body of its Task, which the Trusted User who applied the Marker Label vouches for, and comments written by Trusted Users. Nothing written by anyone else reaches an Agent. An issue body edited by someone who is not a Trusted User after the Marker Label was applied is not used until a Trusted User confirms it with `/hephaestus confirm`. An edit is detected by comparing a hash of the content with the hash recorded at admission; an edit whose editor cannot be established counts as untrusted.

NFR-2: Agents cannot act on GitHub. No Agent holds a GitHub credential.

NFR-3: Containment. A Task cannot reach the host's files, other Projects, or the container runtime's control interface. Hephaestus does not weaken an Agent's own safety mechanisms outside a Workspace.

NFR-4: Network egress. Outbound network access from a Workspace can be restricted per Project to an allowlist. In v0.1 access is open to any public host unless a list is set; the allowlist becomes the default before Hephaestus is announced for general use. The workspace network is a Docker internal network whose only exit is a separate, unprivileged egress proxy that refuses destinations resolving to loopback, link-local, or private addresses and applies the allowlist by host name, per step. A tool that cannot use an HTTP proxy has no network access from a Workspace.

NFR-5: Resource and spend limits. Each Project has limits on Task duration, processor, and memory (from v0.1). When an Agent runs on an API key, a spend limit stops new work and notifies the Owner once reached (accounting decided in v0.2). Where an Agent reports no usage data, the spend limit cannot be enforced and Hephaestus says so.

NFR-6: Least privilege on GitHub. Hephaestus's own GitHub identity has only the permissions its features need, and only on registered Projects.

NFR-7: Audit trail. Every action Hephaestus takes that is visible outside it (push, pull request, comment, label change, merge) is recorded with its cause.

NFR-8: Honest security claims. Documentation states the security assumptions and known limitations. Hephaestus is not described as secure beyond what has been tested.

**Reliability**

NFR-9: Idempotent external actions. Repeating a step after a failure or restart never duplicates its outside effects.

NFR-10: Everything is bounded. Every retry, loop, and wait that Hephaestus performs on its own has a limit or an explicit resume condition. No automatic cycle can run indefinitely.

NFR-11: Classified failures. Usage Limit, timeout, Agent error, Validation failure, infrastructure error, and unknown are distinguished in history and in what the Owner is told. *(spine)* `cancelled` is a seventh class.

NFR-12: Outage tolerance. When GitHub or an Agent's provider is unreachable, Hephaestus retries later without losing state.

**Data and observability**

NFR-13: Retention. Task history, summaries, and metrics are kept indefinitely. Raw Agent output is kept for a configurable period, 30 days by default, with an option to keep it permanently.

NFR-14: Observable operation. Hephaestus's own operation can be followed through structured logs.

**Openness and footprint**

NFR-15: Free and open-source components. Hephaestus depends only on free and open-source libraries and tools. Agents and their costs are the Owner's own. This governs what an installation needs in order to run; services used only to build and check Hephaestus's own repository are not dependencies of an installation.

NFR-16: Light footprint. A default installation runs on one machine with Docker and requires no additional server.

NFR-17: English. Source code, documentation, and public materials are written in English. What Hephaestus says to the Owner while running follows the language the Owner chose (FR-69).

**Engineering quality**

NFR-18: Quality gate on Hephaestus's own code. A change reaches `develop` or `main` only through a pull request that passes the build, every test, static analysis, and test coverage of at least 80%, measured both over all code and over the code the change adds. This holds for changes Hephaestus makes to itself.

**Identity and tone (PRD §5.5, unnumbered)**

Hephaestus presents itself professionally, with restrained references to craftsmanship, the forge, and its mythological namesake, for example in release names such as "Ignite the Forge". The references never get in the way of clarity. This applies to CLI output, issue comments, pull request text, and documentation.

### Additional Requirements

**Project foundation and starter**

- AR-1: **No starter template.** The project is greenfield and the architecture names no starter. The solution is scaffolded by hand to the spine's Structural Seed: `src/` with `Hephaestus.Core`, `Hephaestus.Agents`, `Hephaestus.GitHub`, `Hephaestus.Workspaces`, `Hephaestus.Persistence`, `Hephaestus.Host`, and the Angular workspace `Hephaestus.Dashboard`, which Story 1.14 adds; `methodologies/default` and `methodologies/bmad`; `tests/` with one test project per source project plus end-to-end; `deploy/` with `compose.yaml`, `Dockerfile`, `tools.Dockerfile`, `agents.lock`; `docs/`. This shapes Epic 1 Story 1 together with AR-4.
- AR-2: Build conventions: nullable reference types on, warnings as errors, central package management in `Directory.Packages.props`. GitHub Actions builds and tests every pull request; branches, checks, and publication follow AR-76 and AR-77.
- AR-3: Pinned stack: .NET 10 LTS and ASP.NET Core 10.0 (runtime 10.0.15), C# 14, EF Core 10.0.15 with the SQLite provider, Octokit 14.0.0, Docker.DotNet.Enhanced 4.4.1, System.CommandLine 2.0.15, YamlDotNet 18.1.0, xunit.v3 4.0.3, NSubstitute 6.2.0, Testcontainers 4.17.0, Git 2.48 or later in the orchestrator and tools images, Docker Engine 29.0 or later with Compose v2. For the Dashboard: Angular 22.3.0 with the Angular CLI and Angular Material, TypeScript 6.0.3 (Angular 22.3 accepts 6.0.x only), Vitest 5.1.2, and Node.js 26.13.1 LTS in the build stage only. Deliberately absent: a message broker, a second database, a workflow engine, .NET Aspire, Kubernetes, a second web server, anything loaded from a content delivery network, SignalR or any other two-way channel to the browser.

**Spikes and ordering**

- AR-4: **Injection spike, the first story of v0.1** (conditions AD-7 and AD-8). It must show: both pinned CLIs run from a read-only mount as user `10001:10001` with no passwd entry, a throwaway `HOME`, and all capabilities dropped, on Debian-, Ubuntu-, and Alpine-based images; the right Claude Code build is selected for the image's C library; account-linked tools and repository-supplied executable configuration can be disabled in each authentication mode; both CLIs and the common package managers (npm, `dotnet restore`, pip, cargo, go, Maven, Gradle) work through `HTTPS_PROXY`, with which settings in the Project image; the vendors' terms allow fetching the binaries this way. Fallback if injection fails: the same rule with a Hephaestus base image that Project images derive from. Done, 2026-11-26: both decisions confirmed; the fallback is not needed.
- AR-5: **BMAD spike, before the Methodology epic; v0.1 depends on it.** Which BMAD workflows complete unattended under `claude -p` and `codex exec` with their files at `/hephaestus/methodology`, what they need in the image, which workflow serves each step kind, how an issue maps to the artifacts BMAD works from, and how a workflow is started when the Agent has no `Skill` tool and is given no repository instruction file.
- AR-6: **Usage Limit spike, before FR-27 to FR-29 are designed in v0.2.** What Claude Code and Codex CLI emit in their event streams when a real subscription Usage Limit is reached: exit code, events, reset-time fields.
- AR-7: **GitHub edit-history test.** Confirm whether GraphQL `userContentEdits` returns the editor of an issue body and of a comment to a GitHub App installation token. If it does, the gateway gains one GraphQL query over plain HTTP; no library is added. The fail-closed behavior is already fixed (NFR-1).
- AR-8: **Codex subscription sign-in.** Unsupported. The credential file is a full account credential that must be kept and refreshed between runs, which AD-16 forbids taking from a step container, and the tools the account brings cannot be listed from the CLI. Should it ever be supported, the Models Codex accepts differ by authentication mode, so an Alias could not resolve to one Model for both. The per-run reasoning-effort override is confirmed: `model_reasoning_effort`.
- AR-9: Carve a proof-of-concept milestone inside v0.1 when epics are written (`release-scope.md`, Ordering).

**Binding architecture decisions (spine AD-1 to AD-20)**

- AR-10 (AD-1): Single-writer orchestrator. Task and step-run records are written only by the orchestrator loop through `IUnitOfWork`, which nothing else may depend on (asserted by a test). Changes arrive only as Signals appended to a persisted inbox by the poller, the local API, and the runtime. One transaction per Signal covers marking it applied, every Task and step-run write, and the intents of the effects it causes; effects run after the commit. Inbox order is a database-assigned sequence; a duplicate append (same source and source event id) is a no-op; the poller appends first and advances its cursor second. `Running` is entered only from `Queued`. Dispatch order: interrupted Tasks first, ignoring windows; then, inside an open window, Priority Label; then issue number. Dispatch of a Task with no step run requires an open window record for its Project. A step interrupted by a restart is never resumed in place: its container is removed, a checkpoint is taken, and it is attempted again as a new step run. Apart from the poller, no module acts on a timer; startup recovery is the loop's first tick and output retention is a loop phase.
- AR-11 (AD-2): Source of truth split. GitHub is authoritative for human intent, read only as actor-attributed events (labelled, unlabelled, closed, reopened, review submitted or dismissed, comment created or edited), each one Signal; intent is never inferred from the current label set or open or closed state. SQLite is authoritative for execution state. A Task is one Project and one issue number for life: opting in again reuses the Task, its branch, and its pull request. A merge by Hephaestus after approval, or by a Trusted User, makes the Task `Merged`; a merge by anyone else makes it `Cancelled`, reported on the issue and never reverted; a Trusted User accepts such a merge with `confirm` on the pull request.
- AR-12 (AD-3): Dependency direction enforced by project references. Core references no adapter project and no Octokit, Docker, EF Core, or ASP.NET Core package. Adapters reference only Core. Only Host references adapters. Core defines the ports `IAgentAdapter`, `IWorkspaceRuntime`, `ICodeHost`, `IUnitOfWork`, `IReadStore`, `ISignalInbox`, `IPollCursorStore`, `IOutputStore`, `ISecretStore`, `INotifier`, `IChangeFeed`. Persistence implements the six storage ports; Host implements `IChangeFeed`.
- AR-13 (AD-4): One GitHub gateway. `Hephaestus.GitHub` is the only code that talks to GitHub, the only holder of the GitHub App credential, the owner of the bare mirrors, and the only code that pushes. The poller only appends Signals. There is no code path that pushes, force-pushes, or deletes any ref outside `hephaestus/*`; the only write to a Base Branch is a merge call carrying the approved head commit id.
- AR-14 (AD-5): Effect journal. Only the loop writes the journal; every effecting port method takes the idempotency key as a parameter. Effects are push, pull request open and update, comment, label change, merge, issue creation, container, volume, and network creation and removal, notification. On recovery an intent without a result is resolved by natural key before any retry (pull request by head branch, comment and created issue by hidden marker, label as a set operation, push as fast-forward, merge by pull request state, Docker object by label, removal by absence; a notification is not sent again). An effect that fails because the far side is unreachable is retried at a recorded next-attempt time within a bound; at the bound the Task moves to `WaitingForOwner`, not `Failed`. The journal is append-only and is the audit trail. *(owner decision, 2026-11-25)* An effect the far side refuses for good is not retried: the refusal is journaled and the Task moves to `WaitingForOwner`.
- AR-15 (AD-6): Workspace shape. A Task owns exactly one named Docker volume; each step runs in a fresh container removed when the step ends. Before every step a credential-free container re-creates `/workspace` from the bundle of the last pushed tip: a fresh clone with no hooks, default configuration, and no untracked or ignored files. Only `Implementation`, `Correction`, and `Rework` steps are checkpointed. A step container receives only the credential of the Agent performing that step; Validation, checkout, and checkpoint containers receive none. No host bind mounts. A Task volume is mounted with Docker's copy-from-image switched off (`volume-nocopy`) in every container that mounts it, so that what an image holds at `/workspace` can neither enter the volume nor change the owner of its root. The Task volume is removed, as a journaled effect, only when the Task has left `Running` and its last checkpoint tip is pushed.
- AR-16 (AD-7): Container as security boundary. The Docker socket is mounted into the `hephaestus` container only. A step container has exactly these mounts: Task volume at `/workspace`; context, Methodology, and agent binaries read-only; a temporary `HOME`. All capabilities dropped, `no-new-privileges`, default seccomp profile, process-count limit, no published port, fixed user `10001:10001`, time, processor, and memory limits. Step containers cannot reach the host, loopback, link-local or private ranges, or each other. The runtime sets the proxy variables with loopback excluded and rewrites `github.com` SSH remotes to HTTPS; it sets nothing specific to a language or build tool. The proxy is reached from a step container at one fixed host name and port, which are documented and part of the versioned public surface. Hosts the proxy refused are recorded on the step run. Project Configuration can set only the image, resource limits within the installation's, Validation commands, and allowed egress hosts. Inside a step container the Agent's own sandbox and permission prompts are off. Every invocation disables tools that execute outside the step container under the vendor account and switches off repository-supplied executable Agent configuration (hooks, MCP servers, settings, plugins). An invocation takes Agent settings only from the throwaway `HOME`, which Hephaestus controls, and from nothing under `/workspace`; the tools it offers the Agent are a closed list fixed by the adapter. "Guaranteed" means shown by the planted-configuration test for the pinned CLI version and the authentication mode; an adapter that cannot show it declares that mode unsupported, and a change of pin, of a switch, or of the tool list repeats the test. For repository plugins the test is owed: no pinned version loaded one from a repository's settings, so no claim is made about them until a test shows a switch stops one. Instruction files and skills in a repository are prompt content, not executable configuration (T2). Confirmed by the injection spike (Story 1.1, 2026-11-26).
- AR-17 (AD-8): Agent CLIs are pinned and injected. Each release pins, in `deploy/agents.lock`, one version of each Agent CLI and the SHA-256 of every executable it consists of, per architecture and, where the vendor builds per C library, per C library: Claude Code is one executable in a glibc and a musl build; Codex CLI is two statically linked executables, `codex` and `codex-code-mode-host`. Where the vendor publishes an archive, the lock pins the archive and each executable extracted from it. Hephaestus images contain no Agent binary: at startup the orchestrator downloads the pinned files from the vendor's official channel, verifies each against the lock, and fills the agent binaries volume; startup stops unless every pinned executable is present and verified. The vendor's signature over a release is verified when a pin changes, by a check in Hephaestus's own repository, not at startup. The orchestrator also places in that volume a CA certificate bundle copied from the `hephaestus` image's own certificate store. The volume is mounted read-only into the Project's image. Project image contract: Linux, glibc or musl, `bash` present; nothing else is required of it. The runtime establishes the image's C library when it resolves the image digest at Task start, records both in the configuration snapshot, and mounts the matching executables at `/hephaestus/agents`; every step runs that digest. CLI self-update is disabled in step containers. Confirmed by the injection spike (Story 1.1, 2026-11-26). No Hephaestus base image exists: one that contained Claude Code could not be published.
- AR-18 (AD-9): Bundle boundary and checkpoints. Code enters and leaves a Workspace only as a git bundle; the orchestrator never runs git or any tool in an agent-writable directory; mirrors are bare with no hooks. A checkpoint follows every checkpointed step, including one ended by timeout, crash, or cancellation: it builds its own git directory from the last pushed tip, writes exactly one commit whose parent is that tip, and flattens the Agent's own commits; an unchanged tree makes no commit and no push. A bundle leaves a container as one regular file of bounded size under a runtime-chosen name, read from a stopped container. The gateway imports it into a quarantine ref with strict object checking, accepts only the Task branch, verifies a fast-forward of the last pushed tip, scans every new blob and commit message with the masker, then pushes; a failed check blocks the push, keeps the Workspace, and raises an Escalation. Each step run records the tip before and after it; attribution is read from those records.
- AR-19 (AD-10): Pure agent adapters. An `IAgentAdapter` describes the invocation for a step, describes a probe (present, authenticated, has capacity), parses a stored output stream into normalized events, produces a normalized result with usage where reported, and classifies a CLI that exited by itself. An invocation is a fixed record: argument vector, environment, secret references, files, required egress hosts for the authentication mode, and where the result is found. Adapters start no process and know nothing about Docker. The adapter describes the invocation for the Agent's authentication mode; where the CLI allows it, the argument vector is the same in every mode and only the secret reference and the declared hosts differ. A file an invocation needs from Hephaestus, such as the CA bundle, is named by its path under `/hephaestus/agents`. The egress hosts an adapter declares are those observed for the pinned version; they are a minimum, and a missing one shows up as a host the proxy refused. Alias resolution happens in Core; mapping to vendor flags happens in the adapter. Effort is a closed Core set each adapter maps or declares unsupported, checked at intake.
- AR-20 (AD-11): Failure classes and bounds. Every failed step run carries exactly one of `usage-limit`, `timeout`, `agent-error`, `validation-failure`, `infrastructure-error`, `cancelled`, `unknown`, decided in one order: the runtime's cause, then the adapter's reading, then Core. Failed Validation and blocking Findings spend Correction rounds; `timeout`, `agent-error`, `infrastructure-error`, and `unknown` spend the step retry bound of that step kind (default 2). `usage-limit` spends neither and is the only class leading to Handoff or `WaitingForCapacity`; Handoffs per Task have their own bound.
- AR-21 (AD-12): Artifact-sufficient steps. Every step is executable from the context files and repository state alone, in a fresh Agent session; session resume is not used. Core assembles one context file set per step run under fixed names (`instruction.md`, `task.json`, `issue.md`, `comments.json`, `plan.md`, `findings.json`, `validation.json`, `handoff.md`, `result.schema.json`); the runtime places it read-only; the adapter's prompt is one fixed sentence pointing at `instruction.md`. Start, correction, rework, and Handoff use the same mechanism. The handoff summary is assembled by Core from recorded data. A repository's own Agent instruction file (`CLAUDE.md`, `AGENTS.md`) is not given to an Agent: each adapter switches its CLI's reading of it off, and a Project instructs Agents through its Methodology. No step depends on a CLI's own skill, sub-agent, or slash-command mechanism; a template instructs through files the Agent reads with the tools the invocation offers.
- AR-22 (AD-13): Validated step results. Results by step kind: `Readiness` gives a verdict, `Planning` a plan, `Implementation`, `Correction`, and `Rework` an implementation summary, `Review` Findings. Every result has an outcome of `completed` or `needs-decision`; the latter carries questions and options and becomes an Escalation. Result types are Core records restricted to what both CLIs' schema-constrained output accepts. Core validates by strict deserialization; a missing or invalid result is an `agent-error`. Core gives each Finding an id; a Review after a Correction returns the status of every open Finding plus any new ones. Each Validation command runs in its own credential-free container on a fresh checkout. The author is the Agent of the latest checkpointed step run that changed the tree; the Reviewer is the other of the Task's two Agents; Correction goes to the author.
- AR-23 (AD-14): Methodology as data. A Methodology is a directory with a manifest and one instruction template per Agent step kind; `default` and `bmad` ship in the Hephaestus image. Core selects a template by step kind only and never branches on the Methodology name or the Agent. Templates contain no substitution and refer only to context file paths. Project-supplied Methodology files are read from the Base Branch at the commit of the Task's configuration snapshot and placed read-only at `/hephaestus/methodology`.
- AR-24 (AD-15): Trust decided once. The gateway attaches the acting user's numeric id to every inbound Signal, taken from GitHub's event data. One admission function in Core decides trust; a login is display only. Signals from anyone else are recorded, reported on the issue, and change no Task, except a merge. *(owner decision, 2026-11-25; in the spine since the third update)* Only label actions by anyone else are reported on the issue, once per user, kind of action, and issue; their comments and commands are recorded and get no reply. Hephaestus's own identity is never a Trusted User and its content is never admitted. Commands are parsed by one parser in Core.
- AR-25 (AD-16): Configuration, secrets, masking. Project Configuration is returned by the gateway as raw bytes with its commit id, parsed and validated in Core, and snapshotted onto the Task at start. Installation Configuration lives in SQLite and is written by API handlers in their own transaction; the loop reads it at the start of each tick. Secret values live as files on a volume mounted only into the `hephaestus` container; the database holds names; values are entered through standard input only and never written to the journal, a step-run record, a Docker label, or a container name. Nothing produced by a step container is written to the secrets volume. One masker, built from `ISecretStore` and rebuilt when a secret changes, matches across chunk boundaries and each value's base64 and URL-encoded forms, and is applied by the runtime before raw output is stored, by the gateway before every outbound body and every push, and by the logger.
- AR-26 (AD-17): One host process, two listeners. One long-running container named `hephaestus` hosts the loop and an HTTP API. The **control listener** is a Unix domain socket inside that container: it carries the whole API and is the only place an endpoint that appends a Signal, changes configuration, issues or revokes a Dashboard Token, or touches a secret is mapped. The CLI is the same executable in client mode, run with `docker exec`, and only calls it. Everything that affects a Task or a Project's work is a Signal: the endpoint returns 202 with the Signal id, and the Signal's status reports pending, applied, or rejected with RFC 9457 problem details. The **dashboard listener** is TCP (container port 8717, published as `127.0.0.1:8717`): it serves the Dashboard's static files and read endpoints only, under `/api`, and a test fails when an endpoint that is not a read is mapped on it. It refuses a request whose host name is not one of its loopback names, and the `hephaestus` container shares no Docker network with the egress proxy or any step container. Every read needs a Dashboard Token in a request header; no cookie is used and a token never appears in an address; tokens are Installation Configuration, stored only as a hash, shown once, never logged, and revoking one closes the streams opened with it. Reads on either listener go through `IReadStore` on a read-only SQLite connection and through `IOutputStore`; no endpoint depends on `IUnitOfWork`. The Dashboard and the HTTP API are not part of the versioned public surface in v1.
- AR-27 (AD-18): The loop never awaits a step. `IWorkspaceRuntime` starts a step and returns when the container is created. The runtime writes the raw stream through the masker to `IOutputStore` and appends exactly one step-exited Signal with the step-run id, exit code, cause, and refused hosts. A step run records its deadline before its container starts; the runtime alone stops a container at its deadline. Checkout, Validation, checkpoint, and probe containers follow the same contract. One output file per container run. No behavior depends on a CLI ending by itself: with the network gone, neither pinned CLI stopped within a minute.
- AR-28 (AD-19): Task branch is untrusted code. The GitHub App is granted contents, issues, and pull requests read and write, and metadata, checks, and statuses read; never `workflows` or administration. The gateway does not push a bundle whose diff against the Task's start commit touches a protected path (default `.github/**` and `hephaestus.yml`; the list is Installation Configuration); a hit raises an Escalation and keeps the Workspace. Agent-derived text is neutralised before posting: HTML comments escaped, mentions and issue-closing keywords broken, command keywords fenced. A hidden marker or pull request counts for recovery only when written by Hephaestus's own identity and matched by a journaled intent.
- AR-29 (AD-20): Capacity is a fact about an Agent. Capacity is recorded per Agent credential (exhausted until, last probe), consulted by dispatch before choosing an Agent. An Agent found unavailable by a probe has no capacity; that is not a failed step run. A probe runs with no Task volume.

**Consistency conventions (spine)**

- AR-30: Vocabulary and identity. PRD Glossary terms are the type and member names. A Project is identified by GitHub repository id, a user by numeric id. Entity ids are UUID version 7 stored as text.
- AR-31: Task states `Queued`, `Running`, `WaitingForOwner`, `WaitingForCapacity`, `PullRequestOpen`, `Failed`, `Cancelled`, `Withdrawn`, `Merged`, `Closed`; the lifecycle diagram in the spine is the complete transition table. Step kinds: Agent steps `Readiness`, `Planning`, `Implementation`, `Review`, `Correction`, `Rework` (v0.3); runtime step `Validation`. Publishing and merging are effects of the loop, not step runs.
- AR-32: Time is UTC, stored as fixed-width ISO 8601 text through one value converter; Core reads time only from `TimeProvider`. JSON and YAML use camelCase keys and kebab-case enum strings through `System.Text.Json`; usage fields are nullable and absent is never stored as zero. Every I/O method is async and takes a `CancellationToken` last. Ports are `I<Name>` in `Hephaestus.Core.Ports`; adapters are `<Vendor><Port>`.
- AR-33: GitHub surface conventions. Marker label `hephaestus`; state labels `hephaestus:<state>` in kebab-case; role labels `primary:<alias>`, `fallback:<alias>`, `effort:<alias>`. A command is a comment by a Trusted User whose first line is `/hephaestus <verb>`, from a closed verb set (`confirm` in v0.1, `rework` in v0.3). *(owner decision, 2026-11-25)* `confirm` also accepts commits on a Task branch that Hephaestus did not push. Branch name `hephaestus/<issue-number>-<slug>`, derived once and stored. Hidden marker `<!-- hephaestus:effect=<idempotency-key> -->` as the last line of every comment and created issue body. Idempotency key `<scope>:<cause>:<effect-kind>:<ordinal>`. Commit message composed by Core with trailers `Hephaestus-Task`, `Hephaestus-Step`, `Hephaestus-Agent`, `Hephaestus-Model`, `Hephaestus-Effort`; the committer is the Hephaestus GitHub identity.
- AR-34: Container conventions. Repository at `/workspace`, context at `/hephaestus/context`, Methodology at `/hephaestus/methodology`, agent binaries at `/hephaestus/agents` with the CA bundle at `/hephaestus/agents/certs/ca-certificates.crt`, `HOME`, a tmpfs, at `/hephaestus/home`. The Task volume is mounted `volume-nocopy`. Every container, volume, and network carries labels `hephaestus.installation`, `hephaestus.task`, `hephaestus.step-run`, with names starting `hephaestus-`. Checkout and checkpoint containers run the `hephaestus-tools` image (git only), never the Project image.
- AR-35: Persistence conventions. One `DbContext` and one migration set; SQLite in write-ahead-log mode with a busy timeout; copies and backups are made with SQLite's backup operation with the loop stopped. Raw Agent output is files on the data volume, indexed in SQLite, removed by retention; structured history stays in SQLite.
- AR-36: Logging through `Microsoft.Extensions.Logging`, JSON to stdout, with `taskId` and `stepRunId` scopes, through the masker.
- AR-37: Test conventions. Adapters are tested against recorded CLI transcripts tagged with the CLI version. A scripted fake `IAgentAdapter` drives end-to-end tests with no vendor call. Persistence and runtime tests use real SQLite and Docker through Testcontainers.

**Deployment and operations**

- AR-38: Images `hephaestus` and `hephaestus-tools` on GHCR, built for linux/amd64 and linux/arm64 by GitHub Actions, published by the two channels of AR-77.
- AR-39: Volumes `hephaestus-data` (SQLite, agent output, bundles, pre-migration copies), `hephaestus-secrets`, `hephaestus-mirrors`, `hephaestus-agents`, and one `hephaestus-ws-<task-id>` per Task. The data volume and the secrets volume are the whole installation state.
- AR-40: Startup sequence: back up the database, run migrations, stop on failure leaving the backup; fetch and verify the pinned Agent executables and place the CA bundle; then the loop's first tick resolves unfinished effects and step runs.
- AR-41: Egress proxy deployment: one unprivileged container attached to the internal workspace network and to a network with outbound access; step containers attach to the workspace network only. It has no credential, no volume, and no Docker socket.
- AR-42: Health and status: the API reports loop liveness and the last tick.
- AR-43: GitHub is polled with conditional requests; no public webhook endpoint is required.
- AR-44: One environment (the Owner's installation). Development uses the same Compose file with a locally built image and its own installation id.

**Security verification (threat model §8)**

No security claim may appear in user-facing material until its test exists and passes (NFR-8). The tests the threat model requires:

- AR-45: No GitHub credential in a Workspace: an end-to-end test searches the Task volume, container environment, and context files for the credential value after a full Task.
- AR-46: Step container definition: an integration test inspects user, capabilities, security options, mounts, and network, and from inside attempts to reach the socket, the API, the dashboard port, the host gateway, a link-local address, and another container, directly and through the proxy, expecting failure.
- AR-47: Egress proxy: a direct connection bypassing the proxy fails; a name resolving to a private address is refused; with an allowlist, a host outside it is refused and recorded with the step and a host on it is reached; a Validation container cannot reach a vendor host the Project did not list; the proxy container is inspected for user, capabilities, mounts, and credentials.
- AR-48: Untrusted merge: a recorded merge event from an untrusted actor cancels the Task, a dependent Task is not dispatched, and a Trusted User's `confirm` moves the Task to merged.
- AR-49: Fresh checkout: a hook, a git configuration entry, and an ignored fake test runner planted in one step exist in neither the next step nor Validation.
- AR-50: Bundle boundary: a repository with hostile `.git/config` and hooks, and archives with link entries, parent paths, and extra entries, are imported or rejected without executing anything.
- AR-51: Trusted-actor admission: recorded events from untrusted actors, edits by non-trusted users, renamed logins, and forged markers cause no state change.
- AR-52: Masking and push scan: a secret seeded plain and encoded into agent output and a tracked file is found in none of stored output, logs, outbound text, or the remote branch.
- AR-53: Protected paths and ref limits: a bundle touching a protected path, a non-fast-forward bundle, and a push outside `hephaestus/*` are all refused.
- AR-54: Idempotency and recovery: fault injection stops the process between every intent and its result; no duplicate pull request, comment, or Workspace appears.
- AR-55: Bounds: deadlines on Agent runs and Validation, the Correction bound, and the step retry bound are tested.
- AR-56: Disabled account tools and repository configuration: shown by the injection spike for the pinned versions; repeated with the Owner's credentials whenever a pin changes; a repository plugin that loads is still owed.

**Documentation obligations**

- AR-57: Installation documentation covers GitHub App setup (permissions per AR-28) as a required step.
- AR-58: Installation documentation presents API key and subscription sign-in on an equal footing, with the provider terms that apply and the policy risk of subscription use under automation; an API key with a vendor-side spend cap is named as the conservative choice.
- AR-59: Installation documentation tells Owners to give workflows triggered by `hephaestus/*` branches and their pull requests a read-only token and no deployment or publishing secrets, and recommends branch protection on the Base Branch.
- AR-60: Documentation states that the Marker Label, not state labels, is the control (state labels are re-asserted), and lists the common per-ecosystem proxy settings a Project image needs: Gradle's JVM system properties and Maven's `<proxy>` element, with the proxy's fixed address.
- AR-61: Documentation states the security assumptions and known limitations of the threat model (§6, §7), including open egress in v0.1 and the orchestrator being root-equivalent.
- AR-62: Every release has a changelog entry; the project is published under the MIT license.

**Requirements that reach across releases**

- AR-63: Before v0.2 the notifier is a no-op implementation of `INotifier` in Host; the "Owner is notified" consequences of FR-35 and FR-48 are met through issue comments (FR-52).
- AR-64: FR-20 preserves work on a Usage Limit interruption in v0.1, but detecting the limit as such (FR-27) arrives in v0.2. Until the Usage Limit spike, most real limits are classified `unknown` and retried, not handed off.
- AR-65: Before v0.3 a Trusted User merges on GitHub and the merge arrives as a Signal. A Task cancelled by someone else's merge satisfies no dependency until a Trusted User confirms that merge.
- AR-66: Live progress inside a step is not available in v1, because the stream is parsed after the container exits.

**Design decisions deferred to the epic that settles them (spine, Deferred)**

- AR-67 (v0.1 execution epic): which egress proxy is used, how it is started and given each step run's allowed hosts, how its refusals are collected, SSH rewrites for hosts other than `github.com`, how step containers stay unreachable from each other, the proxy's fixed host name and port as seen from a step container, and whether the proxy is a Compose service or created by the orchestrator.
- AR-68 (intake epic): what the Owner is told, and what happens to the Task, when a declared dependency failed, was closed without a merge, is not a Task, or is part of a cycle; what opting in again means for a Task cancelled by an untrusted merge; when admitted content is read again for the hash comparison; the Priority Label spelling.
- AR-69 (Methodology epic): Story-to-issue identity and drift handling (FR-66); which BMAD workflow serves each step kind.
- AR-70 (v0.2 epics): Usage Limit detection signals and capacity probing detail; notification channels (ntfy, SMTP) and the project that implements `INotifier`; Schedule evaluation and time zone handling; spend limit accounting; a size limit on Task volumes. Starting facts from the injection spike: every Claude Code run under subscription sign-in emits a `rate_limit_event` with `status`, `resetsAt`, and `rateLimitType`, and runs with an API key emit none; Claude Code reports a cost per run, Codex CLI reports token counts and no cost.
- AR-71 (v0.3 epics): Stack update after Rework and after merge (rebase or merge, squash or merge commit, any exception to fast-forward-only; a stack update needing a working tree runs in a Workspace, not a mirror); relaxing dependency satisfaction from `Merged` to "has produced a branch"; independence detection when no dependency is declared.

**Dashboard (spine AD-17, threat model §4.7)**

- AR-72: Dashboard build. An Angular CLI workspace in `src/Hephaestus.Dashboard` with npm and a committed lockfile, unit-tested with the CLI's runner. It is built in a Node.js stage of `deploy/Dockerfile` into static files the Host serves; the runtime image contains no Node.js. It loads nothing from another origin: fonts, icons, and styles are bundled. GitHub Actions builds and tests it on every pull request, and the license check covers its npm packages (NFR-15). How its types stay in step with the read API is fixed by the first dashboard story and followed by the rest.
- AR-73: Dashboard security tests (threat model T12, T34 to T38). Read-only: an architecture test lists every endpoint mapped on the dashboard listener and fails on one that is not a read, and every changing request the control API accepts is refused there. Access: no token, a wrong token, and a revoked token return no data; a foreign host name is refused; the published port is bound to loopback only; from a step container the port is unreachable directly and through the proxy; the token value is in neither the database nor the logs. Content: markup and script seeded into an issue body, a Finding, and Agent output are rendered as text; the response carries a content security policy that allows no other origin and no inline script; the loaded Dashboard requests no other origin and sets no cookie; a lint rule forbids bypassing the framework's escaping.
- AR-74: Change stream. One read endpoint, `/api/changes`, answers with Server-Sent Events. After each committed transaction the loop, and a control API handler after its own, announces what changed through `IChangeFeed`. An event is `{ "scope": "task" | "project" | "installation", "id": "<id>" }` and never carries content. An announcement is not an effect and is not journaled. The Dashboard reads what it shows through the read endpoints and reads everything on screen again when the stream reconnects. The stream needs the token in a request header, so the Dashboard opens it with a streaming request, not with the browser's `EventSource`. Open streams are bounded (default 8, Installation Configuration), and a stream that falls behind is closed. No channel on which a browser can send exists.

**Language (spine AD-21)**

- AR-75 (AD-21): Language is data. Every sentence Hephaestus produces for a person is a message id with parameters, resolved in Core from resource files for `en` and `pl`; adapters and API handlers pass ids and parameters and compose no prose; a test fails when an id has no entry in a supported language. One Core function resolves the language: the Task's configuration snapshot (the Project's `language` key, else the installation's) for text on GitHub and for Agent prose, or the Project's when no Task exists; the installation's for the command line and Notifications; the browser's choice, else the installation's, for the Dashboard. Never translated: command names and options, `--json` output, problem `type` values, label and state names, comment-command verbs, `Depends-On:`, hidden markers, commit trailers, configuration keys, log lines, and Glossary terms. `task.json` carries the Task's language; Methodology templates stay single-language, contain no substitution, and instruct the Agent to write the prose fields of its result in that language. The Dashboard ships both languages in the image.

**Hephaestus's own repository (spine conventions, threat model T39)**

- AR-76: Branches and checks. Two protected branches: `develop`, the default, and `main`. Every change arrives by pull request: features into `develop`; `develop`, or a `hotfix/*` branch, into `main`. The build fails below 80% line coverage in the .NET solution and in the Angular workspace, with exclusions kept in one file, each with a reason. SonarQube Cloud analyses every same-repository pull request and both branches, and its built-in quality gate, which requires 80% coverage on new code, must pass. No SonarQube token is stored: the workflow uses secretless analysis, exchanging a GitHub-issued OpenID Connect token for a credential that lasts thirty minutes and can only submit an analysis for this project, with job permissions `id-token: write` and `contents: read`. Pull requests from forks cannot obtain it and get no analysis. SonarQube Cloud builds and checks this repository; it is not needed to run Hephaestus.
- AR-77: Releases and channels. One file states the version. A pull request to `main` fails unless that version is a valid three-part Semantic Version, higher than the last one published, and present in `CHANGELOG.md`. A merge to `main` creates the tag `v<version>` and publishes both images under it; a version with a pre-release identifier gets its own tag and does not move the stable tags. A merge to `develop` publishes `develop` and `develop-<short commit id>`, a preview channel outside the versioned public surface. Publishing an image deploys nothing.

### UX Design Requirements

From `DESIGN.md` and `EXPERIENCE.md` (`ux-Hephaestus-2026-11-25`). Both are drafts: the Owner chose the two themes, the ember accent, Angular Material, and the noun-then-verb command grammar; passages marked `[ASSUMPTION]` in them stand until the Owner changes them. Where a story and a spine differ, the spine wins.

**Dashboard: visual layer (`DESIGN.md`)**

- UX-DR-1: Two themes, light and dark, built from the tokens in `DESIGN.md` and mapped once onto Angular Material's system tokens. The Dashboard follows the system setting; a switch in the top bar overrides it and the choice is remembered in that browser. No component hard-codes a colour.
- UX-DR-2: One accent, ember, in Material's primary role: links, the selected navigation item, the selected tab, the focus ring, the primary button of Unlock. Never a Task state, a warning, or decoration. No gradients, textures, or forge imagery.
- UX-DR-3: State chip. One component used wherever a Task state appears: the state's own colour token on the icon and left edge, an icon, and the state's name as in the `hephaestus:<state>` label; `cancelled`, `withdrawn`, and `closed` share one token. A tooltip says what the state means and what ends it. Text meets 4.5:1 and state colours 3:1 in both themes.
- UX-DR-4: Typography from system font stacks: `headline`, `title`, `body`, `label`, `mono`. Everything the Owner might copy (ids, commit ids, branch names, commands) and all Agent and Validation output is set in `mono`.
- UX-DR-5: Everything bundled: Material Symbols as local SVG, no font file, nothing from another origin. Flat surfaces separated by borders; elevation only for menus, tooltips, and the side sheet; Material's compact density with 40-pixel table rows.

**Dashboard: behaviour (`EXPERIENCE.md`)**

- UX-DR-6: Frame. A navigation rail listing the surfaces that exist in the running release, a top bar with the page name, the live indicator, the theme switch, and Lock. At most three levels: list, item, side sheet. Filters, the selected tab, and the open step live in the address.
- UX-DR-7: Unlock. The only form. The token is typed or pasted, sent in a request header, kept in session storage, never placed in an address; Lock forgets it. A wrong token says "That token is not valid." without revealing whether a token of that name exists; a token revoked while in use returns to Unlock with the command that issues a new one.
- UX-DR-8: Brand-layer components: step timeline (oldest at the top, a node opens the output side sheet, a running step shows "Running since …"), output viewer (text as stored, paged, copy, no colouring), Finding list (blocking first, resolved ones kept), act-elsewhere hint, copyable value, live indicator, external link, empty state.
- UX-DR-9: Where to act. The Dashboard offers no action; where the Owner would act it shows one sentence and one target, as the table in `EXPERIENCE.md` fixes: a link to GitHub, or a full command in `mono` with a copy button.
- UX-DR-10: State patterns, as the table in `EXPERIENCE.md` fixes: no token, first load with skeleton rows, Hephaestus not reachable (content kept and dimmed with the time it was read), stream reconnecting, loop not alive, each empty list, step running, output removed by retention, unknown address, removed Project. A value that does not exist reads "No data" or "None", never zero.
- UX-DR-11: Live behaviour. A view changes when the change stream says its Task or Project changed: rows update in place, the reader's scroll position never moves, the page never reloads, and nothing is highlighted or animated beyond the chip changing.
- UX-DR-12: Accessibility floor, WCAG 2.2 AA: state by icon and name as well as colour; full keyboard reach with a visible focus ring; focus moves to the headline on navigation; polite, sparing live announcements; tables with header cells and captions; reduced motion honoured; output readable line by line.
- UX-DR-13: Responsive: full layout from 1024 px; from 768 px the rail collapses to icons and low-priority columns fold into the row; below 768 px usable but not designed for.

**All three surfaces**

- UX-DR-14: Voice. Facts, then what comes next; Glossary terms exactly and capitalised; exact times with the zone; no exclamation marks, emoji, praise, or apology. At most one forge reference per surface, only in an empty state or the release name. The same rules hold in English and in Polish.

**Command line (`EXPERIENCE.md`, Command Line)**

- UX-DR-15: Grammar. `hephaestus <noun> <verb> [<target>] [--option <value>]`; singular, hyphenated nouns; verbs from the closed vocabulary; a Project is `<owner>/<repo>` and a Task `<owner>/<repo>#<issue-number>`; long `--kebab-case` options, `-h` the only short one, `--help` everywhere; a secret is read from standard input and refused as an argument; no command ever prompts. The command tree in `EXPERIENCE.md` names each story's commands; a story may adjust its own within the grammar.
- UX-DR-16: Output. Aligned tables and `name: value` lines for people; `--json` on every command that prints data, one document in the API's shape and nothing else on standard output; `none` or `null` for an absent value; times in UTC; errors on standard error as title then detail; exit codes 0, 1, and 2; a command that sends a Signal prints its id and waits up to ten seconds for applied or rejected unless `--no-wait`; a value shown once is printed alone on standard output.

**Text on GitHub (`EXPERIENCE.md`, Text on GitHub)**

- UX-DR-17: Comment anatomy: a bold headline saying what happened, at most five lines or one small table of facts, a line starting "Next:", then the hidden marker. Longer material is collapsed. The templates in `EXPERIENCE.md` give the headline and the "Next:" line for each event.
- UX-DR-18: Escalation shape: "Hephaestus needs a decision.", the decision in one sentence, lettered options, how to answer, the hidden marker. One Escalation asks for one decision; Agent-written question text is quoted, not paraphrased.
- UX-DR-19: Pull request description: What changed, Task, How it was checked, Who did what, Findings, Plan, and from v0.3 Stack, in that order, omitting an empty section. A draft for a failed Task opens with "This Task did not pass." Whether the Task's issue is named with a closing keyword is open and is settled with the Owner in Story 3.13.
**Language (`EXPERIENCE.md`, Voice and Tone)**

- UX-DR-20: English by default, Polish selectable. Translated: Dashboard labels, hints, empty states, tooltips, and error sentences; the command line's sentences and table headers; comment headlines, the "Next:" marker, pull request section titles, an Escalation's fixed sentences; Notifications; and, in the Task's language, prose an Agent writes. Never translated: state names, ids, commands shown for copying, command names and options, `--json` output, labels, `/hephaestus` verbs, `Depends-On:`, hidden markers, configuration keys, log lines, and Glossary terms. The Dashboard's top bar has a language switch beside the theme switch; the choice is remembered in that browser and switching requests nothing from another origin.

### FR Coverage Map

FR-1: Epic 1 - Register a Project
FR-2: Epic 1 - Project Configuration in the repository, read from the Base Branch
FR-3: Epic 1 - Installation Configuration from the command line
FR-4: Epic 1 - Secrets stay secret (secret store and masker; each later epic applies the masker where it writes)
FR-5: Epic 1 - Technology-agnostic Projects (image chosen per Project; exercised end to end in Epic 3)
FR-6: Epic 2 - Opt-in by Marker Label from a Trusted User
FR-7: Epic 2 - Per-Task selection through Role Labels
FR-8: Epic 2 - Queue order (policy built here; exercised at dispatch in Epic 3)
FR-9: Epic 4 - Readiness Check
FR-10: Epic 2 - Declared dependencies (satisfied by merge; relaxed in Epic 11)
FR-11: Epic 2 - Task state visible on the issue
FR-12: Epic 6 - Per-Project Schedule
FR-13: Epic 6 - Soft end time
FR-14: Epic 3 - One Task at a time
FR-15: Epic 6 - Work in progress comes first (the interrupted-first dispatch rule is built in Epic 3; Epic 6 adds the case of several Projects' Schedules)
FR-16: Epic 3 - Start on demand
FR-17: Epic 3 - Isolated Workspace per Task
FR-18: Epic 3 - Least credentials
FR-19: Epic 3 - Bounded execution
FR-20: Epic 3 - Incomplete work is preserved
FR-21: Epic 3 - Recovery after restart
FR-22: Epic 3 - Safe cleanup
FR-23: Epic 9 - Cancel a Task from the command line
FR-24: Epic 3 - Two Agents: Claude Code and Codex CLI
FR-25: Epic 3 - Owner-chosen authentication mode
FR-26: Epic 3 - Agent availability check
FR-27: Epic 7 - Usage Limit detection
FR-28: Epic 7 - Handoff to the Fallback Agent
FR-29: Epic 7 - Wait and resume
FR-30: Epic 3 - Attribution
FR-31: Epic 3 - Validation decides
FR-32: Epic 4 - Independent Review
FR-33: Epic 4 - Classified Findings
FR-34: Epic 4 - Bounded Correction Loop
FR-35: Epic 4 - Failed Task outcome as a draft pull request
FR-36: Epic 3 - Pull request per Task (Epic 4 adds the Reviewer and Findings to the description)
FR-37: Epic 11 - Stacking mode per Project
FR-38: Epic 11 - Independent Tasks stay independent
FR-39: Epic 11 - Stack navigation
FR-40: Epic 11 - Tasks above a failed Task
FR-41: Epic 10 - Merge after approval
FR-42: Epic 11 - Stack order on merge
FR-43: Epic 11 - Stack stays readable after a merge
FR-44: Epic 10 - Hard limits (the prohibitions are enforced and tested from Epic 3; Epic 10 adds the merge they guard)
FR-45: Epic 10 - Rework on command
FR-46: Epic 10 - Rework is checked like any other work
FR-47: Epic 11 - Stack follows a Rework
FR-48: Epic 2 - Escalation
FR-49: Epic 2 - Answer resumes the Task
FR-50: Epic 8 - Notifications by email and phone push
FR-51: Epic 8 - Notification failure does not stop work
FR-52: Epic 4 - Progress on the issue
FR-53: Epic 3 - Durable execution history
FR-54: Epic 9 - Status on demand
FR-55: Epic 9 - Real metrics only
FR-56: Epic 12 - Issue template
FR-57: Epic 12 - Authoring skill
FR-58: Epic 1 - Container-based installation
FR-59: Epic 1 - Semantic versioning
FR-60: Epic 1 - Safe upgrades
FR-61: Epic 3 - Methodology per Project
FR-62: Epic 3 - One standard for every Agent (verified for the Reviewer in Epic 4 and for Handoff in Epic 7)
FR-63: Epic 5 - BMAD Method support
FR-64: Epic 3 - Methodology is optional and replaceable; built-in default
FR-65: Epic 4 - Planning within a single Task
FR-66: Epic 5 - Issues from Stories
FR-67: Epic 1 - Read-only Dashboard (the seed; a view is added in Epics 2, 3, 4, 6, 7, 9, 10, and 11)
FR-68: Epic 1 - Dashboard access
FR-69: Epic 1 - Language (the mechanism and the setting; applied to text on GitHub in Epic 2, to Agent prose in Epic 3, and to Notifications in Epic 8)

## Epic List

Twelve epics across three releases. Each epic is usable on its own and needs no later epic to function. The proof-of-concept milestone inside v0.1 (AR-9) is a path of 30 stories through Epics 1 to 3, listed under "Proof-of-concept path" after the epic list.

### v0.1 — Ignite the Forge

### Epic 1: Install Hephaestus and register a Project

The Owner runs Hephaestus with Docker, connects its GitHub App identity, manages Trusted Users, Aliases, limits, and secrets from the command line, registers a repository as a Project, and is told exactly which setting is wrong when `hephaestus.yml` is missing or invalid. The two spikes that gate v0.1 run first, so their results shape everything built after them.

**FRs covered:** FR-1, FR-2, FR-3, FR-4, FR-5, FR-58, FR-59, FR-60, FR-67, FR-68, FR-69

**Also addresses:** NFR-6, NFR-8, NFR-14, NFR-15, NFR-16, NFR-17; AR-1 to AR-5, AR-12, AR-25, AR-26, AR-28 (permissions), AR-30, AR-32, AR-35, AR-36, AR-38 to AR-40, AR-57, AR-62, AR-72, AR-73, AR-75 to AR-77; NFR-18; UX-DR-1 to UX-DR-16, UX-DR-20.

**Implementation notes:** Starts with the injection spike (AR-4) and the BMAD spike (AR-5), then the solution scaffold (AR-1). Builds the host container, the local API on a Unix socket, the CLI in client mode, SQLite with migrations and the pre-migration backup, the secret file store and the masker, the GitHub gateway's App authentication and raw configuration read, and the release pipeline. Its last two stories add the Dashboard: the dashboard listener, the Dashboard Token, the Angular workspace and its build, and the first two views. They are not on the proof-of-concept path. The epic also sets the rules for Hephaestus's own repository: Story 1.3 the branches and the coverage threshold, Story 1.13 the two image channels, and Story 1.16 the SonarQube Cloud quality gate. The message mechanism for the two languages starts with the first command, in Story 1.4.

### Epic 2: Opt issues in and converse on GitHub

A Trusted User applies the Marker Label and sees the Task's state on the issue. Role Labels, the Priority Label, and `Depends-On` lines are honored; labels and comments from anyone else are recorded and ignored. Hephaestus can ask a specific question on the issue, wait, and resume the Task when a Trusted User answers.

**FRs covered:** FR-6, FR-7, FR-8, FR-10, FR-11, FR-48, FR-49

**Also addresses:** NFR-1, NFR-7, NFR-9, NFR-12; AR-7, AR-10, AR-11, AR-13, AR-14, AR-24, AR-31, AR-33, AR-42, AR-43, AR-51, AR-63, AR-68.

**Implementation notes:** Builds the poller with cursors and conditional requests, the Signal inbox, the single-writer orchestrator loop, the admission function, the effect journal, issue snapshots with hash comparison, the command parser with `confirm`, and the state-label projection. Settles the intake decisions deferred by the spine (AR-68). Escalation lives here because Epic 3 already needs it for a blocked push and for a `needs-decision` result.

**Dashboard (FR-67):** Story 2.12 adds Tasks and their states, the Overview's loop line and counts, and the change stream (AR-74).

### Epic 3: From issue to validated pull request

The Owner starts a Project on demand and gets a pull request. One Task runs at a time: an Agent implements the change in a hardened step container that holds no GitHub credential, the work is checkpointed and pushed to the Task branch, the Project's Validation commands decide whether it is acceptable, and a restart at any point loses nothing and duplicates nothing. Story 3.16 closes the proof-of-concept path.

**FRs covered:** FR-14, FR-16, FR-17, FR-18, FR-19, FR-20, FR-21, FR-22, FR-24, FR-25, FR-26, FR-30, FR-31, FR-36, FR-53, FR-61, FR-62, FR-64

**Also addresses:** NFR-2, NFR-3, NFR-4, NFR-5 (duration, processor, memory), NFR-10, NFR-11, NFR-13; the prohibitions of FR-44; AR-8, AR-15 to AR-23, AR-27 to AR-29, AR-34, AR-37, AR-40 (Agent binaries), AR-41, AR-44, AR-45 to AR-50, AR-52 to AR-56, AR-58 to AR-61, AR-64 to AR-67.

**Implementation notes:** The largest epic. Builds the Docker workspace runtime, the egress proxy, the bundle boundary and checkpoints, bare mirrors and push, both agent adapters, the Methodology mechanism with the built-in `default`, context assembly, validated step results, dispatch and windows opened on demand, failure classes and the step retry bound, the output store with retention, and pull request publication. Marker Label removal and issue close while a Task runs are handled here, because the lifecycle table must be total; the Owner's cancel command arrives in Epic 9. Until Epic 4, a Task whose Validation fails ends `Failed` with an issue comment.

**Dashboard (FR-67):** Story 3.22 adds a Task's history, Validation results, stored output, and each Agent's readiness.

### Epic 4: Independent review, correction, and readiness

Every pull request arrives already reviewed by a different Agent than the one that wrote it. Blocking Findings and failed Validation go back to the author within a bounded loop, and a Task that still fails ends in a draft pull request that says what was tried and what remains. An unclear issue produces a question, not code, and a large Task is planned first with the plan shown in the pull request. Each significant step is reported on the issue.

**FRs covered:** FR-9, FR-32, FR-33, FR-34, FR-35, FR-52, FR-65

**Also addresses:** NFR-10; AR-20, AR-22, AR-55.

**Implementation notes:** Adds the `Readiness`, `Planning`, `Review`, and `Correction` step kinds with their result types and `default` templates, Finding ids and status tracking, the Correction bound, and the full set of progress comments.

**Dashboard (FR-67):** Story 4.7 adds Findings, the plan, and Correction rounds.

### Epic 5: BMAD Method projects

A Project that uses BMAD Method has its Tasks planned, implemented, and reviewed with the same BMAD workflow whichever Agent performs the step. The Owner can have Hephaestus create one issue per Story, each linked to its Story, carrying its acceptance criteria and its dependencies, and never created twice.

**FRs covered:** FR-63, FR-66

**Also addresses:** AR-23, AR-69.

**Implementation notes:** Builds on the BMAD spike's findings from Epic 1. Adds the `bmad` Methodology templates, Project-supplied Methodology files read from the Base Branch, and issue creation as a journaled effect with a stable Story identity. Settles Story identity and drift handling.

### v0.2 — Unattended

### Epic 6: Projects on their own schedules

The Owner gives each Project its own working days, start time, and optional end time. Hephaestus works each Project in its window, finishes a Task that runs past the end time, and resumes interrupted work before starting anything new.

**FRs covered:** FR-12, FR-13, FR-15

**Also addresses:** AR-70 (Schedule evaluation and time zone handling).

**Dashboard (FR-67):** Story 6.4 adds Schedules and windows.

### Epic 7: Keep working through Usage Limits

When an Agent runs out of usage, the Task moves to the Fallback Agent with everything it needs, or waits and resumes on its own when capacity returns. Other Tasks keep moving meanwhile.

**FRs covered:** FR-27, FR-28, FR-29

**Also addresses:** NFR-11, NFR-12; AR-6, AR-20, AR-21, AR-29, AR-70 (detection signals and capacity probing).

**Implementation notes:** Starts with the Usage Limit spike (AR-6).

**Dashboard (FR-67):** Story 7.6 adds Agent capacity and Handoffs.

### Epic 8: Notifications

The Owner is alerted by email, phone push, or both when an Escalation is raised, when a Task fails, and when a scheduled window ends. A Notification that cannot be delivered never changes a Task's outcome.

**FRs covered:** FR-50, FR-51

**Also addresses:** AR-63, AR-70 (channels and the notifier project).

**Implementation notes:** The window summary needs Epic 6.

### Epic 9: Control and insight

The Owner cancels a Task from the command line, asks what is running, queued, and waiting, and reads the success metrics computed from real executions only.

**FRs covered:** FR-23, FR-54, FR-55

**Also addresses:** NFR-5 (spend limit); AR-70 (spend limit accounting, Task volume size limit).

**Dashboard (FR-67):** Story 9.6 adds status and the statistics page (FR-54, FR-55).

### v0.3 — Closing the loop

### Epic 10: Approve to merge, command to rework

The Owner's approving review is the only way into the Base Branch: Hephaestus merges approved pull requests at the start of the next window. When the Owner wants changes, `/hephaestus rework` starts a revision that passes Validation and independent Review again, as many times as wanted.

**FRs covered:** FR-41, FR-44, FR-45, FR-46

**Also addresses:** AR-11, AR-13, AR-33 (the `rework` verb).

**Dashboard (FR-67):** Story 10.4 adds pull requests and what each awaits.

### Epic 11: Stacked pull requests

Work continues while pull requests await review: a Task can build on the branch of the Task before it. Each pull request links to its neighbors, merges follow Stack order, a merged or reworked pull request is carried through everything above it, and a failed Task does not silently break what depends on it.

**FRs covered:** FR-37, FR-38, FR-39, FR-40, FR-42, FR-43, FR-47

**Also addresses:** the v0.3 relaxation of FR-10; AR-71.

**Implementation notes:** Builds on Epic 10. Settles Stack update mechanics and independence detection.

**Dashboard (FR-67):** Story 11.8 adds Stacks.

### Epic 12: Task authoring aid

The Owner writes issues that pass the Readiness Check, with an issue template a Project can adopt and a helper skill that works with both Agents.

**FRs covered:** FR-56, FR-57

**Implementation notes:** Repository assets only; no runtime module.

### Proof-of-concept path

Owner decision, 2026-11-25. The proof-of-concept milestone of AR-9 is the shortest dependency-closed path from an empty repository to one labelled issue becoming a validated pull request, with the egress proxy in place. It is an order of work for sprint planning; it moves no story to another epic and changes no story's content.

| Epic | Stories on the path | Stories that follow the milestone |
| --- | --- | --- |
| 1 | 1.1, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.10, 1.11 | 1.2, 1.12, 1.13, 1.14, 1.15, 1.16 |
| 2 | 2.1, 2.2, 2.4, 2.5, 2.9, 2.10 | 2.3, 2.6, 2.7, 2.8, 2.11, 2.12 |
| 3 | 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.10, 3.11, 3.12, 3.13, 3.14, 3.16 | 3.9, 3.15, 3.17 to 3.22 |

Notes on the path:

- It has 30 stories. Stories 1.6, 1.9, 2.5, 2.9, 2.10, and 3.10 are on it because stories later on the path use what they build: the masker, Aliases and limits, comments as journaled effects, Escalation and its answer, and an Agent's credential.
- Story 2.10 is on the path so that a question raised during the demonstration, for example by a blocked push, can be answered and the Task resumed.
- Story 1.2 (BMAD spike) is not on the path but is not delayed by it: it depends only on Story 1.1 and can run beside the path.
- At the milestone only Claude Code is driven; the Fallback Alias must still resolve to a different Agent, and the Codex CLI adapter arrives with Story 3.9.
- Until Stories 2.7 and 2.8 are done, the dispatch-order policy that Story 3.14 applies is the issue number alone.
- The `confirm` command arrives with Story 2.11, which is not on the path; a Task held for confirmation stays held until then. For the same reason, adopting commits Hephaestus did not push is delivered by Story 3.18, not by Story 3.7: on the path such a Task is held and stays held.
- Story 1.16 (the quality gate) is not on the path and is not delayed by it: it depends only on Story 1.3 and should be done early, so that the stories after it are held to the gate.
- No dashboard story is on the path. Stories 1.14 and 1.15 can be built beside it once Story 1.5 is done; Stories 2.12 and 3.22 follow the last story of their epic.
- Story 3.16 is on the path so that a failed Validation or a failed step ends the Task as `Failed` with a comment and frees the single slot; without it the first failure would leave the Task `Running` with no way out.
- The milestone is a demonstration, not a release. The start-up checks of Story 3.15, restart recovery, reactions to GitHub events during a run, history, cleanup, and the security statement (Stories 3.17 to 3.21) are not in place, so it is not left to run unattended.

## Epic 1: Install Hephaestus and register a Project

The Owner runs Hephaestus with Docker, connects its GitHub App identity, manages Trusted Users, Aliases, limits, and secrets from the command line, registers a repository as a Project, and is told exactly which setting is wrong when `hephaestus.yml` is missing or invalid. The two spikes that gate v0.1 run first.

### Story 1.1: Injection spike — Agent CLIs in a hardened container behind a proxy

As the Owner,
I want evidence that both Agent CLIs run when injected into a hardened Project container whose only way out is an HTTP proxy,
So that the Workspace design (AD-7, AD-8) is confirmed or replaced by its fallback before anything is built on it.

**Acceptance Criteria:**

**Given** that the spike needs a real API key and a real subscription sign-in for each vendor, which no dev agent holds
**When** the spike is planned and run
**Then** the Owner supplies the credentials and takes part in the run, and the story is not closed by a dev agent alone

**Given** the Claude Code CLI and the Codex CLI at pinned versions, downloaded from each vendor's official channel
**When** each is run non-interactively from a read-only mount at `/hephaestus/agents` as user `10001:10001` with no passwd entry, `HOME` at a throwaway `/hephaestus/home`, all capabilities dropped, `no-new-privileges`, and the default seccomp profile, in a Debian-based, an Ubuntu-based, and an Alpine-based image
**Then** the report records, per CLI and image, whether the run completed and emitted its machine-readable stream (`claude -p --output-format stream-json`, `codex exec --json`)
**And** it names the Claude Code build that must be selected for glibc and for musl
**And** it records whether each CLI returns schema-constrained output, under which flags, and whether `codex exec` accepts a per-run reasoning-effort override (AR-8)

**Given** a test repository that plants hooks, MCP servers, settings, and plugins for each CLI
**When** each CLI is run in each authentication mode (API key; subscription sign-in where the vendor documents it) with the flags and configuration meant to switch off repository-supplied executable configuration and tools that act outside the container under the vendor account
**Then** the report states, per CLI and authentication mode, whether none of the planted configuration executed and which account-linked tools remain reachable
**And** a mode in which this cannot be guaranteed is marked unsupported (AD-7, threat T5)

**Given** a container attached only to a Docker internal network whose single exit is an HTTP proxy
**When** each CLI makes a model request with the proxy variables set, and again without them
**Then** the request succeeds through the proxy and fails without it
**And** the report lists the hosts each CLI needs in each authentication mode (input to AD-10)

**Given** the same network
**When** npm, `dotnet restore`, pip, cargo, go, Maven, and Gradle each restore a sample dependency
**Then** the report records for each whether it works with the generic proxy variables alone and which settings the Project image must carry

**Given** each vendor's published terms
**When** they are read for redistribution and download rules
**Then** the report states whether fetching the binaries from the official channel at startup is permitted, with the source cited

**Given** all findings
**When** the spike closes
**Then** a report under `docs/spikes/` holds the results and the scripts that reproduce them, and states for AD-7 and for AD-8 either "confirmed" or "fallback: Hephaestus base image" (AR-4)
**And** any change the findings require to the architecture spine is raised with the Owner before a story that depends on it starts

### Story 1.2: BMAD spike — unattended workflows under both Agents

As the Owner,
I want to know which BMAD workflows complete without human input under Claude Code and under Codex CLI,
So that BMAD Method support (FR-63) and the Methodology mount design are built on verified behavior.

**Acceptance Criteria:**

**Given** a sample repository with BMAD Method installed for both Agents, and its Methodology files mounted read-only at `/hephaestus/methodology`, outside `/workspace`
**When** each candidate workflow for readiness, planning, implementation, review, and correction is invoked under `claude -p` and under `codex exec` in the container setup and with the hardened invocations Story 1.1 confirmed, Codex with `project_doc_max_bytes=0` added
**Then** the report records, per workflow and CLI, whether it completed with no human input and what it produced
**And** it names the workflow that serves each step kind, or states that none does

**Given** the workflows that completed
**When** their runtime needs are examined
**Then** the report lists what the Project image must provide (for example `uv` and Python)
**And** it states how a workflow is started when the Agent has no `Skill` and no `Task` tool and is given no repository instruction file, and whether any workflow needs a tool beyond `Bash`, `Read`, `Edit`, and `Write`

**Given** that Hephaestus works from GitHub issues and BMAD works from story and spec files
**When** the mapping is examined
**Then** the report recommends how an issue maps to the artifact a BMAD workflow works from
**And** it recommends how a Story is identified stably across runs (input to FR-66), in a way that survives the renumbering and retitling of Stories

**Given** all findings
**When** the spike closes
**Then** a report under `docs/spikes/` holds the results and the scripts that reproduce them
**And** a step kind with no unattended workflow is raised with the Owner together with its consequence for FR-63 (AR-5)

**Given** a Task whose language is Polish
**When** a BMAD workflow is run for it under each Agent
**Then** the report states how the workflow is told which language to write in and whether its output follows it (FR-69)

**Given** a workflow that completes only with a tool or a setting the hardened invocation switches off
**When** the spike closes
**Then** the report names it, and it is raised with the Owner as a change to the invocation, which repeats the planted-configuration run of Story 1.1 before it is adopted (AD-7)

**Given** Codex CLI, which offers a repository's skills to the model whatever is switched off
**When** a workflow whose files exist both at `/hephaestus/methodology` and in `/workspace` is run
**Then** the report states which copy each Agent followed (AD-14)

### Story 1.3: Solution skeleton with enforced boundaries

As a contributor,
I want a solution whose project references allow only the dependencies the architecture permits,
So that a change made for one feature cannot couple modules that must stay apart.

**Acceptance Criteria:**

**Given** a fresh clone of the repository
**When** the solution is built with the .NET 10 SDK
**Then** it contains `Hephaestus.Core`, `Hephaestus.Agents`, `Hephaestus.GitHub`, `Hephaestus.Workspaces`, `Hephaestus.Persistence`, and `Hephaestus.Host` under `src/`, and one test project per source project plus an end-to-end test project under `tests/`
**And** package versions are managed centrally in `Directory.Packages.props` at the versions the spine's Stack table pins (AR-1, AR-2, AR-3)

**Given** the solution
**When** project references are inspected by an architecture test
**Then** adapters reference only Core, only Host references adapters, and Core references no adapter and no Octokit, Docker, EF Core, or ASP.NET Core package (AD-3)
**And** the test fails when a forbidden reference is added

**Given** any source file
**When** it produces a compiler warning or omits a nullable annotation
**Then** the build fails, because nullable reference types are on and warnings are errors

**Given** the repository
**When** its branches are inspected
**Then** `develop` is the default branch, `develop` and `main` are protected, neither accepts a direct push, and the documentation says how a feature, a release, and a hotfix reach each (AR-76)
**And** protecting the branches is done by the Owner, who alone administers the repository

**Given** a pull request against `develop` or `main`
**When** it is opened or updated
**Then** GitHub Actions builds the solution and runs every test, and reports the result on the pull request
**And** the build fails when line coverage over all code is below 80%, with exclusions kept in one file where each has a stated reason (NFR-18)
**And** these checks are required before a merge

**Given** the repository root
**When** it is listed
**Then** it holds an MIT `LICENSE`, a `README.md` in English, and the `deploy/`, `methodologies/`, and `docs/` directories of the spine's source tree (NFR-15, NFR-17, AR-62)
**And** `methodologies/default` holds the manifest of the built-in Methodology, so that `default` is a known Methodology from the first story that validates configuration

**Given** a pull request that adds or changes a dependency
**When** GitHub Actions runs
**Then** the build fails if any direct or transitive dependency carries a license outside the list of free and open-source licenses this story fixes (NFR-15)

### Story 1.4: Run Hephaestus with Docker Compose

As the Owner,
I want to start Hephaestus with one Compose command and talk to it from the command line,
So that I can run it on my own machine with nothing but Docker.

**Acceptance Criteria:**

**Given** Docker Engine 29 or later with Compose v2 and `deploy/compose.yaml`
**When** the Owner runs `docker compose up -d`
**Then** one long-running container named `hephaestus` starts and stays healthy
**And** no other server, paid service, or cloud service is required (FR-58, NFR-16)

**Given** the running container
**When** the Owner runs the version command through `docker exec hephaestus hephaestus`
**Then** the same executable, in client mode, calls the local HTTP API over a Unix domain socket inside the container and prints the Hephaestus version
**And** the client contains no logic other than calling the API and rendering its answer (AD-17)

**Given** the running container
**When** it is inspected
**Then** the control API listens on a Unix domain socket and on no network address
**And** the container publishes no port other than the dashboard port that Story 1.14 adds, bound to the host's loopback address

**Given** an API request that fails
**When** the CLI receives the answer
**Then** the answer is an RFC 9457 problem details document, the CLI prints its title and detail, and the CLI exits with a non-zero code

**Given** the running container
**When** it writes logs
**Then** each line is a JSON object on stdout written through `Microsoft.Extensions.Logging` (NFR-14)

**Given** `deploy/Dockerfile`
**When** the image is built for linux/amd64 and for linux/arm64
**Then** both builds succeed and each image starts

**Given** the version command, as the first command of the command line
**When** its form and output are compared with `EXPERIENCE.md`
**Then** it follows the command grammar and the output conventions fixed there: `--help`, `--json` printing one document and nothing else on standard output, errors on standard error, and exit codes 0, 1, and 2 (UX-DR-15, UX-DR-16)
**And** every later story that adds a command follows the same grammar and conventions and takes its command names from the command tree in `EXPERIENCE.md`

**Given** any sentence the command line or the API produces, in this story and in every later one
**When** it is written
**Then** it is a message id with parameters, resolved in Core from resources for English and Polish; no handler or adapter composes prose (AD-21)
**And** a test fails when a message id has no entry in a supported language

**Given** the version command run in Polish and in English
**When** the two outputs are compared
**Then** the sentences differ, and the command name, the options, the exit code, and the `--json` document are identical (FR-69, UX-DR-20)

### Story 1.5: Store secrets outside the database

As the Owner,
I want to hand Hephaestus a credential without it ever being written to the database, a log, or my shell history,
So that a copy of the database or a log file reveals nothing.

**Acceptance Criteria:**

**Given** a running installation
**When** the Owner sets a secret by name and supplies its value on standard input
**Then** the value is written as a file on the `hephaestus-secrets` volume, which is mounted only into the `hephaestus` container
**And** the database records only the secret's name
**And** a value passed as a command-line argument is refused
**And** an empty value, or one shorter than the minimum length this story fixes, is refused, because the masker would otherwise mask ordinary text

**Given** stored secrets
**When** the Owner lists them
**Then** only their names are shown

**Given** a stored secret
**When** the Owner removes it
**Then** its file and its name are both gone

**Given** a stored secret with a known value
**When** the database file, the container's logs, and every API response from the operations above are searched for that value
**Then** it is found in none of them (FR-4)

**Given** stored secrets
**When** the container is restarted
**Then** the secrets are still available

**Given** a first start with an empty `hephaestus-data` volume
**When** the container starts
**Then** it creates one SQLite database on the `hephaestus-data` volume in write-ahead-log mode with a busy timeout and applies the migrations of the single migration set (AR-35, AR-39)
**And** entity ids are UUID version 7 stored as text, and instants are stored in UTC as fixed-width ISO 8601 text through one value converter (AR-30, AR-32)

**Given** an API endpoint that reads stored data
**When** it is implemented, in this story and in every later one
**Then** it reads through `IReadStore`, which Persistence implements on a read-only SQLite connection, and never through `IUnitOfWork` (AD-17)

### Story 1.6: Mask secret values wherever text is written

As the Owner,
I want any stored secret value that appears in text Hephaestus writes to be replaced by a mask,
So that a credential echoed by a tool never reaches a log.

**Acceptance Criteria:**

**Given** a stored secret value
**When** a log line contains it in plain form, in base64, or URL-encoded
**Then** the line written to stdout contains a mask in its place (FR-4, AD-16)

**Given** text delivered as a stream of chunks
**When** a secret value is split across two chunks
**Then** the masked output contains no part of the value

**Given** a running installation
**When** a secret is added, changed, or removed
**Then** the masker is rebuilt and the new value is masked without a restart

**Given** the code base
**When** it is inspected by an architecture test
**Then** there is exactly one masker service, defined in Core and built from `ISecretStore`, and the logger writes through it (AR-36)

### Story 1.7: Connect Hephaestus's own GitHub identity

As the Owner,
I want Hephaestus to act on GitHub as its own GitHub App with the fewest permissions it needs,
So that I can approve the pull requests it opens and it can touch nothing beyond my registered repositories.

**Acceptance Criteria:**

**Given** a GitHub App the Owner has created, its App id, and its private key supplied on standard input
**When** the Owner runs the command that connects and checks the GitHub identity
**Then** Hephaestus authenticates as the App and reports the App's identity and the repositories it is installed on
**And** the private key is held as a secret under Story 1.5

**Given** the connected App
**When** its permissions are read
**Then** the command reports each required permission as present or missing: contents, issues, and pull requests read and write; metadata, checks, and statuses read
**And** it warns when `workflows` or administration is granted (AD-19, NFR-6)

**Given** GitHub is unreachable or the credential is rejected
**When** the command runs
**Then** it reports which of the two happened and stores nothing partial

**Given** the code base
**When** it is inspected by an architecture test
**Then** `Hephaestus.GitHub` is the only project that references Octokit or reads the GitHub App credential (AD-4)

**Given** the installation documentation
**When** an Owner follows it
**Then** it leads through creating the GitHub App with exactly the required permissions and explains why a separate identity is required (AR-57)

### Story 1.8: Manage Trusted Users from the command line

As the Owner,
I want to decide which GitHub users Hephaestus acts on,
So that labels, answers, and commands from anyone else change nothing.

**Acceptance Criteria:**

**Given** a connected GitHub identity
**When** the Owner adds a Trusted User by login
**Then** Hephaestus resolves the login to GitHub's numeric user id and stores the id; the login is kept for display only (AD-15)

**Given** a Trusted User who later changes their login
**When** the Owner lists Trusted Users
**Then** the user is still listed, identified by the same numeric id

**Given** a login that does not exist on GitHub
**When** the Owner adds it
**Then** the command fails and says the user was not found

**Given** Hephaestus's own GitHub App identity
**When** the Owner tries to add it as a Trusted User
**Then** the command refuses (AD-15)

**Given** a stored Trusted User
**When** the Owner removes them and lists again
**Then** they are no longer listed

**Given** stored Trusted Users
**When** the container is restarted
**Then** the list is unchanged (FR-3)

### Story 1.9: Define Aliases, limits, and protected paths

As the Owner,
I want to name the Agent and Model combinations and the Effort levels I use, and to set the ceilings no repository can raise,
So that I can choose per Task with a label and stay in control of resources.

**Acceptance Criteria:**

**Given** a running installation
**When** the Owner adds an Alias that names an Agent and a Model
**Then** the Alias is stored and listed, and no change to Hephaestus is needed to make a new Model selectable (FR-7)
**And** an Alias naming an Agent other than Claude Code or Codex CLI is refused

**Given** a running installation
**When** the Owner adds an Alias for an Effort
**Then** it must map to a value of Core's closed Effort set, and any other value is refused (AD-10)

**Given** a running installation
**When** the Owner sets the installation limits for Task duration, processor, and memory
**Then** the limits are stored and shown, and they are the ceilings Project Configuration is checked against

**Given** a new installation
**When** the Owner shows the protected paths
**Then** the list is `.github/**` and `hephaestus.yml`
**And** the Owner can change the list (AD-19)

**Given** stored Installation Configuration
**When** the Owner runs the command that shows it
**Then** Trusted Users, Aliases, limits, protected paths, and secret names are shown, and no secret value is (FR-3)

**Given** any of the changes above
**When** it is made
**Then** the API handler writes it in its own transaction and it survives a restart (AD-16)

**Given** a running installation
**When** the Owner sets the installation's language to `en` or `pl`
**Then** it is stored, shown with the rest of the Installation Configuration, and used by the command line from the next command; any other value is refused; a new installation is in English (FR-69)

### Story 1.10: Register a Project

As the Owner,
I want to register a GitHub repository as a Project with its Base Branch,
So that Hephaestus works on that repository and on no other.

**Acceptance Criteria:**

**Given** a repository on which the GitHub App is installed
**When** the Owner registers it and names its Base Branch
**Then** the Project is stored with GitHub's repository id as its identity, its name for display, and its Base Branch (FR-1)
**And** the command reports whether the Base Branch is protected against direct pushes and recommends protection when it is not (AD-19)

**Given** a repository on which the GitHub App is not installed, or a Base Branch that does not exist
**When** the Owner registers it
**Then** the command fails and says which of the two is wrong

**Given** a registered Project whose repository is later renamed
**When** the Owner lists Projects
**Then** the Project is still listed, identified by the same repository id

**Given** a registered Project
**When** the Owner removes it
**Then** it no longer appears among the Projects Hephaestus works on, and its record and everything recorded under it are retained

**Given** registered Projects
**When** the container is restarted
**Then** the list is unchanged

### Story 1.11: Check a Project's configuration file

As the Owner,
I want Hephaestus to read `hephaestus.yml` from the Base Branch and tell me exactly what is wrong with it,
So that I fix the configuration once and know no Task will run under rules I did not merge.

**Acceptance Criteria:**

**Given** a registered Project with a valid `hephaestus.yml` at the root of its Base Branch
**When** the Owner runs the command that checks the Project
**Then** it reports the configuration as valid and names the commit it was read from (FR-2)

**Given** a Project whose Base Branch has no `hephaestus.yml`
**When** the check runs
**Then** it reports the file as missing

**Given** a file with several invalid settings
**When** the check runs
**Then** every invalid setting is named with the reason it is invalid

**Given** a file that differs between the Base Branch and another branch
**When** the check runs
**Then** only the Base Branch's content is read; the gateway returns the raw bytes and their commit id, and Core parses and validates them (AD-16)

**Given** a configuration file
**When** it is validated
**Then** it may set the default Primary and Fallback Aliases, the default Effort, the Methodology, the Validation commands, the Correction Loop bound, the image, resource limits, allowed egress hosts, and the language (`en` or `pl`), with camelCase keys
**And** any other key is an error

**Given** a file that names an unknown Alias, default Primary and Fallback Aliases that resolve to the same Agent, a Methodology that is not available, or a resource limit above the installation's
**When** it is validated
**Then** each is reported as an error (FR-3, FR-7, FR-61)

**Given** a file that names no Validation command
**When** it is validated
**Then** it is reported as an error, because Validation with no command would pass without checking anything; a Project with nothing to build states a command that says so (owner decision, 2026-11-25; FR-31)

**Given** the documentation
**When** an Owner reads the `hephaestus.yml` reference
**Then** it describes every key and gives a complete example for at least two different technology stacks, each using a stock image (FR-5)

**Given** a file that sets `language`
**When** it is validated and, later, a Task of that Project starts
**Then** a value other than `en` or `pl` is an error, the language is snapshotted onto the Task with the rest of the configuration, and a file without the key uses the installation's language (AD-21)

### Story 1.12: Upgrade without losing configuration or history

As the Owner,
I want an upgrade either to carry all my stored data over or to stop and tell me why,
So that I never end up with a half-converted installation.

**Acceptance Criteria:**

**Given** an installation with stored data and a newer Hephaestus image that carries new migrations
**When** the container starts
**Then** it first copies the database with SQLite's backup operation to the data volume, then applies the migrations, then starts serving
**And** all Installation Configuration stored before the upgrade is present afterwards (FR-60)

**Given** a migration that fails
**When** the container starts
**Then** startup stops, the reason is written to the log and returned by the CLI, and the pre-migration copy is left intact
**And** the database is not left partly migrated

**Given** a database written by a newer Hephaestus version than the image being started
**When** the container starts
**Then** startup stops and says the database is newer than this version

**Given** an upgrade that completed
**When** the Owner looks at the data volume
**Then** the pre-migration copy is there, named with the version it was taken from

### Story 1.13: Publish versioned releases

As someone installing Hephaestus,
I want every release to carry a Semantic Version, published images, and a changelog entry, and a preview image of what is coming,
So that I know what I am running and what changed.

**Acceptance Criteria:**

**Given** a pull request merged to `develop`
**When** the workflow runs
**Then** the `hephaestus` image is built for linux/amd64 and linux/arm64 and published to GHCR as `develop` and as `develop-<short commit id>` (AR-77, AR-38)
**And** the documentation calls these a preview channel that is not a release and not part of the versioned public surface

**Given** a pull request to `main`
**When** its checks run
**Then** it fails unless the version the repository states is a valid three-part Semantic Version, is higher than the last version published, and has an entry in `CHANGELOG.md` that groups its changes as added, changed, fixed, removed, and security (FR-59)

**Given** a version with a fourth numeric part
**When** the check runs
**Then** it fails

**Given** a pull request merged to `main`
**When** the workflow runs
**Then** it creates the git tag `v<version>` and publishes the image under that version

**Given** a version with a pre-release identifier, such as `0.1.0-alpha.2` or `0.1.0-rc.1`
**When** it is published
**Then** it gets its own tag and the stable tags do not move

**Given** a failure after the tag was created and before the image was published
**When** the workflow is run again
**Then** it completes the publication and creates nothing twice

**Given** a 0.x minor release that contains a breaking change to the public surface
**When** its changelog entry is read
**Then** the entry says so

**Given** a running container started from a published image
**When** the Owner asks for the version
**Then** the answer is the version of the tag the image was built from, or `develop` with the commit id for a preview image

**Given** the documentation
**When** it describes versioning
**Then** it names the versioned public surface: the configuration format, the label and comment-command conventions, the command-line interface, and the release image tags
**And** it says that publishing an image deploys nothing

### Story 1.14: Open the Dashboard on my own machine

As the Owner,
I want to open Hephaestus in a browser on the machine that runs it, with a token only I hold,
So that I can look at the forge without it becoming reachable by anyone else.

**Acceptance Criteria:**

**Given** a running installation
**When** the Owner issues a Dashboard Token by name from the command line
**Then** its value is printed once, alone on standard output, only its hash is stored, and the token is listed by name and creation time (FR-68, UX-DR-16)
**And** the Owner can revoke it by name

**Given** a valid token
**When** the Owner opens `http://127.0.0.1:8717` and supplies it on Unlock
**Then** the Dashboard loads and shows the Hephaestus version on Overview
**And** the token is sent in a request header, kept in session storage, and never placed in an address; Lock forgets it (UX-DR-7)

**Given** no token, a wrong token, or a revoked token
**When** any read endpoint is requested
**Then** no data is returned
**And** Unlock says "That token is not valid." without revealing whether a token of that name exists; a token revoked while in use returns the Owner to Unlock with the command that issues a new one

**Given** the running container
**When** its published ports and networks are inspected
**Then** the dashboard port is bound to the host's loopback address only, a request from another machine fails, and the `hephaestus` container shares no Docker network with any other container (AD-17)

**Given** a request whose host name is not one the dashboard listener was configured with
**When** it arrives
**Then** it is refused (T35)

**Given** the dashboard listener
**When** its endpoints are listed by an architecture test
**Then** every one is a read under `/api` or a static file, and none appends a Signal, changes configuration, issues or revokes a token, or touches a secret
**And** every changing request the control API accepts is refused on the dashboard listener (AR-73)

**Given** the Dashboard loaded in a browser with the network observed
**When** it starts
**Then** it requests nothing from any other origin and sets no cookie, and the response carries a content security policy that allows no other origin and no inline script
**And** a lint rule fails the build when a view bypasses the framework's escaping (T36)

**Given** the token value
**When** the database and the container's logs are searched for it
**Then** it is found in neither

**Given** `deploy/Dockerfile`
**When** the image is built for linux/amd64 and linux/arm64
**Then** the Angular workspace in `src/Hephaestus.Dashboard` is built in a Node.js stage with the versions the spine's Stack pins, and the runtime image contains its static files and no Node.js (AR-72)

**Given** a pull request
**When** GitHub Actions runs
**Then** the Angular workspace is built and its unit tests run, and the license check of Story 1.3 covers its npm packages (NFR-15)

**Given** the Dashboard's frame
**When** it is compared with `DESIGN.md` and `EXPERIENCE.md`
**Then** it has the navigation rail, the top bar with page name, theme switch, and Lock, both themes built from the design tokens with ember in the primary role, system font stacks, and bundled icons (UX-DR-1, UX-DR-2, UX-DR-4, UX-DR-5, UX-DR-6)
**And** it meets the accessibility floor and the responsive behaviour fixed there (UX-DR-12, UX-DR-13)

**Given** this story
**When** it is delivered
**Then** a decision record states how the Dashboard's types stay in step with the read API, and every later dashboard story follows it (AR-72)

**Given** the installation documentation
**When** an Owner reads it
**Then** it explains how to issue a token and open the Dashboard, and says that the Dashboard is local, read-only, and not encrypted in transit (NFR-8)

**Given** the Dashboard
**When** the Owner uses the language switch in the top bar
**Then** every label, hint, tooltip, and empty state changes between English and Polish, the choice is remembered in that browser, and nothing is requested from another origin (FR-69, UX-DR-20)
**And** state names, ids, commands shown for copying, and text from GitHub or an Agent do not change
**And** a build fails when a Dashboard text has no Polish entry, and the way the two languages are held is recorded in this story's decision record

**Given** a pull request
**When** the Angular workspace's tests run
**Then** the build fails when its line coverage is below 80% (NFR-18)

### Story 1.15: See Projects and configuration in the Dashboard

As the Owner,
I want to see my registered Projects and how the installation is configured,
So that I can check a setup without typing commands.

**Acceptance Criteria:**

**Given** registered Projects
**When** the Owner opens Projects
**Then** each is listed with its name, its Base Branch, and whether the Base Branch is protected (FR-67)
**And** a removed Project is listed last, marked removed

**Given** a Project
**When** the Owner opens its Configuration tab
**Then** the Dashboard shows what the command that checks the Project shows: the settings read from `hephaestus.yml` on the Base Branch with the commit they were read from, or the file as missing, or each invalid setting with its reason

**Given** a Project whose configuration is invalid or missing
**When** its Configuration tab is shown
**Then** an act-elsewhere hint says to fix `hephaestus.yml` on the Base Branch and links to it; the Dashboard offers no way to change it (UX-DR-9)

**Given** the Installation Configuration
**When** the Owner opens Installation
**Then** Trusted Users, Aliases, limits, and protected paths are shown, and secrets are shown by name only (FR-4)

**Given** any value on these views
**When** it is compared with the command line's output for the same record
**Then** the two agree, and a value that does not exist reads "None" (UX-DR-10)

**Given** an installation with no Project
**When** the Owner opens Projects
**Then** an empty state says what would appear and shows the command that registers a Project, in `mono`, with a copy button (UX-DR-8, UX-DR-9)

**Given** a first load, or Hephaestus not answering
**When** a view is shown
**Then** it shows skeleton rows while loading, and when Hephaestus does not answer it keeps and dims what it showed, with the time it was read (UX-DR-10)

### Story 1.16: Hold every pull request to the quality gate

As the Owner,
I want every pull request analysed and held to a quality gate,
So that neither a person nor an Agent can lower the quality of Hephaestus's own code unnoticed.

**Acceptance Criteria:**

**Given** that the SonarQube Cloud organisation and project belong to the Owner's account
**When** this story is planned and run
**Then** the Owner binds the project to the repository and switches on secretless analysis for GitHub Actions in the organisation's settings, and the story is not closed by a dev agent alone

**Given** a pull request from a branch of this repository, against `develop` or `main`
**When** it is opened or updated
**Then** SonarQube Cloud analyses it, with the coverage of the .NET solution and, once Story 1.14 exists, of the Angular workspace, and reports the result on the pull request (AR-76)

**Given** an analysed pull request
**When** its quality gate is evaluated
**Then** it fails when coverage on new code is below 80% or another condition of the built-in gate is not met, and a passing gate is required before a merge (NFR-18)

**Given** a merge to `develop` or `main`
**When** the workflow runs
**Then** that branch is analysed

**Given** the workflow
**When** it is inspected
**Then** it stores no SonarQube token: it obtains a short-lived credential by secretless analysis immediately before the scanner needs it, its job permissions are `id-token: write` and `contents: read` and nothing else, and the repository holds no `SONAR_TOKEN` secret (T39)
**And** the scanner and action versions are pinned

**Given** a pull request from a fork
**When** it is opened
**Then** it cannot obtain the credential and no analysis runs; the build's own coverage threshold and tests still apply, and the documentation says so

**Given** a pull request opened by Hephaestus from a `hephaestus/*` branch
**When** it is opened
**Then** it is held to the same checks as any other

**Given** the documentation and the threat model
**When** they are read
**Then** they state that SonarQube Cloud is used to build Hephaestus and is not needed to run it (NFR-15), and they record the accepted risk of T39 (NFR-8)

## Epic 2: Opt issues in and converse on GitHub

A Trusted User applies the Marker Label and sees the Task's state on the issue. Role Labels, the Priority Label, and `Depends-On` lines are honored; labels and comments from anyone else are recorded and ignored. Hephaestus can ask a specific question on the issue, wait, and resume the Task when a Trusted User answers.

### Story 2.1: Notice what happens on registered Projects

As the Owner,
I want Hephaestus to notice every label, comment, and close on my registered Projects without a public endpoint,
So that nothing I do on GitHub is missed and my installation works behind a home router.

**Acceptance Criteria:**

**Given** a registered Project
**When** an issue in it is labelled, unlabelled, closed, or reopened, or a comment on it is created or edited
**Then** within one polling cycle exactly one Signal for that event is appended to the persisted inbox, carrying its source, its source event id, and the acting user's numeric id taken from GitHub's own event data (AD-2, AD-15)

**Given** an event that was already appended
**When** the poller sees it again
**Then** the second append is a no-op

**Given** a crash after a Signal was appended and before the poll cursor advanced
**When** Hephaestus restarts
**Then** the event is neither lost nor appended twice, because the poller appends first and advances its cursor second (AD-1)

**Given** nothing changed on a Project
**When** the poller polls it
**Then** it sends conditional requests, a `304 Not Modified` leaves the cursor unchanged, and any minimum poll interval GitHub returns is respected (AR-43)

**Given** a repository that is not registered, or a Project that was removed
**When** the poller runs
**Then** it never reads that repository (FR-1)

**Given** GitHub is unreachable
**When** the poller runs
**Then** it tries again on a later cycle with its cursors intact (NFR-12)

**Given** GitHub answers with a rate-limit response
**When** the poller or the gateway receives it
**Then** it waits until the time GitHub states before the next request, treats the wait as temporary unavailability, and loses no event

**Given** the Installation Configuration
**When** the polling interval is read
**Then** it is a setting with a documented default, and it is the polling cycle that FR-11 and FR-29 measure against

**Given** the code base
**When** it is inspected by an architecture test
**Then** the poller does nothing except append Signals and keep cursors through `IPollCursorStore`; it decides no trust and changes no Task (AD-4)

### Story 2.2: Opt an issue in with the Marker Label

As the Owner,
I want an issue to become a Task only when a Trusted User applies the Marker Label,
So that nobody else can make Hephaestus work on my repository.

**Acceptance Criteria:**

**Given** a registered Project and a Trusted User
**When** that user applies the `hephaestus` label to an issue
**Then** the orchestrator loop applies the Signal in one transaction and creates a Task in `Queued` for that Project and issue number
**And** the issue's title and body are snapshotted with their hash as vouched for by that event
**And** the branch name `hephaestus/<issue-number>-<slug>` is derived once and stored on the Task (FR-6)
**And** the slug holds only lower-case ASCII letters, digits, and hyphens and has a bounded length; a title that yields none of these gives the branch name `hephaestus/<issue-number>`

**Given** a user who is not a Trusted User
**When** they apply the Marker Label
**Then** no Task is created and the Signal is recorded as rejected with the acting user's id

**Given** an issue that carries the Marker Label with no labelled event from a Trusted User behind it
**When** the loop runs
**Then** no Task exists for it, because intent is read from actor-attributed events and never from the current label set (AD-2)

**Given** an event whose actor is Hephaestus's own GitHub identity
**When** the loop applies it
**Then** it creates and changes no Task (AD-15)

**Given** the code base
**When** it is inspected by an architecture test
**Then** nothing but the orchestrator loop depends on `IUnitOfWork` (AD-1)

**Given** a running container
**When** the Owner asks for its health
**Then** the API reports that the loop is alive and when its last tick ran (AR-42)

### Story 2.3: Withdraw, close, or opt in again

As the Owner,
I want removing the Marker Label or closing the issue to take a Task out of the Queue, and applying the label again to bring the same Task back,
So that I can change my mind on GitHub without leaving stray Tasks behind.

**Acceptance Criteria:**

**Given** a `Queued` Task
**When** a Trusted User removes the Marker Label
**Then** the Task becomes `Withdrawn` (FR-6)
**And** removal by anyone else changes nothing

**Given** a `Withdrawn` Task
**When** a Trusted User applies the Marker Label again
**Then** the same Task returns to `Queued` with a fresh snapshot; no second Task is created for the issue (AD-2)

**Given** a `Queued` Task
**When** a Trusted User applies the Marker Label to its issue again
**Then** nothing changes and no second Task is created

**Given** a Task that is not in a final state
**When** a Trusted User closes its issue
**Then** the Task becomes `Closed`
**And** a close by anyone else changes nothing

**Given** a `Closed` Task
**When** its issue is reopened or the Marker Label is applied again
**Then** the Task stays `Closed`, because the lifecycle table makes `Closed` final, and the Signal is recorded as rejected with that reason

**Given** a label applied and removed by a Trusted User in quick succession
**When** the loop applies the two Signals
**Then** it applies them in inbox order and the Task ends `Withdrawn` (AD-1)

### Story 2.4: See a Task's state on its issue

As the Owner,
I want each Task's current state shown as a label on its issue,
So that I can follow and filter my Tasks with GitHub alone.

**Acceptance Criteria:**

**Given** a Task that enters a state
**When** the next polling cycle completes
**Then** its issue carries exactly one label of the form `hephaestus:<state>` in kebab-case, matching that state (FR-11)
**And** GitHub's issue filter on that label lists every Task in that state

**Given** a state label that someone removes or replaces by hand
**When** the loop next reconciles the Task
**Then** it writes the correct label back (AD-2)

**Given** any label change Hephaestus makes
**When** the journal is read
**Then** it holds an intent with an idempotency key of the form `<scope>:<cause>:<effect-kind>:<ordinal>`, committed in the same transaction as the state change, and a result recorded after the change was made
**And** the port method that made the change received the key as a parameter (AD-5)

**Given** a crash after an intent was committed and before its result was recorded
**When** Hephaestus restarts
**Then** the loop's first tick resolves the intent by reading the issue's label set, and the label is applied exactly once (NFR-9)

**Given** GitHub is unreachable when a label change is attempted
**When** the loop continues
**Then** the change is retried at a recorded next-attempt time within a configured bound while the Task keeps its state
**And** at the bound the Task moves to `WaitingForOwner`, not `Failed` (AD-5, NFR-12)

**Given** an effect that GitHub refuses for good, for example because the issue was deleted or the App's permission was withdrawn
**When** the refusal is received
**Then** the effect is not retried, the journal records the refusal with its reason, the Task moves to `WaitingForOwner`, and an error is logged with the Task's id (owner decision, 2026-11-25)
**And** the documentation says that in v0.1 such a Task is visible only in the log when its issue can no longer be written to

**Given** the journal
**When** it is inspected
**Then** it is append-only, only the loop writes it, and every entry names what caused it (NFR-7)

### Story 2.5: Say on the issue what was ignored and why

As the Owner,
I want Hephaestus to state on the issue when it ignored someone's action,
So that an ignored label or command is never a silent mystery.

**Acceptance Criteria:**

**Given** a user who is not a Trusted User
**When** they apply the Marker Label or another label Hephaestus reads on an issue of a registered Project
**Then** a comment on the issue says the action was ignored because that user is not a Trusted User
**And** no Task is created or changed (AD-15)

**Given** the same user repeating the same kind of label action on the same issue
**When** the Signals are applied
**Then** one comment is posted, not one per attempt (NFR-10)

**Given** a user who is not a Trusted User
**When** they post a comment or a `/hephaestus` command on an issue or pull request of a registered Project
**Then** the Signal is recorded as rejected with the acting user's id, no Task changes, and Hephaestus posts no reply, because anyone can comment on a public repository and a reply per account would let outsiders fill the thread (owner decision, 2026-11-25; AD-15)

**Given** any comment Hephaestus posts
**When** its source is read
**Then** its last line is the hidden marker `<!-- hephaestus:effect=<idempotency-key> -->` and the journal holds its intent and result (AD-5)

**Given** a crash after a comment was posted and before its result was recorded
**When** Hephaestus restarts
**Then** the comment is found by its marker and is not posted again
**And** a marker counts only when the comment was written by Hephaestus's own identity and matches a journaled intent; a marker written by anyone else is disregarded (AD-19)

**Given** a comment body that contains a stored secret value
**When** the gateway sends it
**Then** the value is masked before the request leaves Hephaestus (FR-4)

**Given** recorded GitHub events from untrusted actors, an event from a Trusted User whose login was renamed, and a comment carrying a forged hidden marker
**When** the test suite replays them
**Then** the untrusted events and the forged marker change no Task, and the renamed user is still recognized by numeric id (AR-51)

**Given** any comment Hephaestus posts, in this story and in every later one
**When** it is read
**Then** it has the anatomy fixed in `EXPERIENCE.md`: a bold headline saying what happened, at most five lines or one small table of facts, a line starting "Next:" where something follows, and the hidden marker last (UX-DR-17)

**Given** a comment Hephaestus posts on an issue of a Project
**When** its fixed text is written
**Then** it comes from the resources in the Task's language, or the Project's when no Task exists; labels, the command syntax it mentions, and the hidden marker are the same in every language (AD-21, UX-DR-20)

### Story 2.6: Choose Agents and Effort per Task with Role Labels

As the Owner,
I want to pick the Primary Agent, the Fallback Agent, and the Effort for one Task with labels,
So that I can match the Agent to the work without touching configuration.

**Acceptance Criteria:**

**Given** a Task that carries only the Marker Label
**When** its selection is resolved
**Then** it uses the Project's default Aliases and Effort (FR-7)

**Given** a Trusted User
**When** they apply `primary:<alias>`, `fallback:<alias>`, or `effort:<alias>` to a Task's issue
**Then** the Task's selection uses the Alias named for that role
**And** a Role Label applied by anyone else is ignored and reported under Story 2.5

**Given** a Role Label that names an unknown Alias, two Role Labels for the same role, or `primary` and `fallback` that resolve to the same Agent
**When** the Task's selection is resolved
**Then** the Task stays `Queued` and is passed over, and a comment on the issue names the problem (AD-13)

**Given** such a Task
**When** a Trusted User corrects the labels
**Then** the Task becomes eligible with no further action

**Given** an Alias added to Installation Configuration after an issue was labelled with it
**When** the Task's selection is resolved again
**Then** the label is valid, with no change to Hephaestus

**Given** the code base
**When** it is inspected
**Then** Aliases are resolved in Core and nowhere else (AD-10)

### Story 2.7: Work Tasks in a predictable order

As the Owner,
I want priority Tasks worked first and the rest in issue order,
So that I know what Hephaestus will pick up next.

**Acceptance Criteria:**

**Given** this story
**When** it is delivered
**Then** the spelling of the Priority Label is fixed and added to the documented label conventions (AR-68)

**Given** two eligible `Queued` Tasks of one Project, one of which carries the Priority Label applied by a Trusted User
**When** the dispatch-order policy is evaluated
**Then** the Task with the Priority Label comes first (FR-8)

**Given** two eligible Tasks without a Priority Label
**When** the policy is evaluated
**Then** the lower issue number comes first

**Given** a Priority Label applied by someone who is not a Trusted User
**When** the policy is evaluated
**Then** the label has no effect on the order

**Given** a restart
**When** the policy is evaluated afterwards
**Then** it gives the same order, because the order is computed by one Core policy from recorded state and no queue is held in memory (AD-1)

### Story 2.8: Declare that a Task depends on another

As the Owner,
I want to state in an issue that its Task depends on another Task,
So that Hephaestus never starts work on code that is not there yet.

**Acceptance Criteria:**

**Given** an admitted issue snapshot whose body contains lines of the form `Depends-On: #<issue-number>`
**When** Core parses it
**Then** each line is recorded as a dependency on that issue of the same Project (FR-10)

**Given** a Task with a dependency that is not `Merged`
**When** the dispatch-order policy is evaluated
**Then** the Task stays `Queued` and is passed over, and other eligible Tasks are not held back (FR-8)

**Given** such a Task
**When** every Task it depends on becomes `Merged`
**Then** it becomes eligible with no action from the Owner

**Given** a dependency that names an issue that is not a Task, a Task that is `Closed`, `Failed`, `Cancelled`, or `Withdrawn`, or a Task that depends back on this one
**When** the loop evaluates the Task
**Then** a comment on the dependent Task's issue names the dependency and why it cannot be satisfied now
**And** the Task stays `Queued` and is never dispatched
**And** the comment is posted once per distinct reason, and the Task becomes eligible by itself if the dependency later becomes `Merged` (AR-68)

**Given** an issue body edited so that a `Depends-On` line is added or removed, where the edit has not been admitted
**When** dependencies are read
**Then** only the admitted snapshot counts

### Story 2.9: Ask the Owner a question and move on

As the Owner,
I want Hephaestus to ask me a specific question on the issue when it cannot proceed, and to keep working on other Tasks,
So that one open decision never stalls everything.

**Acceptance Criteria:**

**Given** a Task and a decision Hephaestus cannot make on its own
**When** an Escalation is raised through Core's single escalation path
**Then** a comment on the issue names the decision needed and the options Hephaestus sees
**And** the Task moves to `WaitingForOwner` with its resume condition recorded, and the Escalation is recorded on the Task (FR-48)

**Given** an Escalation
**When** it is raised
**Then** the `INotifier` port is called; in v0.1 its implementation in Host does nothing, and the Task's outcome is the same either way (AR-63)

**Given** one Task in `WaitingForOwner`
**When** the dispatch-order policy is evaluated
**Then** other eligible Tasks are not held back by it

**Given** question text that came from an Agent
**When** it is posted
**Then** HTML comments in it are escaped, mentions and issue-closing keywords are broken, and command keywords are fenced (AD-19)

**Given** a Task whose outside effect reached its retry bound under Story 2.4
**When** it moves to `WaitingForOwner`
**Then** it does so through the same path, and the comment says which effect could not be completed

**Given** a Task in `WaitingForOwner`
**When** a Trusted User removes the Marker Label or closes the issue
**Then** the Task becomes `Cancelled` or `Closed` as the lifecycle table states

**Given** an Escalation comment
**When** it is read
**Then** it has the shape fixed in `EXPERIENCE.md`: "Hephaestus needs a decision.", the decision in one sentence, the options lettered one per line, and how to answer; one Escalation asks for one decision (UX-DR-18)

### Story 2.10: Answer on the issue to resume the Task

As the Owner,
I want my reply on the issue to put the Task back in the Queue with my answer attached,
So that I can unblock work from my phone.

**Acceptance Criteria:**

**Given** a Task in `WaitingForOwner` with an open Escalation
**When** a Trusted User comments on the issue
**Then** the comment is admitted and recorded as the answer, and the Task returns to `Queued` with the answer among its admitted comments (FR-49)

**Given** the same Task
**When** someone who is not a Trusted User comments
**Then** the comment is not admitted, the Task stays `WaitingForOwner`, and the attempt is recorded without a reply, as Story 2.5 states

**Given** a comment written by Hephaestus's own identity
**When** comments are admitted
**Then** it is never admitted (AD-15)

**Given** a comment by a Trusted User on the issue of a Task that is not waiting
**When** it is read
**Then** it is admitted as context for later steps and changes no state (NFR-1)

**Given** a comment by a Trusted User whose first line begins with `/hephaestus`
**When** it is read
**Then** it is handled as a command and is not treated as an answer

### Story 2.11: Hold edited content until a Trusted User confirms it

As the Owner,
I want a title, body, or comment that someone else edited to stay out of an Agent's context until I confirm it,
So that nobody can slip instructions into a Task I vouched for.

**Acceptance Criteria:**

**Given** an admitted title, body, or comment with its recorded hash
**When** the poller reports the item as updated and the content is read again
**Then** a hash that differs from the recorded one marks the content as edited, whether or not an edit event was seen (AD-15)

**Given** edited content
**When** GitHub's edit history shows that every editor since admission is a Trusted User
**Then** the new content is admitted and its hash recorded

**Given** edited content
**When** an editor is not a Trusted User, or the editor cannot be established
**Then** the new content is not used, the Task is held in `WaitingForOwner`, and a comment explains that a Trusted User must confirm the edit (NFR-1)

**Given** a held Task
**When** a Trusted User posts a comment on the issue whose first line is `/hephaestus confirm`
**Then** the held content is admitted and the Task returns to `Queued`

**Given** a comment whose first line is `/hephaestus <verb>`
**When** it is parsed
**Then** one parser in Core handles it against a closed verb set, which in v0.1 contains `confirm`
**And** an unknown verb is reported on the issue and changes nothing
**And** a command from someone who is not a Trusted User is ignored and recorded without a reply, as Story 2.5 states

**Given** a GitHub App installation token
**When** a test asks GraphQL `userContentEdits` for the editors of an issue body and of a comment
**Then** the result is recorded in the documentation
**And** if editors are not returned, every detected edit is held for confirmation (AR-7)

### Story 2.12: See Tasks and their states in the Dashboard

As the Owner,
I want each Project's Tasks listed with their current state, and the list to change by itself,
So that I see the whole Queue in one place while I work on GitHub.

**Acceptance Criteria:**

**Given** a Project with Tasks
**When** the Owner opens its Tasks tab
**Then** each Task is listed with issue number, title, state chip, the Primary and Fallback Alias and the Effort selected, its declared dependencies, the time of its last change, and a link to the issue (FR-67)
**And** the list can be filtered by state, and the filter lives in the address (UX-DR-6)

**Given** a Task state shown anywhere in the Dashboard
**When** it is rendered
**Then** it is the state chip: the state's colour token, an icon, and the state's name as in the `hephaestus:<state>` label, with a tooltip that says what the state means and what ends it (UX-DR-3)

**Given** a Task that is passed over
**When** it is listed
**Then** the reason is shown under its title: an unsatisfied dependency or a label problem

**Given** a Task in `waiting-for-owner`
**When** it is listed or shown on Overview under "Needs you"
**Then** the first line of its question is shown with a link to the issue and the hint that it is answered there; the Dashboard has no field for an answer (UX-DR-9)

**Given** Overview
**When** the Owner opens it
**Then** it shows whether the loop is alive and when its last tick ran, the Tasks that need the Owner, and a count of Tasks per state that links to the filtered list
**And** when the loop has not ticked, the loop line says since when and points to the container log (UX-DR-10)

**Given** the Dashboard open on a Project
**When** the loop commits a transaction that changes one of its Tasks
**Then** the change stream sends one event naming the scope and the id, and the new state is on screen without a reload, with the row updated in place and the scroll position unmoved (AR-74, UX-DR-11)

**Given** the change stream
**When** its events are inspected
**Then** they carry a scope and an id only, and no title, body, or output

**Given** a stream that was cut, or a Hephaestus restart
**When** the Dashboard reconnects
**Then** it reads everything on screen again, so a missed event leaves nothing stale
**And** the live indicator reads "Reconnecting" while the stream is closed and "Live" when it is open

**Given** a stream request without a valid token, or one beyond the bound on open streams
**When** it arrives
**Then** it is refused; and revoking a token closes the streams opened with it (T38, FR-68)

**Given** the loop, or a control API handler that changed Installation Configuration
**When** it announces a change
**Then** it does so after its commit, through `IChangeFeed`, and writes nothing to the journal for it (AD-17)

**Given** an issue title or body that contains markup or script
**When** it is shown
**Then** it is rendered as text (AR-73, T36)

## Epic 3: From issue to validated pull request

The Owner starts a Project on demand and gets a pull request. One Task runs at a time: an Agent implements the change in a hardened step container that holds no GitHub credential, the work is checkpointed and pushed to the Task branch, the Project's Validation commands decide whether it is acceptable, and a restart at any point loses nothing and duplicates nothing. Story 3.16 closes the proof-of-concept path defined after the epic list.

Stories 3.1 to 3.13 each deliver one capability with its own tests, on a shared end-to-end harness that Story 3.3 starts; Story 3.14 joins them into the first run from issue to pull request; Stories 3.15 to 3.21 make that run safe to leave alone.

### Story 3.1: Pin, fetch, and verify the Agent CLIs

As the Owner,
I want Hephaestus to bring exactly the Agent CLI versions it was tested with, verified, from the vendors themselves,
So that my Project images need no Agent installed and an adapter never meets a CLI it does not understand.

**Acceptance Criteria:**

**Given** a Hephaestus release
**When** `deploy/agents.lock` is read
**Then** it pins one version of each CLI and the SHA-256 of every executable: Claude Code's glibc and musl builds per architecture, and Codex CLI's `codex` and `codex-code-mode-host` per architecture; where the vendor publishes an archive, the archive's digest and each extracted executable's (AD-8)

**Given** a first start with an empty `hephaestus-agents` volume
**When** the container starts
**Then** it downloads the pinned binaries from each vendor's official channel, verifies each checksum, and places them in the volume before the loop's first tick (AR-40)

**Given** a download whose checksum does not match
**When** the container starts
**Then** startup stops and says which binary failed, and nothing unverified is left in the volume

**Given** a volume that already holds the pinned, verified binaries
**When** the container starts
**Then** nothing is downloaded

**Given** the published Hephaestus images
**When** their contents are inspected
**Then** they contain no Agent binary

**Given** the outcome of Story 1.1
**When** this story is implemented
**Then** it follows injection through the volume, which that story confirmed, starting from `docs/spikes/injection/scripts/agents.lock`; no Hephaestus base image is built

**Given** a start
**When** the agent binaries volume has been filled
**Then** startup stops, naming the file, unless every executable the lock names is present and matches its digest

**Given** a start
**When** the volume is prepared
**Then** it holds a CA certificate bundle at `certs/ca-certificates.crt`, copied from the `hephaestus` image's own certificate store and replaced at every start

**Given** a pull request that changes `deploy/agents.lock`
**When** GitHub Actions runs
**Then** the build fails unless each new digest matches what the vendor publishes and the vendor's signature over that release verifies

**Given** the pinned executables for linux/arm64
**When** each is started in a hardened container on an arm64 runner
**Then** it prints its version; Story 1.1 ran linux/amd64 only

**Given** the documentation for contributors
**When** it describes changing a pin
**Then** it says that the matrix and planted-configuration runs of `docs/spikes/injection` are repeated with the Owner's credentials, and that such a change is not closed by a dev agent alone

### Story 3.2: Run every step in a hardened container

As the Owner,
I want each step of a Task to run in a container that cannot touch my files, other Projects, or Docker itself,
So that containment does not depend on an Agent behaving.

**Acceptance Criteria:**

**Given** a step to run
**When** `IWorkspaceRuntime` starts it
**Then** the call returns as soon as the container is created; it does not wait for the step to finish (AD-18)

**Given** a created step container
**When** it is inspected
**Then** it runs as user `10001:10001` with all capabilities dropped, `no-new-privileges`, the default seccomp profile, a process-count limit, no published port, and processor and memory limits taken from the Task's settings within the installation's
**And** its mounts are exactly the Task volume at `/workspace`, read-only context, Methodology, and agent binaries, and a temporary `HOME`, a tmpfs, at `/hephaestus/home` (AD-7)
**And** it carries the labels `hephaestus.installation`, `hephaestus.task`, and `hephaestus.step-run`, and its name starts with `hephaestus-`

**Given** a process inside a step container
**When** it tries to reach the Docker socket, the Hephaestus API socket, the dashboard port, or any host path
**Then** each attempt fails (FR-17, NFR-3, AR-46)

**Given** the running installation
**When** its containers are inspected
**Then** the Docker socket is mounted into the `hephaestus` container only, and no Task volume is mounted into the `hephaestus` container

**Given** a crash between the intent to create a container and its result
**When** Hephaestus restarts
**Then** the intent is resolved by the container's labels and no second container is created for the step run (AD-5)

**Given** a Task volume
**When** any container mounts it
**Then** it is mounted with `volume-nocopy`, and the step user can create a file in `/workspace` on a glibc image and on a musl image (AD-6)

**Given** a Task that starts
**When** its image digest is resolved
**Then** the runtime establishes whether the image uses glibc or musl, records it in the configuration snapshot, and mounts the matching executables at `/hephaestus/agents` in every step container of the Task (AD-8)

**Given** an image whose C library cannot be established
**When** the Task would start
**Then** it does not start and the Owner is told why

### Story 3.3: Keep what a step printed and bound how long it runs

As the Owner,
I want every step's output stored, its end reported exactly once, and its running time capped,
So that I can see what an Agent did and no step can run forever.

**Acceptance Criteria:**

**Given** a running step
**When** it writes output
**Then** the runtime writes the raw stream through the masker to `IOutputStore` as one file for that container run, indexed in the database (FR-4, FR-53)

**Given** a step that ends for any reason
**When** its container stops
**Then** the runtime appends exactly one step-exited Signal with the step-run id, the exit code, and the runtime's own cause, and removes the container (AD-18)

**Given** a step run whose deadline was recorded before its container started
**When** the deadline passes
**Then** the runtime, and nothing else, stops the container, the cause is recorded as `timeout`, and the output written so far is preserved (FR-19)

**Given** a step that is still running
**When** another Signal is appended to the inbox
**Then** the loop applies it within one tick, because the loop never waits for a step

**Given** the end-to-end test project
**When** this story is delivered
**Then** it holds a harness that starts a real installation against real SQLite and Docker and drives it with a scripted fake `IAgentAdapter`, whose invocation runs a script in a step container and makes no vendor call (AR-37)
**And** every later story of this epic adds its behavior to that harness, so integration grows story by story and is not left to the end of the epic

### Story 3.4: Route all Workspace traffic through an egress proxy

As the Owner,
I want a step container's only way to the network to be a proxy that I can restrict to a list of hosts,
So that code running in a Workspace cannot reach my local network and can be kept from sending data anywhere it likes.

**Acceptance Criteria:**

**Given** a step container
**When** its networks are inspected
**Then** it is attached only to the Docker internal workspace network, and a connection that does not go through the proxy fails (AD-7)

**Given** the egress proxy
**When** it is inspected
**Then** it is a separate, unprivileged container with no credential, no volume, and no Docker socket, and it is not part of the `hephaestus` process (AR-41)

**Given** a request through the proxy to a name that resolves to a loopback, link-local, or private address
**When** the proxy handles it
**Then** it refuses the request
**And** a step container can reach neither the host nor another step container, and cannot reach the dashboard port through the proxy (AR-47, T12)

**Given** a Project with a list of allowed egress hosts
**When** a step container requests a host outside the list
**Then** the proxy refuses it, and the refused host is recorded on the step run through the step-exited Signal
**And** a host on the list is reached

**Given** a Project with no list
**When** a step container requests any public host
**Then** the proxy passes it (NFR-4)

**Given** a step run
**When** its allowed hosts are set
**Then** they are the Project's list plus, only for a container that runs an Agent invocation, the hosts that invocation declares
**And** a Validation container cannot reach an Agent vendor's host that the Project did not list

**Given** any step container
**When** its environment is inspected
**Then** the standard proxy variables are set with the container's loopback excluded, git is configured through environment variables to fetch `github.com` over HTTPS where an SSH address is given, and nothing specific to a language or build tool is set

**Given** this story
**When** it is delivered
**Then** a decision record names the proxy chosen, how it is started, how each step run's hosts reach it, how step containers are kept apart, and the one host name and port at which step containers reach it; the proxy is free and open-source software (AR-67, NFR-15)
**And** that host name and port are documented and listed in the versioned public surface (AD-7)

**Given** the installation's egress default
**When** it is read
**Then** it is one setting whose value in v0.1 is open-unless-a-list-is-set, so that making the allowlist the default later is a change of that default and of nothing else (NFR-4)

**Given** a request over plain HTTP to an allowed host
**When** it goes through the proxy
**Then** it is passed; `dotnet restore` fetches a certificate revocation list that way

### Story 3.5: Give every step a fresh checkout

As the Owner,
I want each step to start from a clean clone of the last pushed commit,
So that nothing a previous step planted outside the commit can run beside another credential or forge a result.

**Acceptance Criteria:**

**Given** a Task that is about to run its first step
**When** its Workspace is prepared
**Then** exactly one named volume `hephaestus-ws-<task-id>` is created for it as a journaled effect, and no host path is bind-mounted (AD-6)

**Given** a registered Project
**When** the gateway fetches it
**Then** it keeps a bare mirror on the `hephaestus-mirrors` volume with no hooks and no working tree, and the orchestrator runs no git command in any directory an Agent can write to (AD-9)

**Given** any step about to run
**When** the checkout container runs first
**Then** it re-creates `/workspace` from a bundle of the Task's last pushed tip, or of its start commit on the Base Branch when nothing was pushed yet, as a fresh clone with no hooks, default git configuration, and no untracked or ignored files (FR-17)
**And** it runs the `hephaestus-tools` image, which contains git 2.48 or later and nothing else, and it receives no credential

**Given** a step that planted a git hook, a git configuration entry, and an ignored fake test runner
**When** the next step's checkout has run
**Then** none of the three exists in `/workspace` (AR-49)

**Given** a prepared Workspace
**When** the Task volume and the container's environment are searched
**Then** they contain no GitHub credential, token, or authenticated remote URL (FR-18)

**Given** a merge to `develop` or to `main`
**When** the publishing workflow of Story 1.13 runs
**Then** the `hephaestus-tools` image is built and published beside the `hephaestus` image, in the same channel and under the same tag, for both architectures (AR-38, AR-77)

**Given** a checkout that has run
**When** the step container starts as user `10001:10001`
**Then** it can write to every path the checkout created; the checkout container mounts the Task volume with `volume-nocopy` like every other (AD-6)

### Story 3.6: Checkpoint a step's work as one commit

As the Owner,
I want whatever a step changed to be captured as a single commit, even when the step was cut short,
So that no work is lost to a timeout, a crash, or a cancellation.

**Acceptance Criteria:**

**Given** a checkpointed step (`Implementation` or `Correction`) that ended for any reason, including a timeout, a crash, or a cancellation
**When** the checkpoint container runs
**Then** it builds its own git directory from the last pushed tip outside the Task volume, takes `/workspace` as work tree only, and writes exactly one commit whose parent is the last pushed tip (AD-9, FR-20)
**And** commits the Agent made during the step are flattened into it

**Given** a `/workspace` whose `.git` directory holds hostile configuration and hooks
**When** the checkpoint container runs
**Then** it never uses that directory and none of its content is executed (AR-50)

**Given** a step that left the tree unchanged
**When** the checkpoint container runs
**Then** it makes no commit and no bundle

**Given** a checkpoint commit
**When** it is read
**Then** Core supplied its whole message and identity: the committer is the Hephaestus GitHub identity and the message carries the trailers `Hephaestus-Task`, `Hephaestus-Step`, `Hephaestus-Agent`, `Hephaestus-Model`, and `Hephaestus-Effort`

**Given** a finished checkpoint container
**When** the runtime collects its result
**Then** it reads exactly one regular file of bounded size, under a name the runtime chose, from the stopped container, and hands it on as a bundle reference with path, checksum, tip, and prerequisite
**And** an archive with a link entry, a parent path, an extra entry, or a file over the size bound is an `infrastructure-error` and nothing from it is used

**Given** a step of a kind that is not checkpointed
**When** it ends
**Then** whatever it left in the tree is discarded

### Story 3.7: Push Task branches through the gateway's checks

As the Owner,
I want Hephaestus to push an Agent's work to the Task branch only after checking it, and to be unable to push anywhere else,
So that no secret, no change to protected files, and no damage to my Base Branch can come out of a Workspace.

**Acceptance Criteria:**

**Given** a bundle reference from a checkpoint
**When** the gateway imports it
**Then** it imports into a quarantine ref with strict object checking before any other ref moves, accepts only the Task's branch, and verifies that the new tip is a fast-forward of the last pushed tip (AD-9)

**Given** an imported bundle
**When** the gateway scans it
**Then** every new blob and every commit message is scanned with the masker, and a stored secret value in plain, base64, or URL-encoded form blocks the push (FR-4, AR-52)

**Given** an imported bundle whose diff against the Task's start commit touches a protected path
**When** the gateway checks it
**Then** the push is blocked (AD-19)

**Given** any failed check
**When** the push is blocked
**Then** the Workspace is kept and an Escalation names the check that failed

**Given** a bundle that passed every check
**When** the gateway pushes it
**Then** the Task branch `hephaestus/<issue-number>-<slug>` moves forward by fast-forward only, the push is a journaled effect, and after a crash it is resolved as a fast-forward and never pushed twice
**And** the step run records the tip before and after it, and the loop deletes the bundle file once the push result is journaled (FR-20, FR-30)

**Given** the gateway
**When** it is asked, in tests, to push, force-push, or delete a ref outside `hephaestus/*`, or to push a branch that is not a fast-forward
**Then** each request is refused, because no such code path exists (FR-44, AR-53)

**Given** a Task branch on GitHub whose tip is not the tip Hephaestus last pushed, because someone else pushed to it
**When** the gateway fetches the branch before a step or before a push
**Then** no step runs on that tip and nothing is pushed; the Task is held in `WaitingForOwner` and an Escalation names the commits Hephaestus did not push (owner decision, 2026-11-25)
**And** the Task stays held until a Trusted User adopts the remote tip with the `confirm` command, which Story 3.18 delivers

**Given** the code base
**When** it is inspected by an architecture test
**Then** `Hephaestus.GitHub` is the only code that pushes

### Story 3.8: Drive Claude Code through its official interface

As the Owner,
I want Hephaestus to run Claude Code non-interactively and understand what it printed,
So that Claude Code can implement or review a Task with no one watching.

**Acceptance Criteria:**

**Given** a step and an authentication mode
**When** the Claude Code adapter describes the invocation
**Then** it returns a fixed record: argument vector for `claude -p` with a stream-JSON output, environment, secret references by name and target, files, the egress hosts the CLI needs in that mode, and where the result is found
**And** the prompt is one fixed sentence that points at `/hephaestus/context/instruction.md` (AD-10, AD-12)
**And** the argument vector is the same for an API key and for a subscription sign-in; only the secret reference differs (AD-10)

**Given** any invocation
**When** it is inspected
**Then** it switches off repository-supplied hooks, MCP servers, settings, and plugins, disables tools that act outside the container under the vendor account, turns off the CLI's own sandbox and permission prompts, and disables self-update, in the way Story 1.1 proved
**And** an authentication mode for which this cannot be guaranteed is declared unsupported (AD-7, AR-56)

**Given** a Model and an Effort resolved by Core
**When** the adapter maps them
**Then** it produces the vendor's flags, or declares the Effort unsupported; it never substitutes another value silently (FR-24)

**Given** a stored output stream
**When** the adapter parses it
**Then** it yields normalized events and a normalized result, with usage where the CLI reported it and null where it did not

**Given** a CLI that exited by itself
**When** the adapter classifies the exit
**Then** it returns a failure class only when the stream positively identifies it, and `unknown` otherwise (AD-11)

**Given** the adapter
**When** it is asked for a probe
**Then** it describes an invocation that checks the CLI is present and authenticated

**Given** the adapter's tests
**When** they run
**Then** they use recorded CLI transcripts tagged with the pinned CLI version, with no container and no vendor call
**And** an architecture test shows the adapter starts no process and references nothing of Docker (AR-37)

**Given** any invocation
**When** it is inspected
**Then** the CLI takes settings only from the throwaway `HOME`, is offered a closed list of tools that holds no `Skill` and no `Task` tool, does not read the repository's `CLAUDE.md`, and has traffic the vendor calls nonessential switched off (AD-7, AD-12)

**Given** a repository that plants a session-start hook, an MCP server, and an `env` block in its Claude Code settings
**When** the CLI is started with the adapter's invocation and no credential
**Then** none of them takes effect; this test needs no vendor call, and what executes only around a tool call stays with the credentialed run of AR-56

**Given** the adapter
**When** it declares its egress hosts
**Then** they are the hosts recorded for the pinned version, `api.anthropic.com` for both modes at 2.1.332

### Story 3.9: Drive Codex CLI through its official interface

As the Owner,
I want Hephaestus to run Codex CLI non-interactively and understand what it printed,
So that the second Agent can implement or review a Task to the same contract as the first.

**Acceptance Criteria:**

**Given** a step
**When** the Codex adapter describes the invocation
**Then** it returns the same fixed record as Story 3.8, built on `codex exec --json`, with the same single-sentence prompt (AD-10)

**Given** the Codex adapter
**When** it is asked which authentication modes it supports
**Then** it supports an API key and declares subscription sign-in unsupported, with the reasons: the credential file is a full account credential that must be kept and refreshed between runs, and the tools the account brings cannot be listed (AR-8)

**Given** any invocation
**When** it is inspected
**Then** repository-supplied executable configuration and tools that act outside the container are switched off, the CLI's own sandbox and approvals are off, and self-update is disabled, in the way Story 1.1 proved (AD-7, AR-56)

**Given** an Effort resolved by Core
**When** the adapter maps it
**Then** it sets `model_reasoning_effort` for the run, the mechanism Story 1.1 confirmed; a Core Effort with no Codex value is declared unsupported, reported, and not imitated (FR-24)

**Given** a stored output stream
**When** the adapter parses it
**Then** it yields the same normalized events and result types as the Claude Code adapter, with usage null where not reported
**And** an exit that is not positively identified is classified `unknown`

**Given** the adapter's tests
**When** they run
**Then** they use recorded transcripts tagged with the pinned CLI version, with no container and no vendor call

**Given** any invocation
**When** it is inspected
**Then** it points `CODEX_CA_CERTIFICATE` at the bundle under `/hephaestus/agents`, keeps the repository's `AGENTS.md` from the Agent, and declares the hosts recorded for the pinned version: `api.openai.com`, `chatgpt.com`, and `ab.chatgpt.com` at 0.172.0 (AD-8, AD-10, AD-12)

**Given** a stored output stream
**When** usage is normalized
**Then** token counts are taken from the stream and cost is null, because Codex CLI reports none

**Given** a repository that plants a hook and an MCP server in its Codex configuration
**When** the CLI is started with the adapter's invocation and no credential
**Then** neither takes effect

### Story 3.10: Choose how each Agent signs in and check that it is ready

As the Owner,
I want to choose, per Agent, between an API key and a subscription sign-in, and to see whether the Agent is ready,
So that I use the access I already pay for and find out about a broken credential before a Task does.

**Acceptance Criteria:**

**Given** an Agent
**When** the Owner sets its authentication mode
**Then** any mode its adapter supports is accepted, and an unsupported mode is refused with the adapter's reason (FR-25)

**Given** a chosen mode
**When** the Owner supplies the credential
**Then** it is taken on standard input and stored as a secret
**And** Hephaestus presents no sign-in flow of its own (AD-16)

**Given** a configured Agent
**When** the Owner runs the command that checks it
**Then** the runtime runs the adapter's probe in a container with no Task volume and the command reports the Agent as ready, or as unavailable with the reason (FR-26)
**And** the probe runs in an image this story fixes and documents, which belongs to no Project and meets the Project image contract

**Given** an Agent a probe found unavailable
**When** the result is recorded
**Then** it is stored on the capacity record of that Agent credential with the time of the probe, the Agent counts as having no capacity, and no step run is marked failed (AD-20)

**Given** a step container that runs an Agent
**When** the runtime starts it
**Then** it resolves only that Agent's secret references through `ISecretStore` at that moment
**And** the value appears in no database row, journal entry, Docker label, or container name (FR-18)

**Given** the installation documentation
**When** an Owner reads the section on Agent sign-in
**Then** both modes are presented on an equal footing with the provider terms that apply to each and the risk of using a subscription for automated work, and an API key with a spend cap at the vendor is named as the conservative choice (AR-58)
**And** it says that Claude Code's subscription credential is the token `claude setup-token` prints, that Codex CLI is supported with an API key only and why, and that Codex CLI contacts `chatgpt.com` even with an API key

### Story 3.11: Give every Agent the same instructions for the same step

As the Owner,
I want the instructions for a step to come from the Project's Methodology and to be identical whichever Agent performs it,
So that moving a Task between Agents changes who does the work, not how it is done.

**Acceptance Criteria:**

**Given** the Hephaestus image
**When** `methodologies/default` is read
**Then** it is a directory with a manifest and one instruction template per Agent step kind it supports, written as plain role prompts; in this epic it holds the `Implementation` template (AD-14, FR-64)

**Given** a Project whose configuration selects no Methodology and whose repository holds no Methodology files
**When** a step's instructions are chosen
**Then** the built-in `default` is used (FR-61)

**Given** a step kind
**When** Core selects a template
**Then** it selects by step kind only and never branches on the Methodology's name or on the Agent
**And** templates contain no substitution and refer only to context file paths

**Given** a step run
**When** Core assembles its context
**Then** it writes one file set under the fixed names `instruction.md`, `task.json`, `issue.md`, `comments.json`, `plan.md`, `findings.json`, `validation.json`, `handoff.md`, and `result.schema.json`, an input that does not exist being an absent file
**And** only admitted content enters any file (AD-12, NFR-1)
**And** the runtime places the set read-only at `/hephaestus/context` and the Methodology read-only at `/hephaestus/methodology`

**Given** the same step of the same Task assembled once for Claude Code and once for Codex CLI
**When** the two `instruction.md` files are compared
**Then** they are byte-identical (FR-62)

**Given** a step run
**When** its record is read
**Then** it states the Methodology's name and the identity of the template applied

**Given** the result types Core defines
**When** `result.schema.json` is generated
**Then** it is restricted to what both CLIs' schema-constrained output accepts, as Story 1.1 established (AD-13)

**Given** a step run
**When** its context is assembled
**Then** `task.json` carries the Task's language, and the `default` templates tell the Agent to write the prose fields of its result in that language (FR-69)
**And** the templates themselves are the same for every language and every Agent, so the two `instruction.md` files stay byte-identical (AD-21, AD-14)

**Given** the `default` templates
**When** they are read
**Then** none relies on a CLI's skill, sub-agent, or slash command, or on an instruction file in the repository, because both Agents run without them (AD-12)

### Story 3.12: Let the Project's Validation commands decide

As the Owner,
I want my Project's own build and test commands, run where nothing can interfere with them, to decide whether work is acceptable,
So that an Agent saying it is finished proves nothing.

**Acceptance Criteria:**

**Given** a pushed Task branch tip and the Validation commands in the Task's configuration snapshot
**When** Validation runs
**Then** each command runs as its own container on a fresh checkout of that tip, in the Project's image at the recorded digest, with no credential and with the Project's egress list alone (AD-13, AD-6)

**Given** the commands' results
**When** the outcome is decided
**Then** Validation passes only if every command exited with status 0, and nothing an Agent reported plays any part (FR-31)

**Given** a command that exits non-zero
**When** Validation ends
**Then** the outcome is `validation-failure`, the commands after it are recorded as not run, and each command's exit status, duration, and output file are recorded

**Given** a command that runs past its deadline
**When** the runtime stops it
**Then** the outcome is `timeout` and its output so far is preserved (FR-19)

**Given** a Validation run that changed files in the tree
**When** it ends
**Then** the changes are discarded; Validation is never checkpointed

**Given** recorded Validation results
**When** a later step's context is assembled
**Then** they are available as `validation.json`

### Story 3.13: Publish one pull request per Task

As the Owner,
I want each Task to end in one pull request that tells me what was done and how it was checked,
So that my review starts from facts and not from an Agent's summary alone.

**Acceptance Criteria:**

**Given** a Task whose branch is pushed
**When** Hephaestus publishes it
**Then** one pull request from the Task branch to the Base Branch is opened under Hephaestus's own identity and linked to the Task's issue
**And** the Task moves to `PullRequestOpen` and its state label follows (FR-36)

**Given** the pull request description
**When** it is read
**Then** it states what changed, which Agent, Model, and Effort implemented it, and the result of each Validation command, all composed by Core from recorded data

**Given** text in the description that came from an Agent
**When** it is posted
**Then** HTML comments are escaped, mentions and issue-closing keywords are broken, command keywords are fenced, and stored secret values are masked (AD-19, FR-4)

**Given** a crash after the pull request was opened and before the result was recorded
**When** Hephaestus restarts
**Then** the existing pull request is found by its head branch and no second one is opened (FR-21)

**Given** a Task that already has a pull request
**When** it is published again after further work
**Then** the same pull request is updated, as a journaled effect

**Given** a publication
**When** the loop performs it
**Then** it is an effect of the loop, not a step run, and it does not hold the single slot

**Given** the pull request description
**When** its sections are read
**Then** they are What changed, Task, How it was checked, and Who did what, in that order, as `EXPERIENCE.md` fixes them, and a section with nothing to say is left out (UX-DR-19)
**And** whether the Task's issue is named with a closing keyword is decided with the Owner in this story and recorded in `EXPERIENCE.md`

### Story 3.14: Start a Project on demand and get a pull request

As the Owner,
I want to start work on a Project from the command line and find a pull request for the labelled issue,
So that I can delegate a Task and come back to a result.

**Acceptance Criteria:**

**Given** a registered Project
**When** the Owner runs the command that starts work on it
**Then** the API answers 202 with a Signal id, the applied Signal opens a window record for the Project, and the Signal's status can be read as pending, applied, or rejected (FR-16, AD-17)

**Given** a Project whose window is already open
**When** the Owner starts work on it again
**Then** the Signal is applied without opening a second window, and the command says a window is already open

**Given** an open window and eligible `Queued` Tasks
**When** the loop dispatches
**Then** it takes the Task the dispatch-order policy puts first, moves it to `Running` only from `Queued`, and never has two Tasks `Running` (FR-14)

**Given** a Task being dispatched
**When** it starts
**Then** the Project Configuration is read from the Base Branch, validated, and snapshotted onto the Task with its commit id and the resolved image digest
**And** every later step of that Task uses the snapshot and the digest, even after the file changes on the Base Branch (FR-2)

**Given** a `Running` Task
**When** its steps run
**Then** the Primary Agent performs `Implementation` on a fresh checkout, the work is checkpointed and pushed, Validation runs, and on a pass the pull request is published
**And** the Task holds the single slot across all its steps

**Given** an open window
**When** no eligible Task remains, or the Owner stops the window
**Then** the window closes; a Task already `Running` finishes and no new one starts

**Given** the harness from Story 3.3
**When** the end-to-end tests run
**Then** its scripted fake `IAgentAdapter` drives a Task from a labelled issue to a pull request with no vendor call (AR-37)

### Story 3.15: Refuse to start what is not ready and record who did what

As the Owner,
I want a Task to start only when its configuration, labels, and content are sound, and every step recorded with the Agent that performed it,
So that a bad setup stops before it costs anything and I can always tell who did what.

**Acceptance Criteria:**

**Given** a Project whose configuration is missing or invalid, a Task whose Effort the chosen Agent's adapter does not support, or a Task whose admitted content no longer matches its recorded hash
**When** the Task would be dispatched
**Then** it does not start, a comment on its issue says exactly what is wrong, and Tasks of other Projects are unaffected (FR-2, AD-10)

**Given** an `Implementation` result
**When** Core reads it
**Then** it is validated by strict deserialization; `needs-decision` raises an Escalation with its questions and options; a missing or invalid result is an `agent-error` and is never interpreted (AD-13)

**Given** an `Implementation` step that reported `completed` and left no commit ahead of the Task's start commit
**When** the loop continues the Task
**Then** no Validation runs and no pull request is opened; the Task becomes `Failed` and a comment on its issue says the Agent made no changes (owner decision, 2026-11-25)

**Given** each step run
**When** its record is read
**Then** it names the Agent, Model, and Effort that performed it and the usage the Agent reported, null where none was (FR-30)

**Given** two Projects with open windows and eligible Tasks
**When** the loop dispatches
**Then** one Task runs and a Task of the other Project starts only after the first has left `Running`
**And** within each Project the order policy of Epic 2 holds, including the Priority Label and declared dependencies (FR-14, FR-8, FR-10)

**Given** a Task that already has a step run and has returned to `Queued`, for example after a trusted answer
**When** no window is open for its Project
**Then** it is dispatched all the same, because only a Task with no step run requires an open window (AD-1, FR-49)

**Given** a second test Project that names a different stock image
**When** the end-to-end tests run a Task in it
**Then** it completes from labelled issue to pull request with no change to Hephaestus (FR-5)

**Given** a step that is about to be given to an Agent
**When** no probe of that Agent credential has succeeded within the freshness period this story fixes
**Then** the runtime runs the adapter's probe before the step starts (FR-26, owner decision, 2026-11-25)

**Given** a probe that finds the Agent unavailable, or its provider unreachable
**When** the result is recorded
**Then** the step does not start and no retry is spent; the Agent counts as having no capacity, the Task moves to `WaitingForCapacity` without holding the single slot, and a comment on its issue names the Agent and the reason (AD-20, NFR-12)
**And** the next probe time is recorded within a bounded schedule, and the Task returns to `Queued` when a probe finds the Agent ready

**Given** a Project that the Owner removes while it has Tasks that are not finished
**When** the removal is applied
**Then** the command lists those Tasks, none of them is dispatched afterwards, and the last effect for that Project is a comment on each such issue saying the Project is no longer registered and the Task will not start (FR-1)
**And** a Task that is `Running` at that moment finishes

### Story 3.16: Classify every failure and bound every retry

As the Owner,
I want each failure named for what it was and retried only a set number of times,
So that Hephaestus neither gives up on a hiccup nor loops forever.

**Acceptance Criteria:**

**Given** a step run that failed
**When** it is recorded
**Then** it carries exactly one of `usage-limit`, `timeout`, `agent-error`, `validation-failure`, `infrastructure-error`, `cancelled`, `unknown`
**And** the class is decided in one order: the runtime's cause first (`cancelled`, `timeout`, and out-of-memory or Docker faults as `infrastructure-error`), then the adapter's reading of a CLI that exited by itself, then Core; anything not positively identified is `unknown` (AD-11, NFR-11)

**Given** a step run that ended in `timeout`, `agent-error`, `infrastructure-error`, or `unknown`
**When** the loop continues the Task
**Then** the step is attempted again as a new step run on a fresh checkout of the last pushed tip, and the attempt counts against the step retry bound of that step kind, two by default, read from configuration (NFR-10)

**Given** a step whose retry bound is exhausted, or a Validation that failed while this epic has no Correction Loop
**When** the Task ends
**Then** it becomes `Failed`, its state label follows, and a comment on the issue says what was attempted, which class of failure ended it, and which command or step failed
**And** the Task branch holds all work done

**Given** a `Failed` Task
**When** a Trusted User applies the Marker Label again
**Then** the same Task returns to `Queued` and continues on the same branch (AD-2)

**Given** a real Usage Limit in v0.1
**When** the adapter cannot positively identify it
**Then** it is recorded as `unknown` and retried within the bound; it is never treated as a Usage Limit (FR-27, AR-64)

**Given** the test suite
**When** it runs
**Then** it covers the deadline on an Agent run, including a CLI that keeps waiting for a network that is gone, the deadline on a Validation command, the step retry bound, and the Task duration limit (AR-55)

**Given** a Task whose configuration snapshot carries a duration limit, within the installation's
**When** a step run's deadline is set
**Then** the deadline is no later than the moment the Task's time in `Running` would reach that limit (NFR-5, owner decision, 2026-11-25)

**Given** a Task whose time in `Running` reaches its duration limit
**When** the runtime stops the running step
**Then** the step run is recorded as `timeout`, a checkpoint preserves the work, no further retry is attempted, and the Task becomes `Failed` with a comment that names the Task duration limit

### Story 3.17: Lose nothing and duplicate nothing across a restart

As the Owner,
I want Hephaestus to pick up exactly where it was after a crash or a restart,
So that I can stop, upgrade, or lose power without cleaning up afterwards.

**Acceptance Criteria:**

**Given** a restart while a step container was running
**When** the loop's first tick runs
**Then** the container is removed, a checkpoint is taken of the Task volume, the step run is closed as `infrastructure-error`, and the step is attempted again as a new step run; it is never resumed in place (AD-1)
**And** the Task branch holds every change made before the interruption, and the continuing Agent starts from that state, not from the starting branch (FR-20)

**Given** an interrupted Task and other eligible Tasks
**When** the loop dispatches after the restart
**Then** the interrupted Task runs first, whether or not a window is open for its Project

**Given** a fault-injection test that stops the process between every intent and its result, for push, pull request, comment, label change, container, and volume
**When** Hephaestus restarts each time
**Then** no duplicate pull request, comment, or Workspace exists, and every Task continues from its recorded state (FR-21, NFR-9, AR-54)

**Given** a crash while a Signal was being applied
**When** Hephaestus restarts
**Then** the Signal is either fully applied or not applied at all, because marking it applied, its Task and step-run writes, and its intents commit in one transaction

**Given** a restart
**When** recovery runs
**Then** it is the loop's ordinary first tick; there is no separate recovery mode, and no module other than the poller acts on a timer

### Story 3.18: React to what happens on GitHub during and after a Task

As the Owner,
I want Hephaestus to respond correctly when I withdraw, close, or merge while it is working or after its pull request is open,
So that GitHub stays the place where I express what I want.

**Acceptance Criteria:**

**Given** a `Running` Task
**When** a Trusted User removes the Marker Label or closes the issue
**Then** the runtime stops the running step, a checkpoint preserves the work done, and the Task becomes `Cancelled` or `Closed` respectively
**And** the same action by anyone else changes nothing

**Given** a Task in `PullRequestOpen` or `Failed`
**When** its pull request is merged by a Trusted User
**Then** the Task becomes `Merged` and Tasks that depend on it become eligible (FR-10)

**Given** such a pull request
**When** it is merged by someone who is not a Trusted User
**Then** the Task becomes `Cancelled`, the reason is reported on the issue, Hephaestus does not revert the merge, and Tasks that depend on it are not dispatched (AD-2, AR-48)

**Given** a Task cancelled by such a merge
**When** a Trusted User posts `/hephaestus confirm` on the pull request
**Then** the Task becomes `Merged`

**Given** a Task held because its branch carries commits Hephaestus did not push
**When** a Trusted User posts `/hephaestus confirm` on the issue or on the pull request
**Then** the remote tip is adopted as the last pushed tip and the Task returns to `Queued`

**Given** a Task with an open pull request
**When** a Trusted User closes the pull request without merging
**Then** the Task becomes `Closed`

**Given** a `Cancelled` Task that was not merged
**When** a Trusted User applies the Marker Label again, later than the cancellation
**Then** the same Task returns to `Queued` and reuses its branch and its pull request

**Given** a registered Project
**When** a pull request opened by Hephaestus is merged or closed, or receives a comment
**Then** the poller appends one actor-attributed Signal for the event, as Story 2.1 does for issues

**Given** the lifecycle table of the architecture spine
**When** the state machine's tests run
**Then** every transition in the table that v0.1 can reach is implemented, and any transition that is not in the table is rejected (AR-31)

### Story 3.19: Reconstruct what happened to a Task

As the Owner,
I want the full record of every step of a Task, kept after its Workspace is gone,
So that I can work out afterwards exactly what happened and which Agent did what.

**Acceptance Criteria:**

**Given** a Task that has run
**When** the Owner runs the command that shows its history
**Then** it lists each step run with its kind, Agent, Model, Effort, start and end time, outcome or failure class, Validation results, Escalations, and reported usage (FR-53)
**And** usage that was not reported is shown as none, not as zero

**Given** any commit in the Task's pull request
**When** the Owner looks it up in the history
**Then** the history names the Agent and Model that produced it, read from the tips recorded before and after each step run and not from the commit's content (FR-30)

**Given** a step run whose raw output is still retained
**When** the Owner asks for it
**Then** the stored, masked output is returned

**Given** a restart, or the removal of the Task's Workspace
**When** the history is shown again
**Then** it is unchanged

**Given** an installation that holds Task history and is upgraded to an image with new migrations
**When** the history is shown after the upgrade
**Then** every step run, Validation result, and Escalation recorded before the upgrade is still shown (FR-60)

**Given** raw output older than the retention period, which is configurable, 30 days by default, with an option to keep output permanently
**When** the loop's retention phase runs
**Then** the output files are removed and their index entries say so, and the structured history is kept (NFR-13)

### Story 3.20: Remove a Workspace only when its work is safe

As the Owner,
I want Hephaestus to clean up finished Workspaces and never one that still holds unpushed work,
So that my disk does not fill up and nothing is thrown away.

**Acceptance Criteria:**

**Given** a Task that has left `Running` and whose last checkpoint tip the journal shows as pushed
**When** the loop cleans up
**Then** the Task volume is removed as a journaled effect (FR-22, AD-6)

**Given** a Task whose Workspace holds work that is not in the repository, for example after a blocked push
**When** the loop cleans up
**Then** the volume is not removed

**Given** a cleanup
**When** it selects what to remove
**Then** it selects by the labels `hephaestus.installation` and `hephaestus.task`, so that no other Task's Workspace and no object of another installation on the same host is touched (AR-34, AR-44)

**Given** a Task whose volume was removed
**When** a later step of that Task runs
**Then** the volume is re-created from the mirror

**Given** a crash between the intent to remove a volume and its result
**When** Hephaestus restarts
**Then** the intent is resolved by the volume's absence

### Story 3.21: State the security assumptions and prove the claims made

As someone deciding whether to run Hephaestus,
I want its documentation to say plainly what it protects against, what it does not, and which of that has been tested,
So that I can decide how far to trust it with my machine and my credentials.

**Acceptance Criteria:**

**Given** a complete Task run in the end-to-end tests
**When** the Task volume, every step container's environment, and every context file are searched for the GitHub credential's value
**Then** it is found in none of them (NFR-2, AR-45)

**Given** the security documentation
**When** it is read
**Then** it states the assumptions and known limitations of the threat model, including that egress is open in v0.1 unless a list is set, that the orchestrator holds the Docker socket and is equivalent to root on the host, and that containers are not a virtual machine boundary (AR-61)

**Given** any security claim in user-facing material
**When** it is checked against the test suite
**Then** a passing test backs it; a control whose test does not exist yet is described as designed, not as tested (NFR-8)

**Given** the installation documentation
**When** an Owner reads it
**Then** it tells them to give workflows triggered by `hephaestus/*` branches and their pull requests a read-only token and no deployment or publishing secrets, and recommends branch protection on the Base Branch (AR-59)
**And** it lists the proxy settings the common ecosystems need, as Story 1.1 found them, with the proxy's fixed host name and port, and each setting listed was run behind the proxy in the place the documentation names; Story 1.1 ran Maven's from `HOME`, which a step container replaces
**And** it says which registry hosts a Project that sets an allow-list must include, and that the Marker Label, not a state label, is the control (AR-60)

**Given** the threat model's verification plan
**When** this epic closes
**Then** each of its controls is mapped to the test that shows it, and the controls that belong to later epics are marked as pending

**Given** the documentation
**When** it lists what must be true before Hephaestus is announced for general use
**Then** the list includes that the egress allowlist is the default; it is a condition on the announcement and is tied to no release number, as NFR-4 states it

**Given** repository plugins, which no run of Story 1.1 got to load
**When** the security statement is written
**Then** it makes no claim about them unless a test first gets a repository plugin to load with nothing switched off and then shows the adapters' invocations stop it; without that test it says plainly that this is not shown (T5, T14)

**Given** the security documentation
**When** it describes what a repository can do to an Agent
**Then** it says that instruction files and skills are prompt content, that neither Agent is given the repository's instruction file, and that Codex CLI still offers a repository's skills to the model (T2)

### Story 3.22: Follow a Task's history in the Dashboard

As the Owner,
I want to open a Task and see every step it went through, with what each printed,
So that I can work out what happened without the command line.

**Acceptance Criteria:**

**Given** a Task that has run
**When** the Owner opens it in the Dashboard
**Then** its header shows issue number and title, the state chip, the Agents and Effort, the branch, and links to the issue and the pull request
**And** a step timeline lists each step run, oldest first, with kind, Agent, Model, Effort, start and end, outcome or failure class, and reported usage, "None" where none was reported (FR-67, FR-53, UX-DR-8)

**Given** a Validation node on the timeline
**When** it is read
**Then** each command is shown with its exit status and duration, and a command that was not run is shown as not run

**Given** a step run whose output is retained
**When** the Owner opens its node
**Then** a side sheet shows the stored, masked output as text, loaded in pages, with a copy action, and the open step lives in the address
**And** output removed by retention is replaced by a line saying when it was removed and that the step's record is kept (UX-DR-10)

**Given** a Task that is `Running` while it is open in the Dashboard
**When** a step starts or ends
**Then** the node appears or changes without a reload and without moving the scroll position
**And** a step that is still running shows "Running since …" and no output until it ends (AR-66, UX-DR-11)

**Given** a restart, or the removal of the Task's Workspace
**When** the Task is opened again
**Then** its history is unchanged

**Given** a `Failed` Task, a Task that has not started in a Project with no open window, or a Task held for confirmation
**When** it is opened
**Then** the act-elsewhere hint that `EXPERIENCE.md` fixes for that case is shown (UX-DR-9)

**Given** Installation
**When** the Owner opens it after this story
**Then** each Agent is listed with its sign-in mode and whether it is ready, with the reason and the time of the last probe when it is not (FR-26)

**Given** Agent output that contains markup, script, or a stored secret value
**When** it is shown
**Then** it is rendered as text, and the secret appears as the mask that was stored (FR-4, T36)

**Given** the end-to-end harness of Story 3.3
**When** it opens the Dashboard for a completed Task
**Then** what is shown matches the recorded history, and the dashboard security tests of the threat model's verification plan pass, including that the dashboard port cannot be reached from a step container (AR-73)

## Epic 4: Independent review, correction, and readiness

Every pull request arrives already reviewed by a different Agent than the one that wrote it. Blocking Findings and failed Validation go back to the author within a bounded loop, and a Task that still fails ends in a draft pull request that says what was tried and what remains. An unclear issue produces a question, not code, and a large Task is planned first with the plan shown in the pull request. Each significant step is reported on the issue.

### Story 4.1: Have the other Agent review every Task

As the Owner,
I want each Task's changes reviewed by a different Agent than the one that wrote them,
So that my own review starts from a higher baseline.

**Acceptance Criteria:**

**Given** a Task whose Validation passed
**When** the loop continues it
**Then** a `Review` step runs before any pull request is presented as ready, performed by the other of the Task's two Agents than the author
**And** the author is the Agent of the latest checkpointed step run that changed the tree (FR-32, AD-13)

**Given** a `Review` result
**When** Core reads it
**Then** it is a validated document of Findings, each classified blocking or non-blocking, and Core gives each Finding an id (FR-33)
**And** the Task's history shows each Finding with its id, its class, and its current status (FR-53)

**Given** a Review with no blocking Findings
**When** the Task is published
**Then** the pull request description names the Reviewer with its Model and Effort and lists every non-blocking Finding
**And** the Task is not held by them

**Given** the Reviewer has no capacity or is unavailable
**When** the Review is due
**Then** the Task moves to `WaitingForCapacity`, does not hold the single slot, and returns to `Queued` when a probe finds the Reviewer ready; it is never reviewed by its own author

**Given** a `Review` step
**When** it ends
**Then** it is not checkpointed and anything it changed in the tree is discarded (AD-6)

**Given** the `default` Methodology
**When** a `Review` step is assembled
**Then** it has a `Review` template, and the Reviewer receives the same Methodology the implementer worked to (FR-62)

**Given** two Tasks of one Project, the first with Claude Code as Primary Agent and Codex CLI as Fallback Agent, the second with the two swapped
**When** both run in the end-to-end tests
**Then** each is implemented by its Primary Agent and reviewed by the other, with no change to the Project between them (FR-24)

### Story 4.2: Correct blocking Findings and failed Validation within a bound

As the Owner,
I want problems found by Validation or Review sent back to the author a limited number of times,
So that routine mistakes are fixed before I ever see the pull request.

**Acceptance Criteria:**

**Given** a failed Validation or a Review with blocking Findings, and Correction rounds left
**When** the loop continues the Task
**Then** a `Correction` step is given to the author with the open Findings as `findings.json` and the Validation results as `validation.json`, and Validation and then Review run again afterwards (FR-34)

**Given** a Review that follows a Correction
**When** Core reads its result
**Then** it holds the status of every Finding that was open and any new Findings

**Given** a Task
**When** its rounds are counted
**Then** only failed Validation and blocking Findings spend Correction rounds, the bound is read from the Task's configuration snapshot with a default of two, and failures of other classes spend the step retry bound instead (AD-11)

**Given** a completed round
**When** the Task's history is read
**Then** the round is recorded with what was returned to the Agent and the tips before and after the Correction

**Given** the `default` Methodology
**When** a `Correction` step is assembled
**Then** it has a `Correction` template, and the step is checkpointed like `Implementation`

**Given** the test suite
**When** it runs
**Then** it shows that the loop stops at the bound and can never run past it (NFR-10, AR-55)

### Story 4.3: End a failed Task in a draft pull request

As the Owner,
I want a Task that could not be completed to leave a draft pull request explaining what happened,
So that I can finish it by hand or decide to drop it with the facts in front of me.

**Acceptance Criteria:**

**Given** a Task whose Correction rounds or step retries are exhausted with blocking Findings or failing Validation remaining, and with at least one commit ahead of its start commit
**When** the Task ends
**Then** Hephaestus opens a draft pull request linked to the issue whose description says what was attempted, what still fails, and which Findings remain open (FR-35)
**And** the Task becomes `Failed` and its state label follows

**Given** a Task that failed with no commit ahead of its start commit
**When** the Task ends
**Then** it gets a comment on its issue and no pull request

**Given** a Task that fails
**When** the failure is recorded
**Then** the `INotifier` port is called and a comment on the issue says the Task failed and links the draft pull request; the outcome is the same whether or not a Notification is delivered (AR-63)

**Given** a `Failed` Task with a draft pull request
**When** a Trusted User opts it in again and it later passes Validation and Review
**Then** the same pull request is updated and marked ready; no second one is opened

**Given** this story
**When** it is delivered
**Then** it replaces the interim outcome of Story 3.16, in which a failed Validation ended the Task with a comment only

### Story 4.4: Check that an issue is ready before writing code

As the Owner,
I want Hephaestus to judge whether an issue is clear enough before it implements anything,
So that an ambiguous issue costs me a question and not a wrong pull request.

**Acceptance Criteria:**

**Given** a dispatched Task with no step run yet
**When** its steps begin
**Then** a `Readiness` step runs first and returns a validated verdict: clear and small, clear and large, or `needs-decision` (FR-9, AD-13)

**Given** a verdict of clear and small
**When** the loop continues
**Then** `Implementation` starts without waiting for the Owner

**Given** a verdict of `needs-decision`
**When** the loop continues
**Then** an Escalation is raised with the specific questions and options the result carries, the Task moves to `WaitingForOwner`, and the next eligible Task is dispatched (FR-48)

**Given** a Task returned to `Queued` by a trusted answer
**When** it is dispatched again
**Then** `Readiness` runs again with the answer in `comments.json`

**Given** a `Readiness` step
**When** it ends
**Then** it is not checkpointed, and its Agent, Model, Effort, and Methodology template are recorded like any step run

**Given** the `default` Methodology
**When** a `Readiness` step is assembled
**Then** it has a `Readiness` template that names the sections it looks for in an issue: the business requirements and the acceptance criteria

### Story 4.5: Plan a large Task before implementing it

As the Owner,
I want a Task that is clear but large to be planned first, with the plan shown in the pull request,
So that the Reviewer and I can see what the implementation was meant to do.

**Acceptance Criteria:**

**Given** a `Readiness` verdict of clear and large
**When** the loop continues
**Then** a `Planning` step runs before `Implementation` and returns a validated plan (FR-65)

**Given** a plan
**When** it is stored
**Then** it is kept in the database, passed to every later step as `plan.md`, and rendered into the pull request description; it is not committed to the repository (AD-12)

**Given** a `Planning` result of `needs-decision`
**When** the loop continues
**Then** an Escalation is raised with its questions and the Task waits for the Owner

**Given** the plan's result type and the `default` `Planning` template
**When** they are read
**Then** they are limited to the one Task: they have no place for product-level requirements, architecture, or a backlog

**Given** a verdict of clear and small
**When** the Task runs
**Then** no `Planning` step runs and the pull request shows no plan

### Story 4.6: Report each significant step on the issue

As the Owner,
I want a short comment on the issue at each significant step of a Task,
So that I can follow the work without opening Hephaestus.

**Acceptance Criteria:**

**Given** a Task
**When** work starts, Validation completes, Review completes, an Escalation is raised, the pull request opens, or the Task fails
**Then** a comment is posted on its issue (FR-52)

**Given** any such comment
**When** it is read
**Then** it says what happened and what comes next, in plain professional English (NFR-17)

**Given** progress comments
**When** they are posted
**Then** each is a journaled effect with a hidden marker, is never posted twice after a restart, has Agent-derived text neutralised, and has stored secret values masked (AD-5, AD-19)

**Given** a Task with several Correction rounds
**When** its issue is read
**Then** the comments report each round's Validation and Review outcome without repeating Findings already reported as unchanged

**Given** any progress comment
**When** it is read
**Then** it follows the comment anatomy and uses the headline and "Next:" line that `EXPERIENCE.md` gives for its event (UX-DR-17, UX-DR-14)

### Story 4.7: See Findings, the plan, and Correction rounds in the Dashboard

As the Owner,
I want a Task's Findings, its plan, and its Correction rounds shown in the Dashboard,
So that I can see what the Reviewer found and what was done about it before I open the pull request.

**Acceptance Criteria:**

**Given** a Task that was reviewed
**When** the Owner opens it in the Dashboard
**Then** its Findings are listed with id, class, current status, and text, blocking Findings first, and a Finding resolved in a later round stays listed as resolved (FR-67, UX-DR-8)

**Given** a Task that was planned
**When** the Owner opens it
**Then** the plan is shown as text, capped at a readable line length, and a Task that was not planned shows no plan section

**Given** a Task with Correction rounds
**When** its timeline is read
**Then** each round is shown with what was returned to the Agent and the Validation and Review that followed it, and the round count is shown against the bound

**Given** Finding or plan text that contains markup or script
**When** it is shown
**Then** it is rendered as text (AR-73, T36)

**Given** a Task being reviewed or corrected while it is open in the Dashboard
**When** a Review or a Correction round ends
**Then** the new Findings and the new round appear without a reload (UX-DR-11)

## Epic 5: BMAD Method projects

A Project that uses BMAD Method has its Tasks planned, implemented, and reviewed with the same BMAD workflow whichever Agent performs the step. The Owner can have Hephaestus create one issue per Story, each linked to its Story, carrying its acceptance criteria and its dependencies, and never created twice.

### Story 5.1: Use the Methodology files a Project keeps in its repository

As the Owner,
I want a Project's own Methodology files to be the ones Agents follow, taken only from the Base Branch,
So that a Task cannot rewrite the workflow its Reviewer follows.

**Acceptance Criteria:**

**Given** a Project whose Methodology supplies files in the repository
**When** a Task starts
**Then** those files are read from the Base Branch at the commit of the Task's configuration snapshot and placed read-only at `/hephaestus/methodology` for every step (AD-14)

**Given** a Task branch that changes a Methodology file
**When** a later step of that Task runs
**Then** the step still sees the files of the snapshot commit, and templates refer to `/hephaestus/methodology`, never to a copy in `/workspace`

**Given** a Project Configuration that selects a Methodology whose files are not present in the Project
**When** a Task would be dispatched
**Then** it does not start and the Owner is told which Methodology is unavailable and what is missing (FR-61)

**Given** a Methodology added this way
**When** the code that schedules, validates, and approves Tasks is compared before and after
**Then** it is unchanged (FR-64)

### Story 5.2: Run BMAD workflows for each step

As the Owner,
I want a BMAD Method Project to have its Tasks planned, implemented, and reviewed by BMAD's own workflows under either Agent,
So that every Agent works to the standard my Project already uses.

**Acceptance Criteria:**

**Given** the Hephaestus image
**When** `methodologies/bmad` is read
**Then** it holds a manifest and one template per step kind, each naming the BMAD workflow that Story 1.2 found to complete unattended for that step kind (FR-63, AR-69)

**Given** a step of a BMAD Project
**When** it is assembled once for Claude Code and once for Codex CLI
**Then** both receive the same instruction and run the same BMAD workflow

**Given** a BMAD workflow that needs a decision from a person
**When** the step runs
**Then** the step returns `needs-decision` and Hephaestus raises an Escalation; no workflow that waits for human input is used

**Given** a step kind for which Story 1.2 found no unattended BMAD workflow
**When** the `bmad` Methodology is built
**Then** its manifest states what serves that step kind instead, as agreed with the Owner at the close of Story 1.2

**Given** a BMAD Project whose image lacks what the workflows need
**When** a step fails for that reason
**Then** the failure report names the missing tool
**And** the documentation lists what a BMAD Project's image must provide, including `uv` and Python

**Given** a BMAD Project
**When** a Task runs from a labelled issue to a pull request in the end-to-end tests
**Then** each step run records the `bmad` Methodology and the template applied (FR-62)

### Story 5.3: Create issues from a Project's Stories

As the Owner,
I want Hephaestus to turn the Stories I planned into GitHub issues that I can then opt in one by one,
So that planned work reaches the Queue without copying it by hand.

**Acceptance Criteria:**

**Given** a Project whose Methodology documents on the Base Branch contain Stories
**When** the Owner runs the command that creates issues from them
**Then** one issue is created per Story, linking to the Story and carrying its requirements and acceptance criteria (FR-66)
**And** Stories are read from the Base Branch only

**Given** Stories with an order or dependencies between them
**When** their issues are created
**Then** each dependent issue carries a `Depends-On: #<issue-number>` line for each Story it depends on (FR-10)

**Given** a created issue
**When** its labels are read
**Then** it does not carry the Marker Label, and no Task exists for it until a Trusted User applies one (FR-6)

**Given** the command run a second time
**When** some Stories already have issues
**Then** issues are created only for Stories that have none
**And** a Story is matched to its issue by a stable Story identity declared by the Methodology manifest and carried in the issue's hidden marker; nothing is written back to the Base Branch (AD-14)

**Given** a crash after an issue was created and before its result was recorded
**When** Hephaestus restarts
**Then** the issue is found by its hidden marker and is not created again, because issue creation is a Project-scoped journaled effect (AD-5)

**Given** a Story whose content changed after its issue was created
**When** the command runs
**Then** it reports the Story as changed and leaves the issue as it is; the Owner decides whether to edit the issue (AR-69)

**Given** Stories that were renumbered or retitled in their document after their issues were created
**When** the command runs
**Then** each Story is still matched to its own issue and no issue is created twice, because the stable Story identity is neither the Story's number nor its title

## Epic 6: Projects on their own schedules

The Owner gives each Project its own working days, start time, and optional end time. Hephaestus works each Project in its window, finishes a Task that runs past the end time, and resumes interrupted work before starting anything new.

### Story 6.1: Set a Schedule for a Project

As the Owner,
I want to set the days and hours on which Hephaestus works on each Project,
So that I can give different days to different Projects.

**Acceptance Criteria:**

**Given** a registered Project
**When** the Owner sets its Schedule with days of the week, a start time, and optionally an end time
**Then** the Schedule is stored in the Installation Configuration and shown by the command that shows the Project (FR-12)

**Given** a Schedule
**When** it is stored
**Then** it carries the time zone its times are meant in, its times are evaluated in that zone across daylight-saving changes, and every recorded instant is stored in UTC (AR-70, AR-32)

**Given** a Schedule whose end time is earlier than its start time
**When** it is stored
**Then** the end time is taken as that time on the following day, and the command says so

**Given** a Schedule with an invalid day or time
**When** the Owner sets it
**Then** the command refuses it and names the invalid value

**Given** a Project with a Schedule
**When** the Owner clears it
**Then** the Project is worked only when started on demand

**Given** any repository content
**When** it is merged
**Then** it cannot set or change a Schedule; the Project Configuration schema has no key for one (FR-3)

### Story 6.2: Work each Project inside its window

As the Owner,
I want Hephaestus to start a Project's Tasks only inside its Schedule and to finish what is running when the end time passes,
So that work happens when I planned it and is never cut off halfway.

**Acceptance Criteria:**

**Given** a Project whose Schedule's start time arrives on a scheduled day
**When** the loop ticks
**Then** it opens a window record for the Project by comparing the Schedule with `TimeProvider`; no timer outside the loop is involved (AD-1)

**Given** a Project with no open window
**When** it has eligible `Queued` Tasks
**Then** none of them starts (FR-12)

**Given** an open window with an end time
**When** the end time passes while a Task is `Running`
**Then** the Task finishes normally, the window closes, and no new Task of that Project starts after it (FR-13)

**Given** an open window with no end time
**When** the Queue has no eligible Task, every remaining Task is waiting, or no Agent has capacity
**Then** the window closes

**Given** Hephaestus was stopped when a scheduled start time passed
**When** it starts again before that window's end time, or on the same scheduled day when there is no end time
**Then** the window opens on the first tick

**Given** a Project with a Schedule
**When** the Owner starts it on demand outside the Schedule
**Then** a window opens as in Story 3.14 (FR-16)

**Given** the documentation on Schedules
**When** an Owner reads it
**Then** it says that a Schedule limits when a Task may start, not when it may finish: a Task that has already started resumes outside the Schedule after a trusted answer, when capacity returns, or after an interruption (owner decision, 2026-11-25; AD-1, FR-29)

### Story 6.3: Share one slot fairly between Projects

As the Owner,
I want Projects whose windows overlap to take turns on the single slot, with interrupted work finished first,
So that two schedules never mean two Tasks at once or a Task left half done.

**Acceptance Criteria:**

**Given** two Projects with open windows and eligible Tasks
**When** the loop dispatches
**Then** one Task runs and a Task of the other Project starts only after the first has left `Running` (FR-14)

**Given** two Projects with open windows
**When** the next Task is chosen between them
**Then** the choice follows one documented, deterministic rule that this story fixes in the dispatch-order policy

**Given** a Task of one Project that was interrupted
**When** another Project's window has opened in the meantime
**Then** the interrupted Task is resumed and finished before any new Task starts, whether or not its own Project's window is still open (FR-15)

**Given** a Task that returned to `Queued` from `WaitingForOwner` or `WaitingForCapacity`
**When** it is considered for dispatch
**Then** it needs no open window, because only a Task with no step run requires one, and it is ordered among eligible Tasks by Priority Label and issue number; it is not treated as interrupted and does not jump ahead of an interrupted Task (AD-1, FR-29, FR-49)

### Story 6.4: See Schedules and windows in the Dashboard

As the Owner,
I want each Project's Schedule and its windows shown in the Dashboard,
So that I know when Hephaestus will next work on a Project.

**Acceptance Criteria:**

**Given** a Project with a Schedule
**When** the Owner opens its Schedule tab
**Then** it shows the days, the start time, the end time if any, and the time zone the times are meant in (FR-67, FR-12)

**Given** a Project
**When** its Schedule tab or the Projects list is shown
**Then** it shows whether a window is open, when it opened and how (by Schedule or on demand), and when the next one opens

**Given** a Project with no Schedule
**When** its Schedule tab is shown
**Then** it says the Project is worked only when started on demand and shows the commands that set a Schedule and that start the Project (UX-DR-9)

**Given** a window that opens or closes while the Dashboard is open
**When** the loop records it
**Then** the view changes without a reload (UX-DR-11)

## Epic 7: Keep working through Usage Limits

When an Agent runs out of usage, the Task moves to the Fallback Agent with everything it needs, or waits and resumes on its own when capacity returns. Other Tasks keep moving meanwhile.

### Story 7.1: Usage Limit spike — what the CLIs emit at a real limit

As the Owner,
I want to know what Claude Code and Codex CLI actually emit when a real Usage Limit is reached,
So that limit handling is built on observed signals and not on undocumented guesses.

**Acceptance Criteria:**

**Given** that a real Usage Limit can only be reached on the Owner's own accounts and cannot be triggered on demand
**When** the spike is planned and run
**Then** the Owner supplies the accounts and takes part in the run, the story may stay open until a real limit occurs, and it is not closed by a dev agent alone

**Given** each pinned CLI in each supported authentication mode
**When** a real Usage Limit is reached, or a real occurrence is captured
**Then** the report records the exit code, every event in the stream around the stop, and any field that states when the limit resets (AR-6)

**Given** the captured output
**When** it is stored
**Then** it is added to the recorded transcripts, tagged with the CLI version, for the adapters' tests

**Given** each CLI
**When** its options for checking remaining capacity without a model call are examined
**Then** the report states whether such a probe exists and what it returns

**Given** a mode in which no reliable signal was observed
**When** the spike closes
**Then** the report says so, and the consequence, that such stops stay classified `unknown`, is raised with the Owner

**Given** all findings
**When** the spike closes
**Then** a report under `docs/spikes/` holds them, and the remaining stories of this epic are adjusted to them before they start

**Given** the `rate_limit_event` that Story 1.1 saw on every Claude Code run under subscription sign-in, with `status`, `resetsAt`, and `rateLimitType`
**When** the spike examines it
**Then** the report states how its fields behave before and at a real limit, and whether it tells remaining capacity without a further model call

### Story 7.2: Recognize a Usage Limit for what it is

As the Owner,
I want Hephaestus to tell a Usage Limit apart from every other failure,
So that it hands off or waits when it should and never mistakes a real error for a limit.

**Acceptance Criteria:**

**Given** a stored output stream that carries the signals Story 7.1 confirmed
**When** the adapter classifies the exit
**Then** the step run is recorded as `usage-limit` (FR-27)

**Given** a stream that does not positively carry those signals
**When** the adapter classifies the exit
**Then** the class is `unknown` or another positively identified class; it is never `usage-limit` (AD-11)

**Given** a `usage-limit` whose stream states when the limit resets
**When** it is recorded
**Then** that time is stored on the capacity record of the Agent credential as exhausted-until; where no time is stated, the record holds the time of the next probe instead (AD-20)

**Given** a `usage-limit` step run
**When** the Task's bounds are counted
**Then** it spends neither a Correction round nor a step retry

**Given** a checkpointed step ended by a Usage Limit
**When** the loop continues
**Then** a checkpoint is taken first, so the Task branch holds the work done (FR-20)

**Given** the adapters' tests
**When** they run
**Then** they cover the transcripts captured in Story 7.1 for both CLIs

### Story 7.3: Do not send work to an Agent that has no capacity

As the Owner,
I want Hephaestus to know which Agents have capacity before it gives them work,
So that the next Task is not thrown at an Agent already known to be exhausted.

**Acceptance Criteria:**

**Given** an Agent credential recorded as exhausted until a time in the future
**When** the loop chooses an Agent for a step
**Then** that Agent is treated as having no capacity (AD-20)

**Given** an exhausted-until time that has passed, or a next-probe time that has arrived
**When** the loop ticks
**Then** the runtime runs the adapter's probe in a container with no Task volume, and the capacity record is updated with the result and the probe time

**Given** a probe that finds an Agent unavailable or still without capacity
**When** it is recorded
**Then** no step run is marked failed, and the next probe time is recorded within a bounded schedule (NFR-10)

**Given** an Agent's provider that is unreachable
**When** a probe runs
**Then** the Agent counts as having no capacity and is probed again later; no Task moves to `Failed` because of the outage (NFR-12)

### Story 7.4: Wait for capacity and resume without being asked

As the Owner,
I want a Task that no Agent can currently work on to wait and to resume on its own,
So that I never have to restart anything after a limit resets.

**Acceptance Criteria:**

**Given** a Task whose next step falls to an Agent that has no capacity
**When** the loop continues it
**Then** the Task moves to `WaitingForCapacity` with its resume condition recorded, and its issue shows the state and, where known, until when (FR-29)

**Given** a Task in `WaitingForCapacity`
**When** the dispatch-order policy is evaluated
**Then** the Task does not hold the single slot, and other Tasks that have an Agent with capacity are dispatched (AD-20)

**Given** capacity that returns for the Agent a waiting Task needs
**When** the capacity record is updated
**Then** the Task returns to `Queued` within one polling cycle with no action from the Owner, and continues from its recorded state whether or not a window is open for its Project (AD-1)

**Given** a Task in `WaitingForCapacity`
**When** a Trusted User removes the Marker Label
**Then** the Task becomes `Cancelled` as the lifecycle table states

### Story 7.5: Hand a Task to the Fallback Agent

As the Owner,
I want a Task to continue on the Fallback Agent when the working Agent runs out of usage,
So that work goes on while a limit resets.

**Acceptance Criteria:**

**Given** a step that ended in `usage-limit` and a Fallback Agent with capacity
**When** the loop continues the Task
**Then** the same step kind is given to the other Agent as a new step run, and a Handoff is recorded with its cause, the Agent it came from, and the Agent it went to (FR-28)
**And** the Task's history shows the Handoff (FR-53)

**Given** the receiving step run
**When** its context is assembled
**Then** it holds the requirements and acceptance criteria as `issue.md`, the plan, the Findings, the Validation results so far, and a `handoff.md` that Core assembled from recorded data stating what is completed and what remains
**And** no conversation history is transferred and no Agent wrote the summary (AD-12)

**Given** the receiving Agent's instructions
**When** they are read
**Then** they are the same Methodology template for that step kind, and they tell the Agent to inspect and validate the partial work on the branch before continuing (FR-62)

**Given** a Task after a Handoff
**When** its Review is due
**Then** the Reviewer is the other Agent than the one that wrote the latest changes, and if that Agent has no capacity the Task waits under Story 7.4 (FR-32)

**Given** a Task
**When** its Handoffs are counted
**Then** they have their own bound, read from configuration, and exceeding it ends the Task as `Failed` (AD-11)

**Given** a Handoff
**When** it happens
**Then** a comment on the issue says which Agent stopped, why, and which Agent continues (FR-52)

**Given** a step that ended in `usage-limit` when the Fallback Agent has no capacity either
**When** the loop continues the Task
**Then** no Handoff is recorded and the Task waits under Story 7.4 until either Agent has capacity (FR-29)

### Story 7.6: See Agent capacity and Handoffs in the Dashboard

As the Owner,
I want to see which Agents have capacity and which Tasks changed hands,
So that a quiet Queue explains itself.

**Acceptance Criteria:**

**Given** the Agents surface
**When** the Owner opens it
**Then** each Agent is shown with its capacity record: ready, or exhausted until a time, with the time of the last probe and of the next one (FR-67, AD-20)

**Given** a Task in `waiting-for-capacity`
**When** it is listed or opened
**Then** it shows which Agent it waits for and, where known, until when (FR-29)

**Given** a Task that was handed off
**When** its timeline is read
**Then** the Handoff appears between the two step runs with its cause, the Agent it came from, and the Agent it went to (FR-28)

**Given** capacity that returns, or a Handoff that happens, while the Dashboard is open
**When** the loop records it
**Then** the view changes without a reload (UX-DR-11)

## Epic 8: Notifications

The Owner is alerted by email, phone push, or both when an Escalation is raised, when a Task fails, and when a scheduled window ends. A Notification that cannot be delivered never changes a Task's outcome.

### Story 8.1: Set up notification channels

As the Owner,
I want to tell Hephaestus where to alert me, by email, by phone push, or both,
So that I hear about what needs me without watching GitHub.

**Acceptance Criteria:**

**Given** a running installation
**When** the Owner configures an email channel with their own SMTP server, or a push channel with an ntfy server and topic
**Then** the channel is stored in the Installation Configuration and its credentials are stored as secrets, entered on standard input (FR-50, AR-70)

**Given** a configured channel
**When** the Owner runs the command that sends a test Notification
**Then** the Notification arrives, or the command reports why it could not be delivered

**Given** both channels configured
**When** a Notification is sent
**Then** it goes to both

**Given** no channel configured
**When** an event that notifies occurs
**Then** nothing is sent and work continues

**Given** this story
**When** it is delivered
**Then** a notifier adapter implements `INotifier` in place of the no-op, its place in the solution is recorded in a decision record, and it depends only on free and open-source components (NFR-15)

### Story 8.2: Alert the Owner when a Task needs them or fails

As the Owner,
I want an alert when Hephaestus asks a question or a Task fails,
So that a waiting Task does not sit unnoticed.

**Acceptance Criteria:**

**Given** a configured channel
**When** an Escalation is raised or a Task becomes `Failed`
**Then** a Notification is sent that says which Project and Task need attention and links to the issue or pull request where the Owner acts (FR-50)

**Given** a Notification
**When** its content is read
**Then** it says only that attention is needed; it carries no question text, no answer, and no Agent output, and stored secret values are masked

**Given** a Notification that cannot be delivered
**When** the attempt fails
**Then** the failure is recorded, work continues, and the Task's state and outcome are the same as if it had been delivered (FR-51)

**Given** a crash after a Notification's intent was recorded
**When** Hephaestus restarts
**Then** the Notification is not sent again (AD-5)

**Given** a Notification
**When** it is composed
**Then** its text is in the installation's language (FR-69)

### Story 8.3: Summarize a window when it ends

As the Owner,
I want one summary when a Project's window closes,
So that I know in a glance what the session produced.

**Acceptance Criteria:**

**Given** a window that closes
**When** the loop records its end
**Then** one summary Notification is sent for that window stating the pull requests opened, the Tasks that failed, and the Escalations waiting, each with a link (FR-50)

**Given** a window in which nothing was dispatched
**When** it closes
**Then** the summary says so; the counts are not shown as if work had been done

**Given** a summary that cannot be delivered
**When** the attempt fails
**Then** the failure is recorded and nothing else changes (FR-51)

## Epic 9: Control and insight

The Owner cancels a Task from the command line, asks what is running, queued, and waiting, and reads the success metrics computed from real executions only.

### Story 9.1: Cancel a Task from the command line

As the Owner,
I want to cancel a Task that is queued or in progress,
So that I can stop work I no longer want without losing what was done.

**Acceptance Criteria:**

**Given** a Task that is `Queued`, `Running`, `WaitingForOwner`, or `WaitingForCapacity`
**When** the Owner runs the cancel command for it
**Then** the API answers 202 with a Signal id, and when the Signal is applied the Task becomes `Cancelled` and its issue shows it (FR-23)

**Given** a `Running` Task
**When** it is cancelled
**Then** the runtime stops the running step within a bounded time, the step run is recorded as `cancelled`, and a checkpoint preserves the work done (FR-20)

**Given** a cancellation requested during a long Agent run
**When** the loop ticks
**Then** the Signal is applied within one tick, because the loop never waits for a step (AD-18)

**Given** a `Cancelled` Task
**When** time passes or a window opens
**Then** it does not start again
**And** it returns to `Queued` only when a Trusted User applies the Marker Label later than the cancellation

**Given** a Task in a final state, or an issue that is not a Task
**When** the Owner tries to cancel it
**Then** the Signal is rejected with problem details that say why

### Story 9.2: Ask what Hephaestus is doing

As the Owner,
I want to ask from the command line what is running, what is queued, and what is waiting,
So that I can see the state of the forge without reading every issue.

**Acceptance Criteria:**

**Given** a running installation
**When** the Owner runs the status command
**Then** it names the `Running` Task with its Project, its current step, and the Agent performing it, or says that nothing is running (FR-54)

**Given** Tasks in `WaitingForOwner` or `WaitingForCapacity`
**When** status is shown
**Then** each is listed with what it waits for and, where known, until when

**Given** `Queued` Tasks
**When** status is shown
**Then** they are listed in the order the dispatch-order policy would take them, and a Task that is passed over shows why: an unsatisfied dependency, a label problem, or a closed window

**Given** the status command
**When** it runs
**Then** it also shows open windows, each Agent's capacity record, and the loop's last tick
**And** it reads state only; it appends no Signal

### Story 9.3: Show metrics computed from real executions

As the Owner,
I want Hephaestus's success metrics computed only from what actually happened,
So that I can judge whether it is earning its place.

**Acceptance Criteria:**

**Given** recorded executions
**When** the Owner runs the metrics command
**Then** it shows SM-1 unassisted acceptance, SM-2 first-pass Validation, SM-3 Owner interventions per Task, SM-4 review effort per pull request, SM-5 unattended windows, SM-6 successful Handoffs, SM-7 self-hosting, and the counter-metrics SM-C1 rework rate and SM-C2 wasted review, as `success-metrics.md` defines them (FR-55)

**Given** any figure shown
**When** the Owner asks for its detail
**Then** the Tasks it was computed from are listed

**Given** a metric with no data, including one whose inputs do not exist before a later release
**When** it is shown
**Then** it is shown as having no data, never as zero

**Given** the metrics output
**When** it is read
**Then** it contains no estimated or simulated figure, and the number of pull requests opened is not presented as a success metric

**Given** SM-7
**When** it is computed
**Then** it covers the Project the Owner has named as Hephaestus's own repository, and shows no data when none is named

### Story 9.4: Stop new work when a spend limit is reached

As the Owner,
I want a spend limit for Agents that run on an API key,
So that an unattended run cannot cost more than I decided.

**Acceptance Criteria:**

**Given** an Agent that authenticates with an API key
**When** the Owner sets a spend limit for it
**Then** the limit and the period it applies to are stored in the Installation Configuration (NFR-5, AR-70)

**Given** step runs whose Agent reported usage
**When** spend is accounted
**Then** it is computed only from the usage the Agent reported

**Given** an Agent whose accounted spend has reached its limit
**When** the loop chooses an Agent for a step
**Then** no new step is given to that Agent, the Owner is notified through `INotifier`, and status shows the limit as reached
**And** a step already running is not interrupted

**Given** an Agent or an authentication mode that reports no usage
**When** the Owner sets or views a spend limit for it
**Then** Hephaestus says plainly that the limit cannot be enforced

**Given** Codex CLI, which reports token counts and no cost
**When** a spend limit is set for it
**Then** the story fixes how its spend is counted, and a price is never assumed silently; where no count is possible, Hephaestus says the limit cannot be enforced

### Story 9.5: Stop a Task whose Workspace has outgrown its limit

As the Owner,
I want Hephaestus to measure each Task's Workspace and stop the Task when it passes a size limit,
So that one runaway build is cut off before it fills my disk.

**Acceptance Criteria:**

**Given** the Installation Configuration
**When** the Owner sets a Workspace size limit
**Then** it is stored and shown with the other installation limits (AR-70)

**Given** a step that is running
**When** the runtime measures the Task volume, at the interval this story fixes and again when the step ends
**Then** the measured size is recorded on the step run

**Given** a measurement above the limit
**When** the runtime records it
**Then** the running step is stopped and recorded as `infrastructure-error` with a reason that names the limit and the measured size, and the step retry bound applies (AD-11)

**Given** a Task stopped for this reason
**When** its issue is read
**Then** a comment says the Workspace passed the size limit, so the Owner does not have to find it in the history

**Given** this story
**When** it is delivered
**Then** a decision record states how and how often the size is measured, and that this is detection after the limit was passed, not a hard cap: a Workspace can exceed the limit by whatever is written between two measurements (owner decision, 2026-11-25)
**And** the security documentation's limitation about disk exhaustion says the same and claims no more (NFR-8)

### Story 9.6: See status and statistics in the Dashboard

As the Owner,
I want the status of the forge and its success metrics in the Dashboard,
So that I can judge how it is doing without the command line.

**Acceptance Criteria:**

**Given** Overview
**When** the Owner opens it after this story
**Then** a status section names the `Running` Task with its Project, its current step, and the Agent performing it, or says that nothing is running; lists waiting Tasks with what each waits for; and lists `Queued` Tasks in the order the dispatch-order policy would take them, with the reason for any that is passed over (FR-54, FR-67)
**And** it agrees with the status command at the same moment

**Given** the Statistics surface
**When** the Owner opens it
**Then** it shows SM-1 to SM-7 and the counter-metrics SM-C1 and SM-C2 as Story 9.3 computes them (FR-55)

**Given** any figure on Statistics
**When** the Owner opens it
**Then** the Tasks it was computed from are listed, each linking to its Task

**Given** a metric with no data
**When** it is shown
**Then** it reads "No data", never zero, and no estimated or simulated figure appears anywhere (UX-DR-10)

**Given** an Agent whose spend limit is reached, or cannot be enforced
**When** Agents is shown
**Then** it says so

## Epic 10: Approve to merge, command to rework

The Owner's approving review is the only way into the Base Branch: Hephaestus merges approved pull requests at the start of the next window. When the Owner wants changes, `/hephaestus rework` starts a revision that passes Validation and independent Review again, as many times as wanted.

### Story 10.1: Merge approved pull requests at the start of a window

As the Owner,
I want Hephaestus to merge the pull requests I approved when a Project's next window opens,
So that approved work lands without my doing it by hand, and nothing lands without my approval.

**Acceptance Criteria:**

**Given** a registered Project
**When** a review is submitted or dismissed on a pull request Hephaestus opened
**Then** the poller appends one actor-attributed Signal, and Core records an approval only when the reviewer is a Trusted User (AD-2, AD-15)

**Given** a window that opens for a Project, by Schedule or on demand
**When** the loop processes it
**Then** before dispatching new work it merges each pull request in `PullRequestOpen` whose approval by a Trusted User is not dismissed, covers the pull request's latest commit, and whose repository checks pass (FR-41)
**And** the merge call carries the approved head commit id, the merge is a journaled effect resolved after a crash by the pull request's state, and the Task becomes `Merged`

**Given** a pull request that fails a condition
**When** the window opens
**Then** it is left open and a comment says which condition failed: no trusted approval, an approval that does not cover the latest commit, or checks that have not passed
**And** the comment is posted once per distinct reason and head commit

**Given** any combination of labels, comments, issue content, Agent output, or an approval by someone who is not a Trusted User
**When** the tests exercise it
**Then** no merge happens without an approving review from a Trusted User on the latest commit (FR-44)

**Given** the gateway
**When** its code paths are tested
**Then** the merge call is its only write to a Base Branch, it never force-pushes to or deletes a Base Branch, and nothing in Hephaestus deploys anything

**Given** a merge
**When** the loop performs it
**Then** it is an effect of the loop, not a step run, and does not hold the single slot
**And** the merge method used is fixed by this story and documented

### Story 10.2: Start a Rework with an explicit command

As the Owner,
I want a revision of an open pull request to start only when I issue the Rework Command,
So that my ordinary review comments never trigger work by accident.

**Acceptance Criteria:**

**Given** a Task in `PullRequestOpen`
**When** a Trusted User posts a comment on its pull request whose first line is `/hephaestus rework`
**Then** the Task returns to `Queued` for a Rework, and the verb `rework` is part of the closed command set from this release (FR-45, AR-33)

**Given** comments and review comments on the pull request without the command
**When** they are posted
**Then** no work starts

**Given** the Rework Command posted by someone who is not a Trusted User
**When** it is read
**Then** it is ignored and recorded without a reply, as Story 2.5 states, and the Task does not change

**Given** a Rework that starts
**When** its context is assembled
**Then** it holds the review comments and comments on the pull request written by Trusted Users and admitted under the rules of Epic 2, and nothing written by anyone else (NFR-1)

**Given** the Rework Command on a pull request whose Task is not in `PullRequestOpen`
**When** it is read
**Then** a reply says why no Rework was started

### Story 10.3: Check reworked changes like any other work

As the Owner,
I want a reworked pull request to pass Validation and an independent Review before I am asked to look again,
So that a revision meets the same bar as the first version.

**Acceptance Criteria:**

**Given** a Task queued for a Rework and dispatched in a window
**When** its steps run
**Then** a `Rework` step is performed on a fresh checkout of the Task branch, is checkpointed and pushed by fast-forward, and is followed by Validation and by a Review from the other Agent than the one that wrote the reworked changes (FR-46, FR-32)

**Given** a Rework whose Validation fails or whose Review has blocking Findings
**When** the loop continues
**Then** the Correction Loop applies with its bound counted afresh for this Rework

**Given** a Rework that passed
**When** the pull request is updated
**Then** its description shows the new Validation results and the new Review, the Task returns to `PullRequestOpen`, and a comment tells the Owner the pull request is ready to look at again

**Given** an approval given before the Rework
**When** the reworked commits are pushed
**Then** that approval no longer covers the latest commit and the pull request is not merged until it is approved again (FR-41)

**Given** a Task
**When** the Owner issues the Rework Command again after any number of earlier Reworks
**Then** another Rework starts; there is no limit on their number, and each starts only on the command

**Given** the `default` and `bmad` Methodologies
**When** a `Rework` step is assembled
**Then** each has a `Rework` template

### Story 10.4: See which pull requests await what in the Dashboard

As the Owner,
I want every open pull request listed with what it still needs,
So that I know which ones are waiting for me.

**Acceptance Criteria:**

**Given** Tasks in `PullRequestOpen`
**When** the Owner opens Pull requests
**Then** each is listed with its Project, Task, and link, whether a Trusted User approved it, whether that approval covers the latest commit, and whether the repository's checks pass (FR-67, FR-41)

**Given** a pull request that will be merged when the next window opens
**When** it is listed
**Then** it says so and when that window opens; one that will not says which condition fails

**Given** a pull request awaiting approval
**When** it is listed
**Then** an act-elsewhere hint links to it on GitHub; the Dashboard offers no way to approve or merge (UX-DR-9, FR-44)

**Given** a Task queued for a Rework
**When** it is listed or opened
**Then** it shows that a Rework is pending and, afterwards, the Rework step on its timeline

**Given** a review submitted or a merge performed while the Dashboard is open
**When** the loop records it
**Then** the list changes without a reload (UX-DR-11)

## Epic 11: Stacked pull requests

Work continues while pull requests await review: a Task can build on the branch of the Task before it. Each pull request links to its neighbors, merges follow Stack order, a merged or reworked pull request is carried through everything above it, and a failed Task does not silently break what depends on it.

### Story 11.1: Choose a stacking mode for a Project

As the Owner,
I want each Project to choose whether Tasks stack in Queue order, stack only on declared dependencies, or do not stack at all,
So that stacking fits how each Project's work hangs together and I can still have dependent work wait for my merge.

**Acceptance Criteria:**

**Given** a Project Configuration
**When** it sets the stacking mode
**Then** the accepted values are stacking in Queue order, stacking only on declared dependencies, and off, and any other value is reported as an error (FR-37)

**Given** a Project whose stacking mode is off
**When** a Task with a declared dependency is considered for dispatch
**Then** the dependency is satisfied only when its Task is `Merged`, exactly as before v0.3, and no Task is stacked (FR-10)

**Given** a Project Configuration that does not set the mode
**When** it is validated
**Then** stacking only on declared dependencies applies (owner decision, 2026-11-25)
**And** the release's changelog states that a declared dependency is now satisfied by a branch and that setting the mode to off restores the earlier behavior (FR-10, FR-59)

**Given** a change of mode merged to the Base Branch
**When** later Tasks start
**Then** they follow the new mode, and Tasks and Stacks that already exist stay as they are

### Story 11.2: Build a Task on the branch of the Task it depends on

As the Owner,
I want a Task to start from the branch of the Task it depends on as soon as that branch exists,
So that dependent work does not wait for my review of the work below it.

**Acceptance Criteria:**

**Given** a Project whose stacking mode is not off, and a Task with a declared dependency whose Task is in `PullRequestOpen`
**When** the dispatch-order policy is evaluated
**Then** the dependency counts as satisfied, because it has produced a branch to build on (FR-10)

**Given** such a Task being dispatched
**When** it starts
**Then** its start commit is the tip of the dependency's branch, and the two Tasks form a Stack

**Given** a stacked Task that is published
**When** its pull request is opened
**Then** it targets the branch of the Task below it, so its diff shows only its own changes, and its description says it is stacked, on which pull request, and why (FR-38)

**Given** a Task with more than one unmerged dependency that do not lie in one Stack
**When** the policy is evaluated
**Then** the Task waits until its unmerged dependencies lie in a single Stack, and a comment on its issue says what it waits for

**Given** a protected-path check on a stacked Task
**When** the gateway checks the bundle
**Then** the diff is taken against the Task's own start commit, so changes made by the Task below are not attributed to it (AD-19)

### Story 11.3: Stack in Queue order and keep independent Tasks independent

As the Owner,
I want Queue-order stacking to chain Tasks that build on one another and to leave unrelated Tasks on their own branches,
So that an unrelated fix is never held up behind a Stack.

**Acceptance Criteria:**

**Given** a Project in Queue-order mode
**When** a Task is dispatched while earlier Tasks have open pull requests
**Then** Hephaestus determines whether the Task depends on that pending work, by the method this story fixes and documents (AR-71)

**Given** a Task found to depend on pending work
**When** it starts
**Then** it is stacked on the top of that Stack as in Story 11.2

**Given** a Task found not to depend on pending work
**When** it starts
**Then** it gets its own branch from the Base Branch and a pull request against the Base Branch (FR-38)

**Given** either outcome
**When** the pull request is read
**Then** its description states whether the Task was stacked or independent, and why

**Given** a Task with a declared dependency
**When** independence is determined
**Then** the declaration decides and the automatic judgement is not consulted (FR-10)

### Story 11.4: Link every pull request in a Stack to its neighbors

As the Owner,
I want each pull request in a Stack to link to the one before and the one after,
So that I can walk a Stack in order while reviewing.

**Acceptance Criteria:**

**Given** a Stack of pull requests
**When** any of them is read
**Then** its description links to the previous and to the next pull request in the Stack, and shows its position (FR-39)

**Given** a pull request that joins a Stack
**When** it is published
**Then** the links of its neighbor are updated as journaled pull-request-update effects

**Given** a pull request that is merged from a Stack or leaves it by being closed
**When** the loop reconciles the Stack
**Then** every remaining pull request's links point to its actual neighbors

### Story 11.5: Merge a Stack in order and keep it readable

As the Owner,
I want a Stack merged from the bottom up, with nothing above an unapproved pull request merged, and the rest of the Stack still readable afterwards,
So that approved work lands in the right order and what remains shows only its own changes.

**Acceptance Criteria:**

**Given** a Stack in a window that opens
**When** Hephaestus merges approved pull requests
**Then** it merges them in Stack order and stops at the first pull request that does not meet the conditions of Story 10.1 (FR-42)

**Given** a Stack of three in which only the second is approved
**When** the window opens
**Then** nothing is merged and the Owner is told that the first awaits approval

**Given** the first pull request of a Stack has been merged
**When** the loop reconciles the Stack
**Then** the second targets the Base Branch, its diff contains only its own Task's changes, and its issue link and Stack links are correct (FR-43)

**Given** the update the remaining pull requests need after a merge
**When** it needs a working tree
**Then** it runs in a Workspace and never in a mirror, and the way it is done (rebase or merge, squash or merge commit, and any exception to fast-forward-only pushes of Task branches) is fixed by this story in a decision record (AR-71, AD-9)

**Given** an update after a merge that cannot be completed cleanly
**When** it fails
**Then** an Escalation is raised on the affected pull request and nothing is guessed

**Given** an approval on a pull request whose branch the update changed
**When** the next window opens
**Then** the approval counts only if it still covers the latest commit (FR-41)

### Story 11.6: Carry a Rework up the Stack

As the Owner,
I want a change I asked for in the middle of a Stack to reach every pull request above it,
So that the Stack stays consistent without my rebasing anything.

**Acceptance Criteria:**

**Given** a Rework that passed on a pull request with others stacked above it
**When** the loop continues
**Then** each pull request above it is updated, in Stack order, to include the change, by the mechanism Story 11.5 fixed (FR-47)

**Given** an updated pull request above a Rework
**When** it is read
**Then** its diff still shows only its own Task's changes and its Stack links are correct

**Given** an update that cannot be completed cleanly
**When** it fails
**Then** an Escalation is raised on that pull request naming the conflict, the pull requests above it are left as they were, and nothing is guessed

**Given** a pull request whose branch was updated this way
**When** Validation results on it are read
**Then** Validation has run on the updated branch, and a failure is reported on the pull request

### Story 11.7: Decide what happens above a failed Task

As the Owner,
I want Hephaestus to work out, for each Task above a failed one, whether it can still be done,
So that one failure neither stops unrelated work nor produces pull requests built on nothing.

**Acceptance Criteria:**

**Given** a Task in a Stack that becomes `Failed` with Tasks depending on it
**When** each dependent Task comes up for dispatch
**Then** Hephaestus decides whether it can still be done by standing in for the missing piece (FR-40)

**Given** a dependent Task that can proceed
**When** it is published
**Then** its pull request states what was stood in for

**Given** a pull request built on a failed Task
**When** it is published
**Then** it stays a draft until the Task below it is fixed, and is marked ready once the Task below passes

**Given** a dependent Task that cannot proceed
**When** the decision is made
**Then** work on that Stack stops, the Owner is notified with the reason, and Tasks outside the Stack continue

**Given** the failed Task
**When** a Trusted User opts it in again and it passes
**Then** work on the Stack above it resumes with no further action

### Story 11.8: See Stacks in the Dashboard

As the Owner,
I want each Stack shown in order,
So that I can see what builds on what before I review.

**Acceptance Criteria:**

**Given** a Project with Stacks
**When** the Owner opens its Stacks tab
**Then** each Stack is shown bottom to top with every Task's state chip, its pull request link, and its position (FR-67, FR-39)

**Given** a pull request in a Stack
**When** it is shown on Pull requests or on its Task
**Then** it says whether it is stacked or independent, on which pull request, and why (FR-38)

**Given** a pull request held as a draft above a failed Task, or a Stack whose update raised an Escalation
**When** the Stack is shown
**Then** it says so on the affected pull request, with the hint for where to act (FR-40, UX-DR-9)

**Given** a Stack that is merged from, reworked, or joined while the Dashboard is open
**When** the loop records it
**Then** the Stack's order and states change without a reload (UX-DR-11)

## Epic 12: Task authoring aid

The Owner writes issues that pass the Readiness Check, with an issue template a Project can adopt and a helper skill that works with both Agents.

### Story 12.1: Offer an issue template that matches the Readiness Check

As the Owner,
I want an issue template that asks for what a Task needs,
So that the issues I write pass the Readiness Check the first time.

**Acceptance Criteria:**

**Given** the Hephaestus repository
**When** its assets are read
**Then** it provides a GitHub issue template that prompts for the business requirements and the acceptance criteria, with a place for `Depends-On` lines (FR-56)

**Given** an issue written with the template and every section filled in
**When** it is compared with the `default` `Readiness` template
**Then** it contains each section that template looks for, and a test fails if the two drift apart

**Given** the documentation
**When** an Owner wants to adopt the template
**Then** it explains how to add it to a Project by hand; Hephaestus does not write it, because `.github/**` is a protected path (AD-19)

**Given** a Project that has not adopted the template
**When** its issues are opted in
**Then** they are handled as before; the template is never required

### Story 12.2: Provide a skill that turns a rough idea into a well-formed issue

As the Owner,
I want a helper skill for my coding agent that shapes a rough idea into an issue draft,
So that writing a good Task takes minutes.

**Acceptance Criteria:**

**Given** the Hephaestus repository
**When** its assets are read
**Then** it provides one authoring skill installable for Claude Code and for Codex CLI from the same source (FR-57)

**Given** a rough idea
**When** the Owner runs the skill with either Agent
**Then** it produces an issue draft with the sections of the template from Story 12.1

**Given** a draft
**When** the skill finishes
**Then** the Owner is shown the draft and decides whether an issue is created; the skill creates nothing without that decision

**Given** an issue created from a draft
**When** its labels are read
**Then** the skill has not applied the Marker Label, and its instructions forbid it to

**Given** the skill
**When** it is used
**Then** it runs in the Owner's own agent session; it adds no runtime module to Hephaestus and needs no running installation
