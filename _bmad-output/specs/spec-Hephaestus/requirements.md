# Requirements

The functional requirements (FR-1 to FR-69) behind each capability in `SPEC.md`, and the cross-cutting requirements (NFR-1 to NFR-18). Ids are stable; the architecture spine binds to them.

Each entry gives the requirement, its release in brackets, and its testable consequences as sub-bullets. **Note** lines carry refinements from the architecture spine or the PRD addendum. Release allocation is summarized in `release-scope.md`.

## CAP-1 — Project setup and configuration

Behavior settings are versioned and reviewed with the code and take effect only once merged. Everything that decides who is trusted, what secrets exist, and when work happens stays with the installation. Realizes UJ-6.

- **FR-1 — Register a Project** [v0.1]. The Owner can register a GitHub repository as a Project.
  - Issues in repositories that are not registered are never acted on, whatever labels they carry.
  - Removing a Project stops new Tasks from starting there; history of past Tasks is retained.
- **FR-2 — Project Configuration in the repository** [v0.1]. Each Project defines its Project Configuration in a version-controlled file in its repository.
  - Hephaestus reads Project Configuration only from the Base Branch; a change on a Task's branch has no effect until it is merged.
  - A Task already running keeps the settings it started with.
  - When the Project Configuration is missing or invalid, no Task starts in that Project and the Owner is told which setting is wrong and why.
- **FR-3 — Installation Configuration outside the repository** [v0.1]. The Owner can view and change the Installation Configuration from the command line.
  - No change to any repository can add a Trusted User, alter a credential, change a Schedule, or raise a limit.
  - Installation Configuration survives restarts and upgrades.
- **FR-4 — Secrets stay secret** [v0.1]. Credentials are never written to a repository, an issue or pull request comment, a pull request description, or stored logs.
  - A search of repository contents, GitHub comments, pull request descriptions, and stored logs for any configured credential value finds nothing.
  - Agent output that contains a credential value is masked before it is stored or posted.
- **FR-5 — Technology-agnostic Projects** [v0.1]. A Project in any language or framework that can be built and tested in a Linux container can be added through configuration alone.
  - Adding such a Project requires no change to Hephaestus's code.
  - The environment a Project's Tasks run in is chosen per Project.
  - Out of scope: Windows-only execution environments in v1.
  - **Note:** the execution environment needs only Linux with glibc or musl and `bash`; Hephaestus supplies the Agent CLIs and the certificates they need. A build tool that does not honor the standard proxy variables (Maven, Gradle) needs its proxy settings supplied by the Project. Installation documentation lists them.
  - **Note (addendum):** the execution environment is a per-Project abstraction (image and platform), so a Windows execution image can be added later without touching the orchestration core.

## CAP-2 — Task intake

An issue becomes a Task when a Trusted User applies the Marker Label. Without further labels the Project's defaults apply. Realizes UJ-1, UJ-5.

- **FR-6 — Opt-in by Marker Label from a Trusted User** [v0.1]. An issue becomes a Task only when the Marker Label was applied by a Trusted User.
  - The Marker Label applied by anyone else does not create a Task, and the attempt is recorded.
  - Removing the Marker Label before work starts withdraws the Task.
- **FR-7 — Per-Task selection through Role Labels** [v0.1]. A Trusted User can choose a Task's Primary Agent and Fallback Agent, with their Models, by applying Role Labels such as `primary:opus5.5` and `fallback:sol6.2`, and can choose the Effort the same way, for example `effort:medium`.
  - A Task with only the Marker Label runs with the Project's defaults.
  - A new Model becomes selectable by adding an Alias to configuration, with no change to Hephaestus.
  - A Role Label naming an unknown Alias, or two Role Labels for the same role, stops the Task from starting and is reported on the issue.
  - **Note (spine AD-13):** `primary` and `fallback` must resolve to different Agents, or the Task does not start.
- **FR-8 — Queue order** [v0.1]. Tasks carrying a Priority Label are worked first; otherwise the oldest issue comes first. Declared dependencies are always respected.
  - Of two ready Tasks, the one with a Priority Label starts first; of two without one, the lower issue number starts first.
  - A Task whose dependency is not yet satisfied (FR-10) is passed over, not started out of order.
