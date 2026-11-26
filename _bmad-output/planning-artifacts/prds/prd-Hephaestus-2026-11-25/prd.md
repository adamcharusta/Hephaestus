---
title: "Hephaestus — Product Requirements Document"
status: final
created: 2026-11-25
updated: 2026-11-26
---

# PRD: Hephaestus

*Forge software. Orchestrate intelligence.*

## 0. Document Purpose

This PRD defines what Hephaestus must do and for whom. It is written for the project owner, for future contributors, and for the downstream BMAD workflows that will produce the architecture, the epics and stories, and the implementation.

The source brief was `init.md`, removed from the repository on 2026-11-25 once this PRD had absorbed it. Where the two disagreed, this PRD won; every such departure is recorded in the run's decision log.

Vocabulary is anchored in the Glossary (§3). Requirements are grouped by feature with globally numbered identifiers (FR-N). Technical mechanisms, rejected alternatives, and unverified facts about third-party tools live in `addendum.md`, not here.

## 1. Vision

Hephaestus is an open-source, self-hosted orchestrator that turns GitHub issues into reviewed, merge-ready pull requests by driving coding agents the user already has. It is not a coding agent, a chatbot, an IDE, or a model provider. It is the layer above them: it decides what runs, when, with which agent, in which isolated environment, and what has to be true before the work counts as done.

Today a developer starts an agent, describes a task, and watches it work. That scales to one task at a time and only while the developer is present. Hephaestus removes the developer from the loop between "task described" and "pull request ready". The developer writes issues, labels them, and returns later to a set of pull requests that have already been built, tested, and reviewed by a second, independent agent. When an agent runs out of usage, Hephaestus hands the task to another agent or waits for capacity. When it cannot proceed without a decision, it asks, alerts the developer, and moves on to other work. Whichever agent does a step, it works to the same methodology, so moving a task between providers changes who does the work, not how it is done.

The developer stays responsible for intent and for acceptance: nothing is merged without an explicit human approval, and nothing is ever deployed. Hephaestus is built first for its owner's own daily work, published under the MIT license for anyone who finds it useful, and designed to work on any project that can be built and tested in a container.

## 2. Target User

### 2.1 Jobs To Be Done

- **Delegate without supervising.** When I have several well-described tasks, I want them worked on while I do something else, so my attention is spent on decisions and review, not on watching an agent type.
- **Trust the result before I read it.** I want every pull request to arrive already built, tested, and reviewed by a different model than the one that wrote it, so my review starts from a higher baseline.
- **Use the subscriptions I already pay for, fully.** When one agent hits its usage limit, I want work to continue on another or resume on its own when the limit resets, without me restarting anything.
- **Get the same way of working from every agent.** When work moves between agents from different providers, I want it done to one standard, so a handoff or a review does not turn into two styles in one pull request.
- **Be interrupted only when it matters.** When the system needs a decision, I want a clear question and an alert on my phone, not a silent stall and not a guess.
- **Keep control.** I want my explicit approval to be the only way anything reaches the main line of my repository.
- **Run it myself.** I want this on my own machine or server, with free and open-source dependencies, with no mandatory cloud service.
- **Use it on anything.** I want to add a project in a different language or framework through configuration, not by changing Hephaestus.

### 2.2 Non-Users (v1)

- Teams that need multiple user accounts, roles, or permission management.
- Users looking for a hosted service or a managed cloud offering.
- Users who want a coding agent or a model; Hephaestus requires agents the user already has access to.
- Projects that can only be built or tested on Windows. Linux containers are the only execution platform in v1; Windows execution environments are a possible later addition.

### 2.3 Key User Journeys

The protagonist throughout is **Adam**, an experienced engineer working alone on several personal projects, with a Claude subscription and an OpenAI subscription, running Hephaestus in Docker on his own machine.

- **UJ-1. Adam queues a day of work and reviews it in the evening.**
  Adam writes five issues in his project's repository, each with business requirements and acceptance criteria. He applies the marker label to each, and on some adds labels choosing the primary agent, the fallback agent, and the reasoning effort; the rest use the project's defaults. The project is scheduled for Monday, Wednesday, and Friday at 09:00. On Wednesday at 09:00 Hephaestus picks up the first task, prepares an isolated workspace and a branch, runs the primary agent, runs the project's build and test commands, has the other agent review the result, lets the implementer correct blocking findings within a bounded loop, and opens a pull request. It then takes the next task; because that task builds on the first, its branch starts from the first task's branch. A task that does not depend on pending work gets its own branch from the base branch instead. At 22:00 Adam finds five pull requests, each stating which agents wrote and reviewed it, what was validated, and where it sits in the stack, with links to the previous and next pull request. **Edge case:** the last task is still running when the schedule's end time passes; Hephaestus finishes it and starts nothing new.

- **UJ-2. Adam approves, and Hephaestus merges.**
  Adam reads the five pull requests and approves three. At the start of the project's next scheduled window, Hephaestus merges each approved pull request, in stack order, provided the approval came from a trusted user, covers the latest commit, and the repository's checks pass. It then continues with the queue. **Edge case:** the second pull request in a stack is approved but the first is not; Hephaestus merges nothing from that stack above the unapproved one and reports why.

- **UJ-3. Adam asks for changes in the middle of a stack.**
  On the third pull request Adam leaves review comments and issues the rework command. Hephaestus ignores ordinary comments; the explicit command is what triggers it. In the next window it reworks the third pull request according to the comments, re-runs validation and independent review, and then updates the fourth and fifth so they include the change. Adam can repeat this as many times as he wants; each round happens only on his command.

- **UJ-4. The primary agent runs out of usage.**
  Midway through a task the primary agent reports that its usage limit is reached. Hephaestus records the state of the work and hands the task to the fallback agent, giving it the requirements, the plan, the current diff, and the validation results so far. The fallback agent inspects the partial work before continuing. Because the reviewer must be a different agent than the one that wrote the latest changes, the review falls to the first agent once its limit resets. **Edge case:** no agent has capacity; the task waits and resumes automatically when a limit resets, with no action from Adam.