- **FR-9 — Readiness Check** [v0.1]. Before implementation, Hephaestus assesses whether the issue is clear enough to implement.
  - When the issue is clear enough, implementation starts without waiting for the Owner.
  - When it is not, Hephaestus raises an Escalation with specific questions and takes the next Task (FR-48).
- **FR-10 — Declared dependencies** [v0.1]. An issue can declare that its Task depends on another Task.
  - A Task is not started before every Task it depends on is satisfied. Until Stacks exist (v0.3), a dependency is satisfied when its pull request has been merged into the Base Branch; from v0.3, when it has produced a branch to build on.
  - A declared dependency overrides any automatic judgement about independence.
  - **Note (spine):** the declaration is a line `Depends-On: #<issue-number>` in the issue body, one per dependency.
  - **Note (owner decision):** only a merge by a Trusted User, or by Hephaestus after an approval, satisfies a dependency. A pull request merged by anyone else cancels its Task, with the reason reported on the issue; Hephaestus never reverts the merge. A Trusted User accepts such a merge with `/hephaestus confirm` on the pull request, which makes the Task count as merged.
- **FR-11 — Task state visible on the issue** [v0.1]. The current state of a Task (queued, in progress, waiting for the Owner, pull request open, failed, merged) is visible on its issue without opening Hephaestus.
  - The state shown changes within one polling cycle of the event that caused it.
  - The Owner can list all Tasks in a given state using GitHub's own issue filters.
  - **Note (spine):** the full state set adds waiting for capacity, cancelled, withdrawn, and closed; states are shown as `hephaestus:<state>` labels.

## CAP-3 — Scheduling

Each Project has its own working days and hours, so the Owner can dedicate different days to different Projects. Realizes UJ-1, UJ-6.

- **FR-12 — Per-Project Schedule** [v0.2]. The Owner can set, per Project, the days of the week and the start time at which Hephaestus works on it, with an optional end time.
  - Outside a Project's Schedule, no new Task from that Project starts.
  - Without an end time, work continues until the Queue is empty, every remaining Task is waiting, or no Agent has capacity.
- **FR-13 — Soft end time** [v0.2]. A Task running when the end time passes is finished; no new Task starts after it.
  - With an end time of 22:00, a Task that finishes at 22:30 completes normally and no Task starts after it.
- **FR-14 — One Task at a time** [v0.1]. At most one Task executes at any moment across the whole installation.
  - With Tasks ready in two Projects whose Schedules overlap, one runs and the other starts only after it ends.
- **FR-15 — Work in progress comes first** [v0.2]. A Task that was interrupted is resumed and finished before a new Task is started, including when another Project's Schedule has begun in the meantime.
  - After an interruption, the next Task to run is the interrupted one, even when a different Project's Schedule is now active.
- **FR-16 — Start on demand** [v0.1]. The Owner can start work on a Project outside its Schedule.
  - A start on demand works the Project's Queue under the same rules as a scheduled window and ends when the Queue is empty or the Owner stops it.

## CAP-4 — Workspace and execution

The container, not the Agent's own restraint, is what keeps a Task away from the Owner's files and other Projects. Realizes UJ-1, UJ-4.

- **FR-17 — Isolated Workspace per Task** [v0.1]. Each Task runs in a dedicated Workspace created from a clean state of its starting branch.
  - A Task cannot read or modify files outside its Workspace, including other Projects and the host's personal files.
  - A Task has no access to the container runtime's control interface.
- **FR-18 — Least credentials** [v0.1]. A Workspace receives only the credentials its Agent needs to run, and never a GitHub credential.
  - Every push, pull request, comment, label change, and merge is performed by Hephaestus outside the Workspace.
- **FR-19 — Bounded execution** [v0.1]. Every Agent run and every Validation command has a time limit.
  - A run that exceeds its limit is stopped and recorded as a failure with its output preserved.
- **FR-20 — Incomplete work is preserved** [v0.1]. When a Task is interrupted by a Usage Limit, a timeout, a crash, or a restart, the work done so far is kept and remains available for continuation and inspection.
  - After any such interruption, the Task's branch holds every change made before it, and a continuing Agent starts from that state, not from the starting branch.
- **FR-21 — Recovery after restart** [v0.1]. After Hephaestus restarts, every Task continues from its recorded state.
  - A restart never produces a duplicate pull request, a duplicate comment, or a second Workspace for the same Task.
- **FR-22 — Safe cleanup** [v0.1]. A Workspace is removed only after its Task has ended and its work is safely in the repository.
  - A Workspace holding work that is not yet in the repository is never removed automatically.
  - Removing a Workspace affects nothing outside it, including other Tasks' Workspaces.
- **FR-23 — Cancel a Task** [v0.2]. The Owner can cancel a Task that is queued or in progress.
  - A cancelled Task stops within a bounded time, its work so far is preserved (FR-20), and its issue shows it as cancelled.
  - A cancelled Task does not start again unless a Trusted User opts it in again.

## CAP-5 — Agents

Agents are interchangeable workers with differing capabilities. Realizes UJ-4.

- **FR-24 — Two Agents in v1** [v0.1]. Hephaestus supports Claude Code and Codex CLI, using only officially documented execution and authentication mechanisms.
  - A Task can be implemented by either Agent and reviewed by the other with no change to the Project.
  - A capability that one Agent lacks is reported as unavailable for that Agent; it is not imitated silently.
  - Out of scope: unofficial authentication, credential extraction, or any method intended to circumvent provider restrictions.
  - **Note (addendum):** integration is by CLI child process (`claude -p --output-format stream-json`, `codex exec --json`). The Claude Agent SDK and the Agent Client Protocol are not used.
- **FR-25 — Owner-chosen authentication mode** [v0.1]. For each Agent, the Owner chooses whether it authenticates with an API key or with a subscription login, within what the provider officially permits.
  - Sign-in always completes through the Agent's own tool; Hephaestus never presents a provider's sign-in itself and only uses the credential the Owner supplies to their own installation.
  - Installation documentation presents both modes on an equal footing and states, for each, the provider terms that apply and the risk of using a subscription for automated work.
  - **Note (spine):** Claude Code is supported with an API key and with a subscription sign-in, the latter through a token from the vendor's `claude setup-token`. Codex CLI is supported with an API key only: its subscription credential is a file holding a full account credential that must be kept and refreshed between runs, and the tools the account brings cannot be listed from the CLI.
  - **Note (addendum):** subscription sign-in for unattended use is a policy risk, not a settled permission. Anthropic's terms reserve it for ordinary individual use and allow enforcement without notice.
- **FR-26 — Agent availability check** [v0.1]. Before using an Agent, Hephaestus verifies that it is installed and authenticated.
  - An unavailable Agent is reported to the Owner with the reason and is treated as having no capacity.
- **FR-27 — Usage Limit detection** [v0.2]. Hephaestus recognizes when an Agent stopped because of a Usage Limit and distinguishes this from other failures.
  - A failure that cannot be classified is recorded as unknown and is never treated as a Usage Limit.
  - Where the Agent reports when the limit resets, Hephaestus records that time and uses it to decide when to resume.
- **FR-28 — Handoff to the Fallback Agent** [v0.2]. When the working Agent cannot continue, Hephaestus hands the Task to the Fallback Agent with the requirements, acceptance criteria, plan, current changes, completed and remaining work, and Validation and Review results so far.
  - The receiving Agent inspects and validates the partial work before continuing.
  - A Handoff does not depend on transferring conversation history between providers.
  - Each Handoff is recorded with its cause.
- **FR-29 — Wait and resume** [v0.2]. When no Agent has capacity, the Task waits and resumes automatically once capacity returns, with no action from the Owner.
  - The waiting Task's issue shows that it is waiting for capacity and, where known, until when.
  - Work resumes within one polling cycle after capacity returns.
  - **Note (spine AD-20):** a Task waiting for capacity does not block other Tasks.
- **FR-30 — Attribution** [v0.1]. For every step of a Task, Hephaestus records which Agent, Model, and Effort performed it, and usage information where the Agent provides it.
  - From a Task's history the Owner can tell, for any commit in its pull request, which Agent and Model produced it.

## CAP-6 — Validation, Review, and Correction

An Agent saying it is finished proves nothing. Realizes UJ-1, UJ-4.

- **FR-31 — Validation decides** [v0.1]. The Project's Validation commands are run on the Task's changes, and their results determine whether the work is acceptable.
  - A Task whose Validation fails is never presented as successful, whatever the Agent reported.
- **FR-32 — Independent Review** [v0.1]. Every Task's changes are reviewed by an Agent other than the one that wrote the latest changes.
  - With two Agents, authorship and Review alternate between them.
  - When the only other Agent has no capacity, the Task waits; it is not reviewed by its own author.
  - Out of scope: random Reviewer selection among three or more Agents.