- **UJ-5. Hephaestus gets stuck and asks.**
  An issue turns out to be ambiguous on a point that changes the implementation. Hephaestus posts a specific question as a comment on the issue, marks the task as waiting for Adam, and sends an alert by email or phone push saying a decision is needed. It then takes the next task in the queue. Adam answers in a comment from his phone an hour later; Hephaestus picks the task back up with his answer.

- **UJ-6. Adam adds a second project.**
  Adam registers another repository, gives it its own defaults (agents, validation commands, base branch, stacking mode) and its own schedule (Tuesday and Thursday), and from then on the two projects are worked on their own days with no change to Hephaestus itself.

- **UJ-7. Adam turns a planned epic into Tasks.**
  Adam has planned a new capability for one of his projects with BMAD Method, on his own, and the resulting stories are merged into the repository. He asks Hephaestus to create issues from them. Each story becomes an issue that links back to the story, carries its acceptance criteria, and declares which earlier story it depends on. Nothing starts yet. Adam reads the issues, applies the marker label to the four he wants done this week, and leaves the rest. From there the day runs as in UJ-1, with every agent working to the same BMAD workflows. **Edge case:** one story is large; Hephaestus has the agent plan that single Task first and includes the plan in the pull request.

## 3. Glossary

- **Owner** — The human who operates a Hephaestus installation and makes all approval decisions. One per installation in v1.
- **Project** — A GitHub repository registered with Hephaestus. An installation has one or more Projects.
- **Project Configuration** — The version-controlled file in a Project's repository that defines how Hephaestus works on it: default Agents and Effort, Methodology, Validation commands, stacking mode, Correction Loop bound, and execution environment. Read only from the Base Branch.
- **Installation Configuration** — Settings held by the Hephaestus installation and never stored in a repository: registered Projects with their Base Branch and Schedules, Trusted Users, Agent credentials, Aliases, limits, protected paths, and Notification channels.
- **Role Label** — A label of the form `role:alias` that assigns an Alias to a role on a Task, for example `primary:opus5.5` or `fallback:sol6.2`.
- **Priority Label** — A label that moves a Task ahead of others in the Queue.
- **Queue** — The ordered set of a Project's Tasks that are ready to be worked on.
- **Readiness Check** — The assessment, made before implementation starts, of whether a Task's issue is clear enough to implement without asking the Owner.
- **Task** — A GitHub issue in a Project that carries the Marker Label applied by a Trusted User. One issue is one Task.
- **Marker Label** — The label that opts an issue in to Hephaestus. The only mandatory label on a Task.
- **Alias** — A name defined in configuration that resolves to an Agent with a Model, or to an Effort. Aliases let the Owner choose per Task without Hephaestus knowing model names in advance.
- **Trusted User** — A GitHub user whose label, approval, answer, or command Hephaestus acts on. Actions by anyone else are ignored.
- **Agent** — A coding agent that Hephaestus drives through its officially supported non-interactive interface (in v1: Claude Code and Codex CLI). An Agent may start its own sub-agents; Hephaestus does not manage those.
- **Model** — The underlying language model an Agent is asked to use. Distinct from the Agent.
- **Effort** — The reasoning effort requested from the Model for a Task.
- **Primary Agent** — The Agent that starts work on a Task.
- **Fallback Agent** — The Agent that takes over a Task when the Primary Agent cannot continue.
- **Reviewer** — The Agent that performs the Review of a Task. Always a different Agent than the one that wrote the latest changes.
- **Methodology** — A named set of instructions that defines how an Agent performs each step of a Task: Readiness Check, implementation, Review, correction, and Rework. A Project uses one Methodology, and every Agent working on it receives the same one. BMAD Method is the first supported Methodology besides the built-in default.
- **Story** — A unit of planned work described in a Project's Methodology documents; in BMAD Method, a story within an epic. Hephaestus can create one issue per Story.
- **Usage Limit** — A provider-imposed cap on an Agent's use that, once reached, prevents further work until it resets.
- **Handoff** — The transfer of a Task in progress from one Agent to another, carried by durable artifacts and repository state rather than by conversation history.
- **Schedule** — A Project's working time: days of the week, a start time, and an optional end time. The end time stops new Tasks from starting; it does not interrupt a running one.
- **Workspace** — The isolated environment in which a single Task is worked on: a container with its own checkout of the Project on the Task's branch.
- **Base Branch** — The Project branch that independent Tasks branch from and that approved pull requests are ultimately merged into. Set when the Project is registered.
- **Stack** — An ordered chain of Tasks in which each Task's branch starts from the previous Task's branch. A Task that does not depend on pending work is not part of a Stack.
- **Validation** — The deterministic commands a Project configures (for example build and test) whose results decide whether work is acceptable. An Agent's own claim of completion is never Validation.
- **Review** — The Reviewer's independent assessment of a Task's changes, producing Findings.
- **Finding** — A single issue raised by a Review, classified as blocking or non-blocking.
- **Correction Loop** — The automatic, bounded cycle in which the implementing Agent resolves blocking Findings and Validation and Review run again.
- **Rework** — A revision of an open pull request made in response to the Owner's review comments. Starts only on a Rework Command and has no round limit.
- **Rework Command** — The explicit keyword or tag a Trusted User places on a pull request to start a Rework.
- **Escalation** — Hephaestus asking the Owner a question in an issue or pull request comment because it cannot proceed. The Task waits for the answer while other Tasks continue.
- **Notification** — An alert (email or phone push) telling the Owner that an Escalation or another event needs attention. A Notification never carries the conversation; answers are given on GitHub.
- **Dashboard** — The web interface through which the Owner follows Hephaestus in a browser. It shows; it changes nothing.
- **Dashboard Token** — A credential the Owner issues and revokes from the command line. A browser must present it to read anything from the Dashboard.

## 4. Features

### 4.1 Project Setup and Configuration

**Description:** The Owner registers a repository as a Project and describes, in a file inside that repository, how Hephaestus should work on it. Behavior settings are versioned and reviewed with the code and take effect only once merged. Everything that decides who is trusted, what secrets exist, and when work happens stays with the installation, out of reach of anything an Agent or an outside contributor can change. Realizes UJ-6.

#### FR-1: Register a Project

The Owner can register a GitHub repository as a Project.