- **FR-33 — Classified Findings** [v0.1]. Each Finding is classified as blocking or non-blocking. Non-blocking Findings are reported in the pull request and do not hold the Task.
  - A Task with only non-blocking Findings proceeds to a pull request, and each such Finding appears in its description.
- **FR-34 — Bounded Correction Loop** [v0.1]. Blocking Findings and failed Validation are returned to the implementing Agent for correction, for at most a configured number of rounds, two by default.
  - When the bound is reached with blocking Findings or failing Validation remaining, the loop stops and FR-35 applies.
  - Each round is recorded with what was returned to the Agent and what changed.
- **FR-35 — Failed Task outcome** [v0.1]. When the Correction Loop ends without passing Validation and Review, Hephaestus opens a draft pull request describing what was attempted, what still fails, and which Findings remain open.
  - The draft pull request is linked to its issue, the Task's state shows as failed, and the Owner is notified (FR-50).
  - **Note (spine):** a Task that fails with no commit ahead of its start commit gets an issue comment and no pull request.

## CAP-7 — Pull requests and Stacks

A Task that does not depend on pending work stays outside the Stack. Realizes UJ-1, UJ-3.

- **FR-36 — Pull request per Task** [v0.1]. Hephaestus opens one pull request per Task, linked to its issue.
  - The description states what changed, which Agent implemented and which reviewed, the Validation results, and any non-blocking Findings.
- **FR-37 — Stacking mode per Project** [v0.3]. Each Project chooses one of three modes: Tasks stack in Queue order, Tasks stack only on declared dependencies, or Tasks do not stack at all.
  - Changing the mode affects Tasks started afterwards and leaves existing Stacks as they are.
  - A Project that sets no mode stacks only on declared dependencies.
  - With stacking off, a dependent Task waits until the Task it depends on is merged, as in v0.1 and v0.2 (FR-10).
- **FR-38 — Independent Tasks stay independent** [v0.3]. In Queue-order mode, a Task that does not depend on pending work gets its own branch from the Base Branch and a pull request against it.
  - The pull request states whether the Task was stacked or independent, and why.
- **FR-39 — Stack navigation** [v0.3]. Every pull request in a Stack links to the previous and the next pull request in that Stack, and the links are kept current as the Stack changes.
  - After a pull request joins, is merged from, or leaves a Stack, every remaining pull request's links point to its actual neighbors.
- **FR-40 — Tasks above a failed Task** [v0.3]. When a Task in a Stack fails, Hephaestus decides for each dependent Task whether it can still be done.
  - A dependent Task that can proceed by standing in for the missing piece does so, and its pull request states what was stood in for.
  - A pull request built on a failed Task stays a draft until the Task below it is fixed.
  - Otherwise, work on that Stack stops and the Owner is notified; unrelated Tasks continue.

## CAP-8 — Approval and merge

Hephaestus performs the merge so that approved work lands, in order, without the Owner doing it by hand. Realizes UJ-2.

- **FR-41 — Merge after approval** [v0.3]. At the start of a Project's scheduled window, Hephaestus merges each pull request that a Trusted User approved.
  - A pull request is merged only if the approval covers its latest commit and the repository's checks pass.
  - A pull request that fails any condition is left open and the Owner is told which condition failed.
- **FR-42 — Stack order** [v0.3]. Pull requests in a Stack are merged in Stack order, and nothing above an unapproved pull request is merged.
  - In a Stack of three where only the second is approved, nothing is merged and the Owner is told the first awaits approval.
- **FR-43 — Stack stays readable after a merge** [v0.3]. After a pull request in a Stack is merged, the remaining pull requests show only their own changes and their issue links and navigation remain correct.
  - After the first pull request of a Stack is merged, the second targets the Base Branch and its diff contains only its own Task's changes.
  - **Note (addendum):** a stacked pull request targets its parent's branch until the parent merges. Squash merges cause conflicts in dependent branches; the update mechanics are decided in the v0.3 epic.
- **FR-44 — Hard limits** [v0.3]. Hephaestus never merges without an explicit approval from a Trusted User, never deploys, and never rewrites or deletes history on the Base Branch.
  - No combination of labels, comments, or issue content causes a merge without an approving review from a Trusted User.
  - Hephaestus's GitHub identity is never used to force-push to or delete the Base Branch.

## CAP-9 — Rework

Ordinary comments never trigger work. Realizes UJ-3.

- **FR-45 — Rework on command** [v0.3]. A Trusted User can start a Rework of an open pull request by issuing the Rework Command.
  - Comments without the Rework Command start no work.
  - A Rework Command from anyone who is not a Trusted User is ignored.
- **FR-46 — Rework is checked like any other work** [v0.3]. Reworked changes pass Validation and independent Review before the pull request is presented again. There is no limit on the number of Reworks.
  - A reworked pull request shows new Validation results and a new Review of the reworked changes before the Owner is asked to look again.
- **FR-47 — Stack follows a Rework** [v0.3]. After a Rework, every pull request above it in the Stack is updated to include the change.
  - When an update cannot be completed cleanly, Hephaestus raises an Escalation instead of guessing.

## CAP-10 — Escalation and Notification

The alert only says that attention is needed; the conversation stays on the issue or pull request. Realizes UJ-5.

- **FR-48 — Escalation** [v0.1]. When Hephaestus cannot proceed with a Task, it posts a specific question as a comment on the issue or pull request, marks the Task as waiting for the Owner, and takes the next Task.
  - The question names the decision needed and the options Hephaestus sees, and the Owner is notified (FR-50).
  - While one Task waits, other ready Tasks continue to be worked.
- **FR-49 — Answer resumes the Task** [v0.1]. A reply from a Trusted User returns the Task to the Queue with the answer as part of its context.
  - A reply from anyone who is not a Trusted User does not resume the Task.
- **FR-50 — Notifications** [v0.2]. The Owner can be notified by email, by phone push, or both.
  - A Notification is sent when an Escalation is raised and when a Task fails.
  - A summary Notification is sent when a scheduled window ends: pull requests opened, Tasks failed, Escalations waiting.
  - A Notification links to the place where the Owner acts.
  - **Note (addendum):** channels to evaluate are a user-supplied SMTP server and ntfy; both satisfy the free-components rule.
- **FR-51 — Notification failure does not stop work** [v0.2]. When a Notification cannot be delivered, the failure is recorded and work continues.
  - A Task's outcome is the same whether or not its Notifications were delivered.

## CAP-11 — Visibility and history

- **FR-52 — Progress on the issue** [v0.1]. Hephaestus reports a Task's progress as comments on its issue at each significant step.
  - A comment is posted when work starts, when Validation and Review complete, on each Handoff and Escalation, and when the pull request opens or the Task fails.
  - Each comment says what happened and what comes next.
- **FR-53 — Durable execution history** [v0.1]. For every Task, Hephaestus retains each step, its Agent, timing, Validation results, Findings, Handoffs, Escalations, and the Agents' output.
  - The history survives restarts and remains available after the Workspace is removed.
- **FR-54 — Status on demand** [v0.2]. The Owner can ask Hephaestus, from the command line, what it is doing now, what is queued, and what is waiting.
  - The answer names the running Task and its current step, each waiting Task with what it waits for, and the order of queued Tasks.
- **FR-55 — Real metrics only** [v0.2]. Hephaestus derives the metrics in `success-metrics.md` from actual executions only and shows them to the Owner from the command line and on the Dashboard's statistics page (FR-67).
  - Each figure can be traced to the Tasks it was computed from.
  - A metric with no data is shown as having none, not as zero.

## CAP-12 — Task authoring aid

The quality of a Task's result depends on the quality of its issue.

- **FR-56 — Issue template** [v0.3]. A Project can adopt an issue template that prompts for the business requirements and acceptance criteria a Task needs.
  - An issue written with the template contains the sections the Readiness Check looks for.
- **FR-57 — Authoring skill** [v0.3]. The Owner can use a helper skill with their coding agent to turn a rough idea into a well-formed issue.
  - The skill works with both supported Agents.
  - Its result is an issue draft the Owner reviews before it is created; the skill never applies the Marker Label.

## CAP-13 — Distribution and versioning

- **FR-58 — Container-based installation** [v0.1]. Hephaestus is distributed as container images and runs with Docker on Linux containers, on a developer machine or a server.
  - Running Hephaestus requires no paid Hephaestus service and no mandatory cloud service.
  - **Note (addendum):** the developer machine, including a Windows host, is only the Docker host. Images are built for linux/amd64 and linux/arm64.