**Consequences (testable):**
- Issues in repositories that are not registered are never acted on, whatever labels they carry.
- Removing a Project stops new Tasks from starting there; history of past Tasks is retained.

#### FR-2: Project Configuration in the repository

Each Project defines its Project Configuration in a version-controlled file in its repository.

**Consequences (testable):**
- Hephaestus reads Project Configuration only from the Base Branch; a change on a Task's branch has no effect until it is merged.
- A Task already running keeps the settings it started with.
- When the Project Configuration is missing or invalid, no Task starts in that Project and the Owner is told which setting is wrong and why.

#### FR-3: Installation Configuration outside the repository

The Owner can view and change the Installation Configuration from the command line.

**Consequences (testable):**
- No change to any repository can add a Trusted User, alter a credential, change a Schedule, or raise a limit.
- Installation Configuration survives restarts and upgrades.

#### FR-4: Secrets stay secret

Credentials are never written to a repository, an issue or pull request comment, a pull request description, or stored logs.

**Consequences (testable):**
- A search of repository contents, GitHub comments, pull request descriptions, and stored logs for any configured credential value finds nothing.
- Agent output that contains a credential value is masked before it is stored or posted.

#### FR-5: Technology-agnostic Projects

A Project in any language or framework that can be built and tested in a Linux container can be added through configuration alone.

**Consequences (testable):**
- Adding such a Project requires no change to Hephaestus's code.
- The environment a Project's Tasks run in is chosen per Project.

**Out of Scope:** Windows-only execution environments in v1.

**Notes:** The execution environment needs only Linux with glibc or musl and `bash`; Hephaestus supplies the Agent CLIs and the certificates they need. A build tool that does not honor the standard proxy variables (Maven, Gradle) needs its proxy settings supplied by the Project. Installation documentation lists them.

### 4.2 Task Intake

**Description:** An issue becomes a Task when a Trusted User applies the Marker Label. Further labels choose the Agents, Model, and Effort for that Task; without them the Project's defaults apply. Before any code is written, Hephaestus checks that the issue is clear enough to act on. Realizes UJ-1, UJ-5.

#### FR-6: Opt-in by Marker Label from a Trusted User

An issue becomes a Task only when the Marker Label was applied by a Trusted User.

**Consequences (testable):**
- The Marker Label applied by anyone else does not create a Task, and the attempt is recorded.
- Removing the Marker Label before work starts withdraws the Task.

#### FR-7: Per-Task selection through Role Labels

A Trusted User can choose a Task's Primary Agent and Fallback Agent, with their Models, by applying Role Labels such as `primary:opus5.5` and `fallback:sol6.2`, and can choose the Effort the same way. Effort uses the same form, for example `effort:medium`.

**Consequences (testable):**
- A Task with only the Marker Label runs with the Project's defaults.
- A new Model becomes selectable by adding an Alias to configuration, with no change to Hephaestus.
- A Role Label naming an unknown Alias, or two Role Labels for the same role, stops the Task from starting and is reported on the issue.

#### FR-8: Queue order

Tasks carrying a Priority Label are worked first; otherwise the oldest issue comes first. Declared dependencies are always respected.

**Consequences (testable):**
- Of two ready Tasks, the one with a Priority Label starts first; of two without one, the lower issue number starts first.
- A Task whose dependency is not yet satisfied (FR-10) is passed over, not started out of order.

#### FR-9: Readiness Check

Before implementation, Hephaestus assesses whether the issue is clear enough to implement.

**Consequences (testable):**
- When the issue is clear enough, implementation starts without waiting for the Owner.
- When it is not, Hephaestus raises an Escalation with specific questions and takes the next Task (FR-48).

#### FR-10: Declared dependencies

An issue can declare that its Task depends on another Task.

**Consequences (testable):**
- A Task is not started before every Task it depends on is satisfied. Until Stacks exist (v0.3), a dependency is satisfied when its pull request has been merged into the Base Branch; from v0.3, when it has produced a branch to build on.
- A declared dependency overrides any automatic judgement about independence.

#### FR-11: Task state visible on the issue

The current state of a Task (queued, in progress, waiting for the Owner, pull request open, failed, merged) is visible on its issue without opening Hephaestus.

**Consequences (testable):**
- The state shown changes within one polling cycle of the event that caused it.
- The Owner can list all Tasks in a given state using GitHub's own issue filters.

### 4.3 Scheduling

**Description:** Each Project has its own working days and hours, so the Owner can dedicate different days to different Projects. Hephaestus works one Task at a time. Realizes UJ-1, UJ-6.

#### FR-12: Per-Project Schedule

The Owner can set, per Project, the days of the week and the start time at which Hephaestus works on it, with an optional end time.

**Consequences (testable):**
- Outside a Project's Schedule, no new Task from that Project starts.
- Without an end time, work continues until the Queue is empty, every remaining Task is waiting, or no Agent has capacity.

#### FR-13: Soft end time

A Task running when the end time passes is finished; no new Task starts after it.

**Consequences (testable):**
- With an end time of 22:00, a Task that finishes at 22:30 completes normally and no Task starts after it.

#### FR-14: One Task at a time

At most one Task executes at any moment across the whole installation.

**Consequences (testable):**
- With Tasks ready in two Projects whose Schedules overlap, one runs and the other starts only after it ends.

#### FR-15: Work in progress comes first

A Task that was interrupted is resumed and finished before a new Task is started, including when another Project's Schedule has begun in the meantime.

**Consequences (testable):**
- After an interruption, the next Task to run is the interrupted one, even when a different Project's Schedule is now active.

#### FR-16: Start on demand

The Owner can start work on a Project outside its Schedule.

**Consequences (testable):**
- A start on demand works the Project's Queue under the same rules as a scheduled window and ends when the Queue is empty or the Owner stops it.

### 4.4 Workspace and Execution

**Description:** Every Task is worked on in its own Workspace: a container with its own checkout on the Task's branch. The container, not the Agent's own restraint, is what keeps a Task away from the Owner's files and other Projects. Work is never lost on interruption. Realizes UJ-1, UJ-4.

#### FR-17: Isolated Workspace per Task

Each Task runs in a dedicated Workspace created from a clean state of its starting branch.