- **FR-59 — Semantic versioning** [v0.1]. Hephaestus releases follow Semantic Versioning (MAJOR.MINOR.PATCH).
  - The versioned public surface is the configuration format, the label and comment-command conventions, the command-line interface, and the container image tags.
  - Steps before a release are published as pre-release versions (for example `0.3.0-alpha.2`, `0.3.0-rc.1`).
  - Every release has a changelog entry that groups changes by kind: added, changed, fixed, removed, security.
  - While the version is 0.x, a minor release may contain breaking changes, and its changelog says so.
  - A merge to `main` publishes the version the repository states; a pull request to `main` cannot merge with a version that is not valid, not higher than the last one, or missing from the changelog.
  - A merge to `develop` publishes a moving `develop` image. It is a preview channel, not a release, and not part of the versioned public surface.
- **FR-60 — Safe upgrades** [v0.1]. Upgrading Hephaestus preserves stored configuration and execution history.
  - When stored data cannot be carried over to the new version, the upgrade stops and says why; data is never half-converted.

## CAP-14 — Methodology

Hephaestus supplies scheduling, Agent selection, state, Handoff, Validation, and approval; the Methodology supplies how the work inside a step is done. Realizes UJ-1, UJ-4, UJ-7.

- **FR-61 — Methodology per Project** [v0.1]. Each Project selects its Methodology in its Project Configuration.
  - A Project that selects none uses the built-in default Methodology.
  - Selecting a Methodology that is unknown or not available in the Project stops Tasks from starting and is reported to the Owner.
- **FR-62 — One standard for every Agent** [v0.1]. For a given step in a given Project, every Agent receives the same Methodology instructions.
  - After a Handoff, the receiving Agent continues under the same Methodology and from the same shared artifacts as the Agent it replaced.
  - The Reviewer assesses changes against the same Methodology the implementer worked to.
  - Each step's record states which Methodology and which of its instructions were applied.
- **FR-63 — BMAD Method support** [v0.1]. A Project that uses BMAD Method can have its Tasks planned, implemented, and reviewed with BMAD's workflows.
  - The same BMAD workflow is used for a step whether Claude Code or Codex CLI performs it.
  - Only workflows that can run without human input are used; where one needs a decision, Hephaestus raises an Escalation (FR-48).
  - **Note:** which BMAD workflows complete unattended under each Agent is unverified and needs the BMAD spike before work on this feature starts.
  - **Note (addendum):** BMAD skills call `uv` and Python scripts under `_bmad/`; the execution image of a BMAD Project must provide them.
  - **Note (addendum, spine):** one BMAD install writes the same skills for both Agents (`.claude/skills/` and `.agents/skills/`). Hardened Claude Code offers no `Skill` tool and neither Agent reads a repository instruction file, so a BMAD workflow is started from its files; to verify in the BMAD spike.
- **FR-64 — Methodology is optional and replaceable** [v0.1]. Hephaestus works on a Project that has no Methodology installed, and adding support for another Methodology requires no change to how Tasks are scheduled, validated, or approved.
  - A Project with no Methodology files in its repository completes a Task from issue to pull request using the built-in default.
  - **Note (addendum):** the built-in default is plain role prompts per step.
- **FR-65 — Planning within a single Task** [v0.1]. Before implementing a Task, Hephaestus can have an Agent plan that one Task with the Methodology's planning workflow. The Readiness Check decides: a Task that is clear but large is planned first; a small one goes straight to implementation.
  - The plan is part of the Task's pull request, so the Reviewer and the Owner see what the implementation was meant to do.
  - Planning never reaches beyond the Task: it produces no product-level requirements, architecture, or backlog.
  - A planning question only the Owner can answer becomes an Escalation.
  - **Note (spine AD-12):** the plan is stored by Hephaestus and rendered into the pull request description; it is not committed.
- **FR-66 — Issues from Stories** [v0.1]. The Owner can have Hephaestus create GitHub issues from the Stories in a Project's Methodology documents. Realizes UJ-7.
  - Each created issue links to its Story and carries the Story's requirements and acceptance criteria.
  - Order and dependencies between Stories become declared dependencies between the issues (FR-10).
  - Stories are read from the Base Branch only.
  - A created issue does not carry the Marker Label; a Trusted User still opts each one in (FR-6).
  - Running it again creates issues only for Stories that have none.
  - **Note (addendum):** the Story file stays the artifact the BMAD build workflow works from; the issue is the trigger and the conversation thread.
  - **Note (spine):** issue creation is a journaled effect; a hidden marker carries a stable Story identity declared by the Methodology manifest; nothing is written back to the Base Branch. Matching across runs and handling of a changed Story are open.