**Consequences (testable):**
- A Task cannot read or modify files outside its Workspace, including other Projects and the host's personal files.
- A Task has no access to the container runtime's control interface.

#### FR-18: Least credentials

A Workspace receives only the credentials its Agent needs to run, and never a GitHub credential.

**Consequences (testable):**
- Every push, pull request, comment, label change, and merge is performed by Hephaestus outside the Workspace.

#### FR-19: Bounded execution

Every Agent run and every Validation command has a time limit.

**Consequences (testable):**
- A run that exceeds its limit is stopped and recorded as a failure with its output preserved.

#### FR-20: Incomplete work is preserved

When a Task is interrupted by a Usage Limit, a timeout, a crash, or a restart, the work done so far is kept and remains available for continuation and inspection.

**Consequences (testable):**
- After any such interruption, the Task's branch holds every change made before it, and a continuing Agent starts from that state, not from the starting branch.

#### FR-21: Recovery after restart

After Hephaestus restarts, every Task continues from its recorded state.

**Consequences (testable):**
- A restart never produces a duplicate pull request, a duplicate comment, or a second Workspace for the same Task.

#### FR-22: Safe cleanup

A Workspace is removed only after its Task has ended and its work is safely in the repository.

**Consequences (testable):**
- A Workspace holding work that is not yet in the repository is never removed automatically.
- Removing a Workspace affects nothing outside it, including other Tasks' Workspaces.

#### FR-23: Cancel a Task

The Owner can cancel a Task that is queued or in progress.

**Consequences (testable):**
- A cancelled Task stops within a bounded time, its work so far is preserved (FR-20), and its issue shows it as cancelled.
- A cancelled Task does not start again unless a Trusted User opts it in again.

### 4.5 Agents

**Description:** Hephaestus drives Agents the Owner already has, through their officially supported non-interactive interfaces, and treats them as interchangeable workers with differing capabilities. When an Agent cannot continue, the Task moves to another or waits. Realizes UJ-4.

#### FR-24: Two Agents in v1

Hephaestus supports Claude Code and Codex CLI, using only officially documented execution and authentication mechanisms.

**Consequences (testable):**
- A Task can be implemented by either Agent and reviewed by the other with no change to the Project.
- A capability that one Agent lacks is reported as unavailable for that Agent; it is not imitated silently.

**Out of Scope:** unofficial authentication, credential extraction, or any method intended to circumvent provider restrictions.

#### FR-25: Owner-chosen authentication mode

For each Agent, the Owner chooses whether it authenticates with an API key or with a subscription login, within what the provider officially permits.

**Consequences (testable):**
- Sign-in always completes through the Agent's own tool; Hephaestus never presents a provider's sign-in itself and only uses the credential the Owner supplies to their own installation.
- Installation documentation presents both modes on an equal footing and states, for each, the provider terms that apply and the risk of using a subscription for automated work.

**Notes:** Claude Code is supported with an API key and with a subscription sign-in, the latter through a token from the vendor's `claude setup-token`. Codex CLI is supported with an API key only: its subscription credential is a file holding a full account credential that must be kept and refreshed between runs, and the tools the account brings cannot be listed from the CLI. See the architecture spine, Open Question 3, and the injection spike report.

#### FR-26: Agent availability check

Before using an Agent, Hephaestus verifies that it is installed and authenticated.

**Consequences (testable):**
- An unavailable Agent is reported to the Owner with the reason and is treated as having no capacity.

#### FR-27: Usage Limit detection

Hephaestus recognizes when an Agent stopped because of a Usage Limit and distinguishes this from other failures.

**Consequences (testable):**
- A failure that cannot be classified is recorded as unknown and is never treated as a Usage Limit.
- Where the Agent reports when the limit resets, Hephaestus records that time and uses it to decide when to resume.

#### FR-28: Handoff to the Fallback Agent

When the working Agent cannot continue, Hephaestus hands the Task to the Fallback Agent with the requirements, acceptance criteria, plan, current changes, completed and remaining work, and Validation and Review results so far.

**Consequences (testable):**
- The receiving Agent inspects and validates the partial work before continuing.
- A Handoff does not depend on transferring conversation history between providers.
- Each Handoff is recorded with its cause.

#### FR-29: Wait and resume

When no Agent has capacity, the Task waits and resumes automatically once capacity returns, with no action from the Owner.

**Consequences (testable):**
- The waiting Task's issue shows that it is waiting for capacity and, where known, until when.
- Work resumes within one polling cycle after capacity returns.

#### FR-30: Attribution

For every step of a Task, Hephaestus records which Agent, Model, and Effort performed it, and usage information where the Agent provides it.

**Consequences (testable):**
- From a Task's history the Owner can tell, for any commit in its pull request, which Agent and Model produced it.

### 4.6 Validation, Review, and Correction

**Description:** An Agent saying it is finished proves nothing. A Task is acceptable only when the Project's Validation commands pass and a different Agent has reviewed the changes. Blocking Findings go back to the implementer within a bounded loop. Realizes UJ-1, UJ-4.

#### FR-31: Validation decides

The Project's Validation commands are run on the Task's changes, and their results determine whether the work is acceptable.

**Consequences (testable):**
- A Task whose Validation fails is never presented as successful, whatever the Agent reported.

#### FR-32: Independent Review

Every Task's changes are reviewed by an Agent other than the one that wrote the latest changes.

**Consequences (testable):**
- With two Agents, authorship and Review alternate between them.
- When the only other Agent has no capacity, the Task waits; it is not reviewed by its own author.

**Out of Scope:** random Reviewer selection among three or more Agents (later release).

#### FR-33: Classified Findings

Each Finding is classified as blocking or non-blocking. Non-blocking Findings are reported in the pull request and do not hold the Task.

**Consequences (testable):**
- A Task with only non-blocking Findings proceeds to a pull request, and each such Finding appears in its description.

#### FR-34: Bounded Correction Loop

Blocking Findings and failed Validation are returned to the implementing Agent for correction, for at most a configured number of rounds, two by default.

**Consequences (testable):**
- When the bound is reached with blocking Findings or failing Validation remaining, the loop stops and FR-35 applies.
- Each round is recorded with what was returned to the Agent and what changed.

#### FR-35: Failed Task outcome