## CAP-15 — Dashboard

- **FR-67 — Read-only Dashboard** [v0.1; grows with each release]. The Owner can open a Dashboard in a browser and follow Hephaestus there. In v0.1 it shows the registered Projects, the Installation Configuration without any secret value, each Project's Tasks with their current state, and each Task's history. Each release adds views for what that release introduces (`release-scope.md`).
  - No view and no request the Dashboard makes changes a Task, a Project, or any configuration.
  - Everything shown is read from the same records the command line reads; the two never disagree.
  - No secret value is shown. Agent output is shown as stored, that is, masked (FR-4).
  - Text that came from GitHub or from an Agent is shown as text and is never interpreted as markup or script.
  - A value that does not exist is shown as none, not as zero.
  - A change to what is on screen appears without the Owner reloading the page.
  - The Dashboard works with no connection to any host other than Hephaestus itself.
  - The Dashboard is not part of the versioned public surface (FR-59) in these releases.
  - **Note (spine AD-17):** the Dashboard learns of changes from one stream of Server-Sent Events that names what changed by scope and id, never by content, and then reads through the ordinary read endpoints. Output of a step is not shown while the step runs.
- **FR-68 — Dashboard access** [v0.1]. The Dashboard can be reached only from the machine that runs Hephaestus, and only with a Dashboard Token. The Owner issues, lists, and revokes tokens from the command line.
  - A request without a valid token returns no data.
  - A revoked token stops working at once.
  - The Dashboard is not reachable from another machine or from a Workspace.
  - The token value is shown once, when issued, and is not stored in readable form.
  - Out of scope: access from other machines, encryption in transit, and more than one user.
  - **Note (spine AD-17):** the listener the Dashboard uses maps reads only, so a token cannot change anything. The token travels in a request header, never in a cookie or an address. Revoking a token closes the streams opened with it.

## CAP-16 — Language

- **FR-69 — Language** [v0.1]. Hephaestus speaks English by default, and the Owner can choose Polish.
  - The Owner sets the installation's language from the command line. The Dashboard, the command line's sentences and table headers, Notifications, and the text Hephaestus writes on GitHub follow it.
  - A Project can name its own language for the text Hephaestus writes on its issues and pull requests.
  - The Dashboard's language can also be switched in the browser.
  - Text an Agent writes for the Owner to read is written in the Task's language.
  - Nothing a program reads changes with the language: command names and options, machine-readable output, labels, comment commands, dependency declarations, and configuration keys are the same in every language.
  - No sentence is missing in a supported language: a build that lacks a translation fails.
  - Out of scope: languages other than English and Polish; translating the Glossary's terms.
  - **Note (spine AD-21):** every sentence is a message id resolved from resources for both languages. The Task's language travels in `task.json`, and instruction templates, which stay single-language, tell the Agent to write prose in it. State names and Glossary terms are not translated.

## Cross-cutting requirements

### Security

Agents run commands and read content that Hephaestus cannot fully trust. These hold across every capability.

- **NFR-1 — Trusted input only.** An Agent receives the issue body of its Task, which the Trusted User who applied the Marker Label vouches for, and comments written by Trusted Users. Nothing written by anyone else reaches an Agent. An issue body edited by someone who is not a Trusted User after the Marker Label was applied is not used until a Trusted User confirms it.
  - **Note (owner decision):** an edit is detected by comparing the content's hash with the snapshot taken when it was admitted. The editor is read from GitHub's edit history; the candidate source is GraphQL `userContentEdits`, to be confirmed by a test with the App's token. When the editor cannot be established, the content is held until a Trusted User issues `/hephaestus confirm`.