When the Correction Loop ends without passing Validation and Review, Hephaestus opens a draft pull request describing what was attempted, what still fails, and which Findings remain open.

**Consequences (testable):**
- The draft pull request is linked to its issue, the Task's state shows as failed, and the Owner is notified (FR-50).

### 4.7 Pull Requests and Stacks

**Description:** Each Task ends in a pull request that tells the Owner what was done and how it was checked. To keep working while pull requests await review, a Project can let Tasks build on one another in a Stack. A Task that does not depend on pending work stays outside the Stack. Realizes UJ-1, UJ-3.

#### FR-36: Pull request per Task

Hephaestus opens one pull request per Task, linked to its issue.

**Consequences (testable):**
- The description states what changed, which Agent implemented and which reviewed, the Validation results, and any non-blocking Findings.

#### FR-37: Stacking mode per Project

Each Project chooses one of three modes: Tasks stack in Queue order, Tasks stack only on declared dependencies, or Tasks do not stack at all.

**Consequences (testable):**
- Changing the mode affects Tasks started afterwards and leaves existing Stacks as they are.
- A Project that sets no mode stacks only on declared dependencies.
- With stacking off, a dependent Task waits until the Task it depends on is merged, as in v0.1 and v0.2 (FR-10).

#### FR-38: Independent Tasks stay independent

In Queue-order mode, a Task that does not depend on pending work gets its own branch from the Base Branch and a pull request against it.

**Consequences (testable):**
- The pull request states whether the Task was stacked or independent, and why.

#### FR-39: Stack navigation

Every pull request in a Stack links to the previous and the next pull request in that Stack, and the links are kept current as the Stack changes.

**Consequences (testable):**
- After a pull request joins, is merged from, or leaves a Stack, every remaining pull request's links point to its actual neighbors.

#### FR-40: Tasks above a failed Task

When a Task in a Stack fails, Hephaestus decides for each dependent Task whether it can still be done.

**Consequences (testable):**
- A dependent Task that can proceed by standing in for the missing piece does so, and its pull request states what was stood in for.
- A pull request built on a failed Task stays a draft until the Task below it is fixed.
- Otherwise, work on that Stack stops and the Owner is notified; unrelated Tasks continue.

### 4.8 Approval and Merge

**Description:** The Owner's approval is the only way into the Base Branch. Hephaestus performs the merge so that approved work lands, in order, without the Owner doing it by hand. Realizes UJ-2.

#### FR-41: Merge after approval

At the start of a Project's scheduled window, Hephaestus merges each pull request that a Trusted User approved.

**Consequences (testable):**
- A pull request is merged only if the approval covers its latest commit and the repository's checks pass.
- A pull request that fails any condition is left open and the Owner is told which condition failed.

#### FR-42: Stack order

Pull requests in a Stack are merged in Stack order, and nothing above an unapproved pull request is merged.

**Consequences (testable):**
- In a Stack of three where only the second is approved, nothing is merged and the Owner is told the first awaits approval.

#### FR-43: Stack stays readable after a merge

After a pull request in a Stack is merged, the remaining pull requests show only their own changes and their issue links and navigation remain correct.

**Consequences (testable):**
- After the first pull request of a Stack is merged, the second targets the Base Branch and its diff contains only its own Task's changes.

#### FR-44: Hard limits

Hephaestus never merges without an explicit approval from a Trusted User, never deploys, and never rewrites or deletes history on the Base Branch.

**Consequences (testable):**
- No combination of labels, comments, or issue content causes a merge without an approving review from a Trusted User.
- Hephaestus's GitHub identity is never used to force-push to or delete the Base Branch.

### 4.9 Rework

**Description:** When the Owner wants changes to an open pull request, an explicit command starts a revision. Ordinary comments never trigger work. Realizes UJ-3.

#### FR-45: Rework on command

A Trusted User can start a Rework of an open pull request by issuing the Rework Command.

**Consequences (testable):**
- Comments without the Rework Command start no work.
- A Rework Command from anyone who is not a Trusted User is ignored.

#### FR-46: Rework is checked like any other work

Reworked changes pass Validation and independent Review before the pull request is presented again. There is no limit on the number of Reworks.

**Consequences (testable):**
- A reworked pull request shows new Validation results and a new Review of the reworked changes before the Owner is asked to look again.

#### FR-47: Stack follows a Rework

After a Rework, every pull request above it in the Stack is updated to include the change.

**Consequences (testable):**
- When an update cannot be completed cleanly, Hephaestus raises an Escalation instead of guessing.

### 4.10 Escalation and Notification

**Description:** When Hephaestus needs a decision, it asks on GitHub, alerts the Owner, and keeps working on something else. The alert only says that attention is needed; the conversation stays on the issue or pull request. Realizes UJ-5.

#### FR-48: Escalation

When Hephaestus cannot proceed with a Task, it posts a specific question as a comment on the issue or pull request, marks the Task as waiting for the Owner, and takes the next Task.

**Consequences (testable):**
- The question names the decision needed and the options Hephaestus sees, and the Owner is notified (FR-50).
- While one Task waits, other ready Tasks continue to be worked.

#### FR-49: Answer resumes the Task

A reply from a Trusted User returns the Task to the Queue with the answer as part of its context.

**Consequences (testable):**
- A reply from anyone who is not a Trusted User does not resume the Task.

#### FR-50: Notifications

The Owner can be notified by email, by phone push, or both.

**Consequences (testable):**
- A Notification is sent when an Escalation is raised and when a Task fails.
- A summary Notification is sent when a scheduled window ends: pull requests opened, Tasks failed, Escalations waiting.
- A Notification links to the place where the Owner acts.

#### FR-51: Notification failure does not stop work

When a Notification cannot be delivered, the failure is recorded and work continues.

**Consequences (testable):**
- A Task's outcome is the same whether or not its Notifications were delivered.

### 4.11 Visibility and History

**Description:** The Owner follows work on the issue itself and can reconstruct afterwards exactly what happened and why. The Dashboard (§4.15) shows the same records in a browser.

#### FR-52: Progress on the issue

Hephaestus reports a Task's progress as comments on its issue at each significant step.

**Consequences (testable):**
- A comment is posted when work starts, when Validation and Review complete, on each Handoff and Escalation, and when the pull request opens or the Task fails.
- Each comment says what happened and what comes next.

#### FR-53: Durable execution history

For every Task, Hephaestus retains each step, its Agent, timing, Validation results, Findings, Handoffs, Escalations, and the Agents' output.

**Consequences (testable):**
- The history survives restarts and remains available after the Workspace is removed.

#### FR-54: Status on demand

The Owner can ask Hephaestus, from the command line, what it is doing now, what is queued, and what is waiting.

**Consequences (testable):**
- The answer names the running Task and its current step, each waiting Task with what it waits for, and the order of queued Tasks.

#### FR-55: Real metrics only

Hephaestus derives the metrics in §8 from actual executions only and shows them to the Owner from the command line and on the Dashboard's statistics page (FR-67).

**Consequences (testable):**
- Each figure can be traced to the Tasks it was computed from.
- A metric with no data is shown as having none, not as zero.

### 4.12 Task Authoring Aid

**Description:** The quality of a Task's result depends on the quality of its issue. Hephaestus helps the Owner write issues that pass the Readiness Check.

#### FR-56: Issue template

A Project can adopt an issue template that prompts for the business requirements and acceptance criteria a Task needs.

**Consequences (testable):**
- An issue written with the template contains the sections the Readiness Check looks for.

#### FR-57: Authoring skill

The Owner can use a helper skill with their coding agent to turn a rough idea into a well-formed issue.

**Consequences (testable):**
- The skill works with both supported Agents.
- Its result is an issue draft the Owner reviews before it is created; the skill never applies the Marker Label.

### 4.13 Distribution and Versioning

**Description:** Hephaestus is installed and run with Docker, depends only on free and open-source components, and is released under a clear versioning policy so that others can rely on it.

#### FR-58: Container-based installation

Hephaestus is distributed as container images and runs with Docker on Linux containers, on a developer machine or a server.

**Consequences (testable):**
- Running Hephaestus requires no paid Hephaestus service and no mandatory cloud service.

#### FR-59: Semantic versioning

Hephaestus releases follow Semantic Versioning (MAJOR.MINOR.PATCH).

**Consequences (testable):**
- The versioned public surface is the configuration format, the label and comment-command conventions, the command-line interface, and the container image tags.
- Steps before a release are published as pre-release versions (for example `0.3.0-alpha.2`, `0.3.0-rc.1`).
- Every release has a changelog entry that groups changes by kind: added, changed, fixed, removed, security.
- While the version is 0.x, a minor release may contain breaking changes, and its changelog says so.
- A merge to `main` publishes the version the repository states; a pull request to `main` cannot merge with a version that is not valid, not higher than the last one, or missing from the changelog.
- A merge to `develop` publishes a moving `develop` image. It is a preview channel, not a release, and not part of the versioned public surface.

#### FR-60: Safe upgrades

Upgrading Hephaestus preserves stored configuration and execution history.

**Consequences (testable):**
- When stored data cannot be carried over to the new version, the upgrade stops and says why; data is never half-converted.

#### FR-69: Language

Hephaestus speaks English by default, and the Owner can choose Polish.

**Consequences (testable):**
- The Owner sets the installation's language from the command line. The Dashboard, the command line's sentences and table headers, Notifications, and the text Hephaestus writes on GitHub follow it.
- A Project can name its own language for the text Hephaestus writes on its issues and pull requests.
- The Dashboard's language can also be switched in the browser.
- Text an Agent writes for the Owner to read is written in the Task's language.
- Nothing a program reads changes with the language: command names and options, machine-readable output, labels, comment commands, dependency declarations, and configuration keys are the same in every language.
- No sentence is missing in a supported language: a build that lacks a translation fails.

**Out of Scope:** languages other than English and Polish; translating the Glossary's terms.

### 4.14 Methodology

**Description:** Agents from different providers have different habits. A Methodology gives every Agent the same instructions for the same step, so that an implementation started by one Agent can be continued by another, and reviewed by a third, to one standard. Hephaestus supplies scheduling, Agent selection, state, Handoff, Validation, and approval; the Methodology supplies how the work inside a step is done. BMAD Method is the first Methodology supported beyond the built-in default. Realizes UJ-1, UJ-4.

#### FR-61: Methodology per Project

Each Project selects its Methodology in its Project Configuration.

**Consequences (testable):**
- A Project that selects none uses the built-in default Methodology.
- Selecting a Methodology that is unknown or not available in the Project stops Tasks from starting and is reported to the Owner.

#### FR-62: One standard for every Agent

For a given step in a given Project, every Agent receives the same Methodology instructions.

**Consequences (testable):**
- After a Handoff, the receiving Agent continues under the same Methodology and from the same shared artifacts as the Agent it replaced.
- The Reviewer assesses changes against the same Methodology the implementer worked to.
- Each step's record states which Methodology and which of its instructions were applied.

#### FR-63: BMAD Method support

A Project that uses BMAD Method can have its Tasks planned, implemented, and reviewed with BMAD's workflows.

**Consequences (testable):**
- The same BMAD workflow is used for a step whether Claude Code or Codex CLI performs it.
- Only workflows that can run without human input are used; where one needs a decision, Hephaestus raises an Escalation (FR-48).

**Notes:** Which BMAD workflows complete unattended under each Agent is unverified and needs a technical spike before v0.1 work on this feature starts. See Open Question 4.

#### FR-64: Methodology is optional and replaceable

Hephaestus works on a Project that has no Methodology installed, and adding support for another Methodology requires no change to how Tasks are scheduled, validated, or approved.

**Consequences (testable):**
- A Project with no Methodology files in its repository completes a Task from issue to pull request using the built-in default.

#### FR-65: Planning within a single Task

Before implementing a Task, Hephaestus can have an Agent plan that one Task with the Methodology's planning workflow. The Readiness Check decides: a Task that is clear but large is planned first; a small one goes straight to implementation.

**Consequences (testable):**
- The plan is part of the Task's pull request, so the Reviewer and the Owner see what the implementation was meant to do.
- Planning never reaches beyond the Task: it produces no product-level requirements, architecture, or backlog.
- A planning question only the Owner can answer becomes an Escalation.