- **NFR-2 — Agents cannot act on GitHub.** No Agent holds a GitHub credential (FR-18).
- **NFR-3 — Containment.** A Task cannot reach the host's files, other Projects, or the container runtime's control interface (FR-17). Hephaestus does not weaken an Agent's own safety mechanisms outside a Workspace.
- **NFR-4 — Network egress.** Outbound network access from a Workspace can be restricted per Project to an allowlist. In v0.1 access is open unless a list is set; the allowlist becomes the default before Hephaestus is announced for general use.
  - **Note (owner decision):** the workspace network is a Docker internal network whose only exit is a separate, unprivileged egress proxy container. The proxy enforces both network rules. It refuses any destination that resolves to a loopback, link-local, or private address (AD-7), and it applies the Project's allowlist by host name; with no allowlist it passes any public host.
  - **Note (owner decision):** allowed hosts are set per step. The hosts an Agent's CLI needs are declared by its adapter and allowed automatically, only in the container that runs that Agent; a Validation container gets the Project's list alone. A Project never lists vendor hosts.
  - **Note (owner decision):** a tool that cannot use an HTTP proxy has no network access from a Workspace. Hephaestus sets the proxy variables in every step container, with loopback excluded, and makes git fetch `github.com` over HTTPS where an SSH address is given. Proxy settings specific to one ecosystem belong in the Project's image and in the installation documentation. A host the proxy refused is recorded with the step. Out for v1: traffic that is not HTTP or HTTPS, and intercepting traffic without the client's cooperation.
  - **Note (owner decision):** the proxy never runs inside the orchestrator process, which holds the Docker socket. A step container reaches it at one fixed, documented host name and port, which is part of the versioned public surface. Which proxy is used, how it is started, and that address are left to the v0.1 execution epic.
- **NFR-5 — Resource and spend limits.** Each Project has limits on Task duration, processor, and memory. When an Agent runs on an API key, a spend limit stops new work and notifies the Owner once reached. Where an Agent reports no usage data, the spend limit cannot be enforced and Hephaestus says so.
- **NFR-6 — Least privilege on GitHub.** Hephaestus's own GitHub identity has only the permissions its features need, and only on registered Projects.
- **NFR-7 — Audit trail.** Every action Hephaestus takes that is visible outside it — push, pull request, comment, label change, merge — is recorded with its cause.
- **NFR-8 — Honest security claims.** Documentation states the security assumptions and known limitations. Hephaestus is not described as secure beyond what has been tested.

### Reliability

- **NFR-9 — Idempotent external actions.** Repeating a step after a failure or restart never duplicates its outside effects (FR-21).
- **NFR-10 — Everything is bounded.** Every retry, loop, and wait that Hephaestus performs on its own has a limit or an explicit resume condition. No automatic cycle can run indefinitely.
- **NFR-11 — Classified failures.** Usage Limit, timeout, Agent error, Validation failure, infrastructure error, and unknown are distinguished in history and in what the Owner is told.
- **NFR-12 — Outage tolerance.** When GitHub or an Agent's provider is unreachable, Hephaestus retries later without losing state.

### Data and observability

- **NFR-13 — Retention.** Task history, summaries, and metrics are kept indefinitely. Raw Agent output is kept for a configurable period, 30 days by default, with an option to keep it permanently.
- **NFR-14 — Observable operation.** Hephaestus's own operation can be followed through structured logs.

### Openness and footprint

- **NFR-15 — Free and open-source components.** Hephaestus depends only on free and open-source libraries and tools. Agents and their costs are the Owner's own. This governs what an installation needs in order to run; services used only to build and check Hephaestus's own repository are not dependencies of an installation.
- **NFR-16 — Light footprint.** A default installation runs on one machine with Docker and requires no additional server.
- **NFR-17 — English.** Source code, documentation, and public materials are written in English. What Hephaestus says to the Owner while running follows the language the Owner chose (FR-69).

### Engineering quality

- **NFR-18 — Quality gate on Hephaestus's own code.** A change reaches `develop` or `main` only through a pull request that passes the build, every test, static analysis, and test coverage of at least 80%, measured both over all code and over the code the change adds. This holds for changes Hephaestus makes to itself.
  - **Note (spine conventions):** the build itself fails below 80% over all code; SonarQube Cloud's built-in quality gate requires 80% on new code. No analysis token is stored: the workflow obtains a short-lived credential by secretless analysis.

### Identity and tone

Hephaestus presents itself professionally, with restrained references to craftsmanship, the forge, and its mythological namesake — for example in release names such as "Ignite the Forge". The references never get in the way of clarity.

## Reference

Details of both Agent CLIs and of GitHub's API behavior are in the research report at `_bmad-output/planning-artifacts/research/technical-agent-clis-and-github-integration-for-he-2026-11-25/research.md`. It is reference material, not contract; where it differs from the architecture spine, the spine wins.