#### FR-66: Issues from Stories

The Owner can have Hephaestus create GitHub issues from the Stories in a Project's Methodology documents. Realizes UJ-7.

**Consequences (testable):**
- Each created issue links to its Story and carries the Story's requirements and acceptance criteria.
- Order and dependencies between Stories become declared dependencies between the issues (FR-10).
- Stories are read from the Base Branch only.
- A created issue does not carry the Marker Label; a Trusted User still opts each one in (FR-6).
- Running it again creates issues only for Stories that have none.

**Notes:** How a Story is matched to its issue across runs, and what happens when a Story changes after its issue exists, are left to the architecture.

### 4.15 Dashboard

**Description:** The Owner can follow Hephaestus in a browser as well as on the issue and from the command line. The Dashboard starts in v0.1 as a small, read-only view and grows with each release, when that release gives the Owner something new to see. It never becomes a place where work is started, approved, or configured: those stay on GitHub and the command line.

#### FR-67: Read-only Dashboard

The Owner can open a Dashboard in a browser and follow Hephaestus there. In v0.1 it shows the registered Projects, the Installation Configuration without any secret value, each Project's Tasks with their current state, and each Task's history. Each release adds views for what that release introduces (§7).

**Consequences (testable):**
- No view and no request the Dashboard makes changes a Task, a Project, or any configuration.
- Everything shown is read from the same records the command line reads; the two never disagree.
- No secret value is shown. Agent output is shown as stored, that is, masked (FR-4).
- Text that came from GitHub or from an Agent is shown as text and is never interpreted as markup or script.
- A value that does not exist is shown as none, not as zero.
- A change to what is on screen appears without the Owner reloading the page.
- The Dashboard works with no connection to any host other than Hephaestus itself.

**Notes:** The Dashboard is not part of the versioned public surface (FR-59) in these releases; it ships with Hephaestus and always matches it.

#### FR-68: Dashboard access

The Dashboard can be reached only from the machine that runs Hephaestus, and only with a Dashboard Token. The Owner issues, lists, and revokes tokens from the command line.

**Consequences (testable):**
- A request without a valid token returns no data.
- A revoked token stops working at once.
- The Dashboard is not reachable from another machine or from a Workspace.
- The token value is shown once, when issued, and is not stored in readable form.

**Out of Scope:** access from other machines, encryption in transit, and more than one user.

## 5. Cross-Cutting Requirements

### 5.1 Security

Agents run commands and read content that Hephaestus cannot fully trust. These requirements hold across every feature.

- **NFR-1: Trusted input only.** An Agent receives the issue body of its Task, which the Trusted User who applied the Marker Label vouches for, and comments written by Trusted Users. Nothing written by anyone else reaches an Agent. An issue body edited by someone who is not a Trusted User after the Marker Label was applied is not used until a Trusted User confirms it.
- **NFR-2: Agents cannot act on GitHub.** No Agent holds a GitHub credential (FR-18).
- **NFR-3: Containment.** A Task cannot reach the host's files, other Projects, or the container runtime's control interface (FR-17). Hephaestus does not weaken an Agent's own safety mechanisms outside a Workspace.
- **NFR-4: Network egress.** Outbound network access from a Workspace can be restricted per Project to an allowlist. In v0.1 access is open unless a list is set; the allowlist becomes the default before Hephaestus is announced for general use.
- **NFR-5: Resource and spend limits.** Each Project has limits on Task duration, processor, and memory. When an Agent runs on an API key, a spend limit stops new work and notifies the Owner once reached. Where an Agent reports no usage data, the spend limit cannot be enforced and Hephaestus says so.
- **NFR-6: Least privilege on GitHub.** Hephaestus's own GitHub identity has only the permissions its features need, and only on registered Projects.
- **NFR-7: Audit trail.** Every action Hephaestus takes that is visible outside it — push, pull request, comment, label change, merge — is recorded with its cause.
- **NFR-8: Honest security claims.** Documentation states the security assumptions and known limitations. Hephaestus is not described as secure beyond what has been tested.

### 5.2 Reliability

- **NFR-9: Idempotent external actions.** Repeating a step after a failure or restart never duplicates its outside effects (FR-21).
- **NFR-10: Everything is bounded.** Every retry, loop, and wait that Hephaestus performs on its own has a limit or an explicit resume condition. No automatic cycle can run indefinitely.
- **NFR-11: Classified failures.** Usage Limit, timeout, Agent error, Validation failure, infrastructure error, and unknown are distinguished in history and in what the Owner is told.
- **NFR-12: Outage tolerance.** When GitHub or an Agent's provider is unreachable, Hephaestus retries later without losing state.

### 5.3 Data and Observability

- **NFR-13: Retention.** Task history, summaries, and metrics are kept indefinitely. Raw Agent output is kept for a configurable period, 30 days by default, with an option to keep it permanently.
- **NFR-14: Observable operation.** Hephaestus's own operation can be followed through structured logs.

### 5.4 Openness and Footprint

- **NFR-15: Free and open-source components.** Hephaestus depends only on free and open-source libraries and tools. Agents and their costs are the Owner's own. This governs what an installation needs in order to run; services used only to build and check Hephaestus's own repository are not dependencies of an installation.
- **NFR-16: Light footprint.** A default installation runs on one machine with Docker and requires no additional server.
- **NFR-17: English.** Source code, documentation, and public materials are written in English. What Hephaestus says to the Owner while running follows the language the Owner chose (FR-69).

### 5.5 Identity and Tone

Hephaestus presents itself professionally, with restrained references to craftsmanship, the forge, and its mythological namesake — for example in release names such as "Ignite the Forge". The references never get in the way of clarity.

### 5.6 Engineering Quality

- **NFR-18: Quality gate on Hephaestus's own code.** A change reaches `develop` or `main` only through a pull request that passes the build, every test, static analysis, and test coverage of at least 80%, measured both over all code and over the code the change adds. This holds for changes Hephaestus makes to itself.

## 6. Non-Goals (Explicit)

- **Not a coding agent, chatbot, IDE, or model provider.** Hephaestus drives Agents; it does not replace them or supply models.
- **Not a hosted service.** There is no Hephaestus cloud, account, or subscription, and no feature depends on one.
- **Not a way around provider rules.** No unofficial authentication, credential extraction, or circumvention of usage restrictions.
- **Not an autonomous releaser.** Hephaestus never merges without an explicit approval from a Trusted User and never deploys anything. It does not update or redeploy itself.
- **Not a manager of an Agent's internals.** Sub-agents an Agent starts on its own are that Agent's business.
- **Not a team product in v1.** No multiple user accounts, roles, or permissions.
- **Not a general-purpose workflow engine in v1.** The lifecycle of a Task is fixed; user-defined workflows are a later possibility.
- **Not a methodology of its own.** Hephaestus applies the Methodology a Project chooses and ships only a minimal default. BMAD Method is supported, never required.
- **Not a product planning tool.** Deciding what to build — briefs, product requirements, architecture, the backlog — stays with the Owner. Hephaestus plans only how to carry out a single Task.
- **Not a clone of existing orchestrators.** Matching the feature set of comparable products is not a goal.
- **Not a monetized product.** No billing, licensing tiers, or marketplace.

## 7. Release Scope

*Three releases, accepted by the owner. Each is usable on its own and builds on the previous one.*

### 7.1 v0.1 — Ignite the Forge: one issue to one reviewed pull request

One Project, started on demand. A labelled issue becomes a validated, independently reviewed pull request, and the Owner follows it on the issue.

- Project setup and configuration: FR-1 to FR-5.
- Task intake: FR-6 to FR-11. Declared dependencies (FR-10) are satisfied by a merge in this release.
- Execution: FR-14, FR-16 to FR-22.
- Agents: FR-24 to FR-26, FR-30.
- Validation, Review, Correction: FR-31 to FR-35.
- Pull request: FR-36.
- Escalation by comment: FR-48, FR-49.
- Visibility: FR-52, FR-53.
- Distribution: FR-58 to FR-60.
- Methodology, including BMAD Method support and issues from Stories: FR-61 to FR-66.
- Dashboard, read-only seed: FR-67, FR-68. It shows Projects, configuration, Tasks, and Task history.
- Language: FR-69.

### 7.2 v0.2 — Unattended: the forge runs on its own

- Schedules and several Projects: FR-12, FR-13, FR-15.
- Usage Limits: FR-27 to FR-29.
- Notifications: FR-50, FR-51.
- Control and insight: FR-23, FR-54, FR-55.
- The Dashboard (FR-67) gains Schedules and windows, Agent capacity and Handoffs, status, and the statistics page.

### 7.3 v0.3 — Closing the loop: stacks, merge, rework

- Stacks: FR-37 to FR-40. A declared dependency (FR-10) is now satisfied as soon as it has produced a branch.
- Approval and merge: FR-41 to FR-44.
- Rework: FR-45 to FR-47.
- Task authoring aid: FR-56, FR-57.
- The Dashboard (FR-67) gains pull requests awaiting approval and Stacks.

### 7.4 Out of Scope for all of the above

- Actions from the Dashboard: it stays read-only in all three releases.
- Dashboard access from another machine, and more than one Dashboard user.
- Multiple user accounts, roles and permissions.
- Agents other than Claude Code and Codex CLI.
- Task sources other than GitHub issues (Jira and similar come later).
- Windows execution environments.
- Parallel execution of Tasks.
- Distributed workers.
- Database engines other than the built-in one, and running against an existing database server.
- Random Reviewer selection among three or more Agents.
- User-defined workflows.
- Methodologies other than the built-in default and BMAD Method.
- Product-level planning with BMAD Method (brief, product requirements, architecture, creating the backlog).

## 8. Success Metrics

The Owner sets no numeric targets. Hephaestus shows every measure below, computed from real executions, and the Owner judges whether it is earning its place. Targets may be added after a month of daily use.

**Primary**
- **SM-1: Unassisted acceptance.** Share of pull requests merged without a Rework and without the Owner changing code by hand. Validates FR-31 to FR-36, FR-41.
- **SM-2: First-pass Validation.** Share of Tasks whose Validation passed before any Correction Loop round. Validates FR-9, FR-31.
- **SM-3: Owner interventions.** Escalations, cancellations, and manual restarts per Task. Validates FR-9, FR-27 to FR-29, FR-48.
- **SM-4: Review effort.** Review comments and Rework rounds per pull request. Validates FR-32 to FR-34.

**Secondary**
- **SM-5: Unattended windows.** Share of scheduled windows that ran to their end without needing the Owner. Validates FR-12, FR-29.
- **SM-6: Successful Handoffs.** Share of Handoffs after which the Task reached a pull request. Validates FR-28.
- **SM-7: Self-hosting.** Share of Hephaestus's own merged pull requests that Hephaestus produced.

**Counter-metrics (do not optimize)**
- **SM-C1: Rework rate.** Share of pull requests that needed at least one Rework. Counterbalances SM-1 and any pressure to open more pull requests.
- **SM-C2: Wasted review.** Pull requests closed without merging, and failed Tasks the Owner had to read. Counterbalances throughput.

The number of pull requests opened is deliberately not a success metric.

**Limitation:** the time the Owner spends reviewing and the time a Task would have taken by hand cannot be measured by Hephaestus. SM-4 is a proxy. Hephaestus never presents estimated or simulated figures as measurements.

## 9. Open Questions

1. What do Claude Code and Codex CLI actually emit when a real subscription Usage Limit is reached? Research found the signals undocumented or unconfirmed; a technical spike must answer this before FR-27 to FR-29 are designed in detail. (See the research report referenced in `addendum.md`.)
2. ~~What exact form does the Rework Command take: a comment keyword, a label, or a review state?~~ Settled by the architecture: a comment by a Trusted User whose first line is `/hephaestus rework`. Commands share that form and a closed set of verbs.
3. How is "this Task does not depend on pending work" determined when the issue declares no dependency?
4. Which BMAD workflows complete without human input under Claude Code and under Codex CLI, and how does a GitHub issue map to the artifacts BMAD works from (FR-63)?

## 10. Assumptions Index

None open. Every assumption raised during discovery was confirmed by the owner on 2026-11-25; the confirmations are recorded in the decision log.
