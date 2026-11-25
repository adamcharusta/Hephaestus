# Rubric Review — ARCHITECTURE-SPINE.md (Hephaestus, initiative altitude)

- **Reviewed:** `ARCHITECTURE-SPINE.md` (status: draft, 17 ADs), against `prd.md` (FR-1..FR-66, NFR-1..NFR-17), with `addendum.md` and the run memlog as context.
- **Date:** 2026-11-25
- **Reviewer stance:** independent; the spine was not edited.

## Verdict

The spine is strong on boundaries (dependency direction, credential placement, bundle boundary, journal, trust admission) and its technology pins check out, but it is **not yet safe to build from**: the contract every epic shares — the Signal inbox, the commit boundary around it, and the Task transition table — is underspecified or self-contradictory, and two Deferred rows hide decisions that v0.1 epics need.

**Counts:** 1 critical, 5 high, 19 medium, 12 low.

## What was verified

| Check | Result |
| --- | --- |
| Mermaid syntax | All 6 blocks parse with the Mermaid parser (`mermaid.parse`, package 12.3.0): 3 flowcharts, 1 state diagram, 1 sequence diagram, 1 ER diagram. No syntax findings. Consistency findings are listed below. |
| NuGet pins | Checked against the public NuGet index today: Octokit 14.0.0, Docker.DotNet.Enhanced 4.4.1, System.CommandLine 3.0.0, YamlDotNet 18.1.0, xunit.v3 4.0.3, NSubstitute 6.2.0, Testcontainers 4.17.0 are each the latest stable; Microsoft.EntityFrameworkCore.Sqlite 10.0.15 exists and is the latest 10.0.x. |
| State names | Every state in the lifecycle diagram is in the Task states convention row and vice versa. Every step node in the steps flowchart maps to a step kind in the convention row. |
| Not verified | .NET runtime 10.0.15, Docker Engine 29.x, Claude Code 2.1.x and Codex 0.171.0 were not checked independently (the memlog records sources for them). The Claude Code licence claim in H-5 is from my own knowledge and must be confirmed against the current licence text. |

---

## Critical

### C-1 — The Signal contract and the commit boundary around it are not fixed

- **Location:** AD-1 Rule; AD-3 port list (`ITaskStore`, `IEffectJournal`, `ISignalInbox`); Core entities diagram; Consistency Conventions (no Signal row).
- **Problem:** Signals are the one contract that four independently built units share: the GitHub poller, the local API, the Workspace runtime and the clock produce them, and the loop consumes them. The spine fixes none of the following:
  1. **Atomicity.** Consuming a Signal, changing the Task or step run, and recording effect intents go through three separate ports. Nothing says they commit together. A crash between them loses or re-applies a transition, which defeats FR-21 and NFR-9 — the properties AD-1 and AD-5 exist to deliver.
  2. **Deduplication.** A polling gateway sees the same label event, comment and review on every poll and again after a restart. With no deterministic source key, the same Rework Command or answer is applied twice.
  3. **Order.** "Applies Signals in order" names no ordering key. UUID version 7 ids generated on several threads are not a total order.
  4. **Shape and kinds.** No envelope, no closed set of kinds, and the poll cursor (ETag, `since`) has no stated home although "nothing needed for recovery lives only in memory".
  5. **In-process writers.** AD-17 prevents a second *process* writing the SQLite file, but the poller, API handlers, the runtime, the retention job and the loop all write concurrently inside one process. No journal mode, busy timeout or writer discipline is set.
- **Proposed fix:**
  - Append to the AD-1 Rule: "Consuming a Signal, every Task and step-run change it causes, and every effect intent it produces are committed in one SQLite transaction through a single Core port, `IUnitOfWork`. Inbox insertion is idempotent on `(source, sourceKey)`. Signals are applied in ascending `sequence`, an integer the inbox assigns at insertion."
  - Add `IUnitOfWork` to the AD-3 port list.
  - Add a Conventions row — **Signal:** "`{ id, sequence, kind, source, sourceKey, projectId, taskId?, actor?, occurredAt, payload }`. `kind` is a closed enum in Core. `sourceKey` is the GitHub event or node id for polled facts, the request id for API calls, the step-run id for container exit, and the due time for clock Signals. The poll cursor per Project is a persisted record."
  - Add a Conventions row — **SQLite:** "WAL mode, `busy_timeout` set, foreign keys on; one `DbContext` per unit of work; no write outside `IUnitOfWork` or `ISignalInbox.AppendAsync`."

---

## High

### H-1 — The Task lifecycle is not a closed transition table, and it contradicts FR-14, FR-23 and AD-2

- **Location:** Structural Seed "Task lifecycle" diagram; Task states convention row; AD-1; AD-2; AD-11.
- **Problem:**
  1. **`Running` is entered without dispatch.** `WaitingForCapacity --> Running` and `PullRequestOpen --> Running` bypass the queue, while `WaitingForOwner --> Queued` does not. If Task B was dispatched while Task A waited, A's direct return gives two running Tasks (FR-14). UJ-3 also says a Rework happens "in the next window", that is, through scheduling. No AD states "at most one Task is `Running`", although AD-1 claims FR-14.
  2. **`Failed` is a dead end.** It has no outgoing edge and no end marker. FR-40 expects a failed Task to be fixed, AD-2 says a closed pull request overrides the Task, and the Owner needs some way to retry.
  3. **AD-2's overrides are missing as edges.** Only `Queued --> Withdrawn` exists for Marker Label removal. Removal after work starts, an issue closed while `Running`, and cancel from `WaitingForOwner` or `WaitingForCapacity` (FR-23: "queued or in progress") are undefined.
  4. **Task identity on opting in again is undefined.** `Cancelled --> Queued` reuses the Task, but `Withdrawn` is terminal. If a re-labelled issue creates a new Task, it collides with the stored branch name `hephaestus/<issue-number>-<slug>` and with the Glossary rule "one issue is one Task".
  5. **Failure class to outcome is undecided.** AD-11 only says what `usage-limit` may do. Which classes are re-attempted, which fail the Task, and what state a Task holds while GitHub or a provider is unreachable (NFR-12) are left to each epic. AD-1 requires every wait to be a Task state, and no state fits an outage.
- **Proposed fix:**
  - Append to AD-1: "At most one Task is in `Running`. `Queued --> Running` is the only transition into `Running` and only dispatch takes it. Dispatch prefers a Task that already has a step run (FR-15), then the queue-order policy. The lifecycle diagram is the complete set of allowed transitions."
  - Append to the Task states row: "A Task is unique per Project and issue number for the life of the installation; opting in again reuses the Task, its branch and its history. A `Queued` Task may carry a `notBefore` resume condition."
  - Change the diagram edges to:
    - `WaitingForCapacity --> Queued: capacity returns`
    - `PullRequestOpen --> Queued: Rework Command`
    - `Failed --> Queued: Rework Command or retry by a Trusted User`
    - `Failed --> Closed: pull request or issue closed`
    - `WaitingForOwner --> Cancelled`, `WaitingForCapacity --> Cancelled: Owner cancels`
    - `Running --> Cancelled: Owner cancels, Marker Label removed, or issue closed`
    - `Withdrawn --> Queued: Marker Label applied again by a Trusted User`
    - `Cancelled --> [*]` stays absent; `Withdrawn --> [*]` is removed.
  - Add a table under AD-11:

    | Failure class | Outcome |
    | --- | --- |
    | `usage-limit` | Handoff to the other Agent; if none has capacity, `WaitingForCapacity` |
    | `timeout`, `agent-error`, `unknown` | Same step attempted again up to the step retry bound, then `Failed` |
    | `validation-failure` | Correction round; at the Correction Loop bound, `Failed` |
    | `infrastructure-error` | `Queued` with `notBefore` set by bounded backoff; at the bound, `Failed` |
    | `cancelled` | `Cancelled` |

### H-2 — The idempotency key cannot express effects that have no Task or no step run

- **Location:** Conventions "Idempotency key" and "Hidden marker" rows; AD-5; Core entities diagram (`TASK ||--o{ SIGNAL`, `TASK ||--o{ EFFECT`); Deferred row "Story-to-issue identity".
- **Problem:** The key is `<task-id>:<step-run-sequence>:<effect-kind>:<ordinal>`. Several required effects have no Task or no step run: issue creation from Stories (FR-66, v0.1), the label and comment written when a Task is `Queued` or rejected for an unknown Alias (FR-7, FR-11), the comment recording invalid Project Configuration (FR-2), and the window summary Notification (FR-50). The Deferred row tells the Methodology epic it "must use the hidden marker and journal (AD-5)", which is impossible with this key. The ER diagram repeats the error by giving every Signal and Effect exactly one Task, although AD-17 routes Installation Configuration changes and start-on-demand through Signals.
- **Proposed fix:**
  - Idempotency key row: "`<scope>:<scope-id>:<sequence>:<effect-kind>:<ordinal>`. `scope` is `task`, `project` or `installation`. `sequence` is the step-run sequence, or `0` for an effect outside a step run. Story issues use `project:<project-id>:0:issue-create:<story-key>`."
  - ER diagram: change to `TASK |o--o{ SIGNAL : receives` and `TASK |o--o{ EFFECT : causes`, and add `PROJECT ||--o{ EFFECT : causes`.

### H-3 — A Deferred row postpones conventions that v0.1 epics must already write and read

- **Location:** Deferred row "Rework Command form, Priority Label spelling, dependency declaration syntax, independence detection" (decided in "Intake and v0.3 epic spines"); AD-15 last sentence; Conventions "Labels" row.
- **Problem:**
  1. **Dependency declaration syntax.** FR-66 (v0.1) must *write* declared dependencies into the issues it creates, and FR-8 (v0.1) must *respect* them in queue order. The syntax is deferred to a v0.3 epic spine with FR-10. The Methodology epic and the Intake epic will each invent one, and issues created in v0.1 may be unreadable by v0.3.
  2. **Comment-command grammar.** AD-15 requires a Trusted User to "confirm" an edited issue body (NFR-1, v0.1) and FR-23 and FR-45 add more commands later. There is a Labels convention but no comment-command convention, so each epic picks its own form (`/hephaestus confirm`, `@hephaestus rework`, a label). Commands are part of the versioned public surface (FR-59).
- **Proposed fix:**
  - Move two items out of Deferred into Conventions:
    - **Dependencies:** "One line in the issue body, `Depends-On: #<n>[, #<n>]`, read from the trusted snapshot by one parser in Core. FR-66 writes this form."
    - **Comment commands:** "A comment line that starts with `/hephaestus <verb>`, from a Trusted User. Verbs are a closed enum in Core with one parser; v0.1 defines `confirm`; later epics add verbs to the enum and never a second grammar."
  - Leave in Deferred only: "Rework verb name, Priority Label spelling, independence detection".

### H-4 — AD-9 commits and pushes whatever is in the tree: side effects of read-only steps, and secrets

- **Location:** AD-9 Rule ("After every step ... a checkpoint commit"; "imports the bundle with object checking"); AD-16 masking registry; FR-4, FR-32.
- **Problem:**
  1. **Non-mutating steps change the branch.** The checkpoint runs after *every* step. After `Validation` it commits build output that the Project does not ignore. After `Review` it commits anything the Reviewer changed (its sandbox is off, AD-7), and AD-9 attributes those commits to the Review step. The Reviewer then is "the one that wrote the latest changes", which breaks FR-32 and makes the next Reviewer choice ambiguous. The Workspaces epic and the Review epic will resolve this differently.
  2. **The push path bypasses masking.** FR-4's testable consequence is that a search of *repository contents* for any credential value finds nothing. The masking registry filters text; the bundle is git objects. The step container holds the Agent's credential, and `git add -A` in the checkpoint will commit a debug dump or log that contains it. Object checking does not look for secrets.
  3. **A rejected bundle has no defined outcome.** Agents may commit, so they may also amend or rebase; the fast-forward check then fails. What happens to the step run, the Task and the unpushed work (FR-20, FR-22) is not stated.
  4. **Attribution depends on a commit that may not exist.** "Commits between two checkpoints" needs a checkpoint commit per step; with a clean tree that means empty commits on the pull request, or no boundary.
- **Proposed fix:** Replace the middle of the AD-9 Rule with: "Only `Planning`, `Implementation`, `Correction` and `Rework` steps may change the Task branch. After any other step the checkpoint container restores the tree to the last checkpoint and makes no commit. Each step run records `startTip` and `endTip`; a checkpoint commit is made only when the tree is dirty; commits in `(startTip, endTip]` are attributed to that step run. Before pushing, the gateway scans every blob added since the last pushed tip against the masking registry. A bundle that fails object checking, the fast-forward check, the ref check or the secret scan is not pushed: the step run fails as `agent-error`, the Task volume is kept, and the Task goes to `WaitingForOwner` with an Escalation."

### H-5 — AD-8 has Hephaestus redistribute Agent CLI binaries

- **Location:** AD-8 Rule ("ships those binaries"); Deployment "Images" and "Volumes" (`hephaestus-agents`); NFR-15, FR-24, FR-58.
- **Problem:** Shipping the binaries in a public MIT image on GHCR is redistribution. Codex CLI is Apache-2.0. Claude Code, to my knowledge, is proprietary ("all rights reserved", use under Anthropic's commercial terms) with no redistribution grant — confirm against the current licence. This is a legal risk for a public project and sits badly with NFR-15 ("Agents ... are the Owner's own") and FR-24 ("only officially documented ... mechanisms"). The spine also never says how the `hephaestus-agents` volume is filled or refreshed on upgrade.
- **Proposed fix:** Reword the AD-8 Rule: "Each Hephaestus release pins one exact version and one SHA-256 per platform for each Agent CLI in `deploy/agents.lock`. Hephaestus does not redistribute the CLIs: at startup the orchestrator downloads any missing pinned binary from the vendor's official distribution channel, verifies it against the lock file, and places it in the `hephaestus-agents` volume under `<agent>/<version>/<platform>/`. A step is not dispatched to an Agent whose pinned binary is absent." Add to Deployment "External providers": "the vendors' download endpoints, at install and upgrade only".

---

## Medium

### M-1 — The sequence diagram shows the loop blocking on a step
- **Location:** "One step across the boundary" diagram; AD-1.
- **Problem:** `W-->>L: events, result, failure class` reads as a synchronous return. AD-1 says container exit is a Signal. A loop that awaits a step for hours cannot apply a cancel (FR-23, "within a bounded time"), an answer or a reconcile.
- **Fix:** Add a participant `I as Signal inbox`; replace the return with `W->>I: Signal step-exited (result, failure class)` and `I-->>L: next Signal`. Append to AD-1: "The loop starts a step and returns to the inbox; it never awaits a container."

### M-2 — AD-1 single-writer is a convention, not a boundary
- **Location:** AD-1; AD-3 (`ITaskStore`).
- **Problem:** `ITaskStore` is resolvable by any API handler in Host, so nothing stops a handler writing Task state.
- **Fix:** "Core exposes `ITaskQueries` (read-only) to Host. The write side (`IUnitOfWork`) is consumed only by the loop; an architecture test fails the build if any type outside `Hephaestus.Core.Orchestration` depends on it."

### M-3 — Several ports and shared services have no implementing project
- **Location:** AD-3 port list; Layer table; Capability map row "Escalation and Notification" ("a notifier adapter").
- **Problem:** `INotifier`, `ISecretStore` and `IOutputStore` have no named implementer, and "a notifier adapter" is not one of the six projects AD-3 calls complete. The masking registry (used by GitHub, Persistence and logging, which may not reference each other), `hephaestus.yml` parsing, and the Methodology loader have no home either. Is YamlDotNet allowed in Core?
- **Fix:** Add a table: `ISecretStore`, `IOutputStore` → `Hephaestus.Persistence`; `INotifier` → `Hephaestus.Host` (or a named seventh project added to the AD-3 diagram); masking registry → a Core service `SecretMasker`, applied inside every `ICodeHost`, `IOutputStore` and `INotifier` implementation and in the logging pipeline; Project Configuration parsing and Methodology loading → Host, handing Core typed models.

### M-4 — "Under an effect-journal entry" is not enforceable by signature
- **Location:** AD-4, AD-5.
- **Problem:** Nothing forces a caller of `ICodeHost` or `IWorkspaceRuntime` to have recorded an intent first.
- **Fix:** "Every mutating method on `ICodeHost`, `IWorkspaceRuntime` and `INotifier` takes the recorded `EffectIntent` as a required parameter; the implementation writes the hidden marker or Docker label from it."

### M-5 — AD-5's resolution list is incomplete
- **Location:** AD-5 Rule.
- **Problem:** Notification and merge are listed as effects but have no natural-key lookup. A notification cannot be looked up, so retry-or-drop is undefined (FR-51). "Append-only" also conflicts with "after it, the result" if the result updates the intent row.
- **Fix:** Add: "merge by the pull request's merged state; a notification intent without a result is not retried and is recorded as `delivery-unknown`. The result is a second appended row that references the intent."

### M-6 — No rule for removing a Task volume (FR-22)
- **Location:** AD-6 (binds FR-22 but states no cleanup rule).
- **Problem:** When a volume is removed, and whether it survives a long `PullRequestOpen`, is undecided; the Workspaces and Rework epics will differ.
- **Fix:** Append to AD-6: "A Task volume is removed only when the Task is in `PullRequestOpen`, `Failed`, `Cancelled`, `Withdrawn`, `Merged` or `Closed` and its last recorded `endTip` equals the pushed tip. A later step recreates the volume from the Task branch (AD-9, AD-12)."

### M-7 — The image for checkpoint and checkout containers is not named
- **Location:** AD-6, AD-9 ("a credential-free container").
- **Problem:** The Project image (untrusted content, `git` by contract) or a Hephaestus helper image? This affects release packaging (FR-58 says "images") and the initial ownership of the volume for user `10001`.
- **Fix:** "Checkout and checkpoint containers run the Hephaestus image with a fixed entrypoint, as `10001:10001`, with hooks disabled (`core.hooksPath=/dev/null`) and no network."

### M-8 — FR-25 and FR-26 have no home in the adapter contract, and v0.1 waits have no resume condition
- **Location:** AD-10 ("does four things"); Deferred row "Usage Limit detection signals and capacity probing"; Capability map row FR-24..FR-30.
- **Problem:** The availability check (FR-26, v0.1) and sign-in through the Agent's own tool (FR-25, v0.1) are not among the adapter's four duties and are not decided elsewhere. FR-26 ("treated as having no capacity") and FR-32 ("the Task waits") need `WaitingForCapacity` in v0.1, but capacity probing is deferred to v0.2, so AD-1's "recorded resume condition" cannot be met in v0.1.
- **Fix:** AD-10: "An adapter also describes an availability probe invocation and a sign-in invocation; the runtime runs them like a step, without a Task volume." Deferred row: "In v0.1 the resume condition of `WaitingForCapacity` is a bounded periodic availability probe; v0.2 replaces it."

### M-9 — AD-13 names no validator and ignores the two CLIs' schema subsets
- **Location:** AD-13; "JSON and YAML" row; Stack.
- **Problem:** "Core validates every result" with no library in the Stack and none allowed in Core. Codex's schema-constrained output accepts a strict subset (every property required, no additional properties); a schema generated from records with optional members will be rejected by one CLI.
- **Fix:** "Validation is strict `System.Text.Json` deserialization into the Core record (unmapped members disallowed, required members enforced). Result records use only required members, `null` instead of optional, closed objects and no polymorphism. Every result carries `schemaVersion`."

### M-10 — AD-16 leaves the Base Branch circular and the home of limits ambiguous
- **Location:** AD-16; "Bounds" row; PRD Glossary; FR-3; NFR-5.
- **Problem:** Project Configuration is read "from the Base Branch only", but the Glossary puts the Base Branch *in* Project Configuration. "Reads its limit from configuration" does not say which; a time or memory limit placed in `hephaestus.yml` lets a repository change raise it, which FR-3 forbids. The schema version key recorded in the memlog was dropped.
- **Fix:** "The Base Branch is recorded at Project registration (default: the repository's default branch) and `hephaestus.yml` is read from it. `hephaestus.yml` has a top-level `version` key." Bounds row: "Time, processor, memory, spend, retry and wait limits come from Installation Configuration. The only bound in `hephaestus.yml` is the Correction Loop bound, capped by an installation maximum."

### M-11 — Persistence conventions that parallel epics need are missing
- **Location:** Consistency Conventions ("Ids and time"); `Hephaestus.Persistence`.
- **Problem:** (a) The EF Core SQLite provider cannot compare or order `DateTimeOffset` in queries, so "what is due" queries fail or run client-side. (b) Enum storage is unspecified. (c) All epics add migrations to one `DbContext`; the model snapshot will conflict between parallel agents.
- **Fix:** "Timestamps are stored as UTC ISO 8601 text of fixed width through one value converter; enums as kebab-case text. One migration per story, regenerated on top of `main` before merge; a released migration is never edited."

### M-12 — Build and CI are silent
- **Location:** Deployment and operations; Source tree; "Tests" row.
- **Problem:** Only the release image build is mentioned. Nothing fixes the SDK pin, central package versions (the Stack pins have no file that carries them), analyzer and nullable settings, the pull-request pipeline, or which tests need Docker. Each coding agent will set these up differently.
- **Fix:** Add a Conventions row — **Build:** "`global.json` pins the SDK; `Directory.Packages.props` holds every version in Stack; `Directory.Build.props` sets nullable on and warnings as errors. CI on every pull request runs build, unit tests, and the Docker-backed tests; a version tag builds and publishes the image." Add these files to the source tree.

### M-13 — Operations is only partly decided
- **Location:** Deployment and operations.
- **Problem:** Startup and backup are covered. Not covered: restart policy and health check of the `hephaestus` service; behaviour on stop (SIGTERM during a step); garbage collection of orphaned containers, volumes and pulled Project images; growth of mirrors and Task volumes; rotation of JSON logs; a backup method safe for a live WAL database; rollback after a failed upgrade.
- **Fix:** Add bullets: "**Service:** `restart: unless-stopped`, a health check on the API, Docker log rotation set in Compose. **Shutdown:** on SIGTERM the loop stops dispatching and exits; running step containers are handled by the restart rule in AD-1. **Garbage collection:** at startup and daily, Docker objects labelled `hephaestus.*` with no live Task are removed under AD-6. **Backup:** `hephaestus backup` writes a consistent copy with the SQLite backup API. **Rollback:** restore the pre-migration copy and run the previous image; there is no automatic downgrade."

### M-14 — GitHub App permissions and the Actions-on-push risk are absent
- **Location:** AD-4 (binds NFR-6); Deployment "External providers".
- **Problem:** NFR-6 is mapped to AD-4, which names no permission set. Each GitHub-touching epic can widen it. The memlog records that a push of a Task branch can trigger workflows that run with repository secrets, and the mitigation (no `workflows` permission); the spine carries neither. Short-lived installation tokens and refreshed Agent tokens are minted at run time, not "loaded", so the masking registry as worded misses them.
- **Fix:** Add a Conventions row — **GitHub App:** "Repository permissions: contents write, issues write, pull requests write, metadata read; checks and statuses read from v0.3. Never `workflows` or `administration`. Installed on registered repositories only. A new permission is a changelog entry." AD-16: "... built from every loaded *or minted* secret value".

### M-15 — AD-6 claims more isolation than it gives
- **Location:** AD-6 "Prevents"; AD-7; NFR-8; NFR-3.
- **Problem:** "Validation code reading agent tokens" is prevented only for the Validation container. Agents run the Project's tests inside their own step container, and Open Question 4 treats the repository as executable configuration there, so repository code can read the credential of whichever Agent runs it, including the other vendor's Agent on the next step. Open egress also reaches the host gateway and the Owner's local network.
- **Fix:** Reword "Prevents" to "a long-lived container accumulating both vendors' credentials; a Validation run seeing any credential". Add to AD-7: "Known limit: code in the repository can read the credential of the Agent whose step container runs it, and with open egress can reach the host's network."

### M-16 — The entity seed omits entities several epics share
- **Location:** Core entities diagram.
- **Problem:** `Escalation` and `Handoff` are named as types in the Vocabulary row and retained by FR-53, but are not entities. There is no work-window entity, although FR-16 (v0.1), FR-13, the FR-50 summary and SM-5 need persisted window state (AD-1 forbids in-memory scheduling state). `TASK ||--|| CONFIG_SNAPSHOT` is wrong for a `Queued` Task, which has no snapshot yet (AD-16).
- **Fix:** Add `TASK ||--o{ ESCALATION : raises`, `TASK ||--o{ HANDOFF : records`, `PROJECT ||--o{ WORK_WINDOW : "is worked in"`; change to `TASK ||--o| CONFIG_SNAPSHOT : "started with"`.

### M-17 — Three Stack rows are ranges, and some named technology is missing
- **Location:** Stack table; AD-8.
- **Problem:** "Claude Code CLI 2.1.x", "Codex CLI 0.171.0 or later" and "Docker Engine 29.x" are not pins, and "or later" contradicts AD-8 ("pins one version"). Not listed: git in the orchestrator image (bundles need the git CLI), the base images, and the library that signs the GitHub App JWT.
- **Fix:** Agent rows: "exact version in `deploy/agents.lock`; v0.1 starts at 2.1.<n> / 0.171.0". Docker row: "minimum Engine 29.0 with Compose v2". Add rows: "git 2.x in the Hephaestus image, run as a subprocess by `Hephaestus.GitHub` only"; "`mcr.microsoft.com/dotnet/aspnet:10.0` base image"; "Microsoft.IdentityModel.JsonWebTokens 8.23.0".

### M-18 — FR-66 has no mechanism, only a deferred identity question
- **Location:** AD-14; Step kinds row; Deferred row "Story-to-issue identity"; Capability map row "Methodology".
- **Problem:** Creating issues from Stories has no Task, so it is not a step. AD-14's Methodology directory has a manifest and step templates but no slot that says where Stories are and how they are read, and Core may not branch on the Methodology name. The Deferred row covers identity and drift only.
- **Fix:** AD-14: "The manifest may declare a Story source (path glob on the Base Branch and field mapping). Core reads Stories through `ICodeHost` from the mirror, never from a Workspace, with one generic reader driven by the manifest."

### M-19 — FR-44's hard limits are not stated as a rule
- **Location:** AD-4, AD-9; Deferred row on Stack update ("any exception to fast-forward-only").
- **Problem:** "Task branches are pushed fast-forward only" does not say the gateway never touches the Base Branch, and the Deferred row opens the door to exceptions without a floor.
- **Fix:** Add to AD-4: "The gateway never pushes to, force-updates or deletes a ref outside `refs/heads/hephaestus/`. The Base Branch changes only through the pull request merge call, made only for a pull request whose head commit has an approving review from a Trusted User. No later decision may relax this sentence."

---

## Low

- **L-1 — Docker objects row.** A Task volume and the workspace network have no step run, so "carries labels `hephaestus.task` and `hephaestus.step-run`" cannot hold; the network is also created by Compose, not by Hephaestus. **Fix:** "Containers carry both labels; Task volumes carry `hephaestus.task`; the workspace network is declared in `compose.yaml`."
- **L-2 — Sequence diagram.** The checkpoint is drawn on the participant "Step container", but AD-9 uses a separate credential-free container. **Fix:** add `participant C as Checkpoint container`.
- **L-3 — Steps flowchart.** No edge from Planning to Escalation (FR-65), and none from Implementation. **Fix:** add `P -->|question for the Owner| E` and `I -->|cannot proceed| E`.
- **L-4 — AD-14 wording.** "One instruction template per step kind" includes `Validation` and `Publish`, which no Agent performs. **Fix:** "per Agent step kind".
- **L-5 — Classification precedence.** AD-11 lets both the runtime and the adapter classify. **Fix:** "Classes observed by the runtime (`timeout`, `cancelled`, `infrastructure-error`) take precedence over the adapter's."
- **L-6 — Source tree.** No place for the issue template and authoring skill (FR-56, FR-57) and no solution file. **Fix:** add `assets/issue-template/`, `assets/skills/` and `Hephaestus.slnx`.
- **L-7 — Bounds row vs FR-46.** "None is unbounded" reads as contradicting unlimited Reworks. **Fix:** "every *automatic* loop, retry and wait"; "Rework rounds are started by the Owner and are not bounded".
- **L-8 — AD-2 categories.** Check status and review state are neither human intent nor execution state, and v0.3 must read them. **Fix:** add "Repository facts (check results, head commit) are read from GitHub when needed and never cached as truth."
- **L-9 — `[ADOPTED]` tag.** AD-4 and AD-16 carry it with no legend, implying the other fifteen are not adopted. **Fix:** define the tag or remove it.
- **L-10 — AD-3 Core dependencies.** A deny-list of four packages is open-ended. **Fix:** "Core references only `Microsoft.Extensions.*.Abstractions` packages."
- **L-11 — Trusted User identity and local actor.** **Fix:** "A Trusted User is stored by numeric GitHub user id, with the login for display. A Signal from the local API has the actor `owner` and is trusted."
- **L-12 — State label projection.** **Fix:** add to the Labels row: "exactly one `hephaestus:<state>` label is present on a Task's issue; terminal states keep theirs." Also state where the plan lives (FR-65): "the plan is a validated step result stored in SQLite and rendered into the pull request description".

---

## Checklist summary

| # | Check | Result |
| --- | --- | --- |
| 1 | Fixes the real divergence points | Mostly; misses the Signal contract (C-1), the transition table (H-1), effect scope (H-2), read-only steps (H-4), port homes (M-3), persistence and build conventions (M-11, M-12). |
| 2 | Each Rule enforceable and effective | AD-3, AD-6, AD-7, AD-12, AD-15, AD-17 hold. AD-1, AD-4, AD-5 rely on convention (M-2, M-4). AD-9 allows what FR-4 and FR-32 forbid (H-4). AD-8 has a licensing problem (H-5). |
| 3 | Deferred items cannot cause divergence | Fails for dependency syntax and command grammar (H-3), Story identity (H-2, M-18), capacity probing in v0.1 (M-8). Other rows are safe. |
| 4 | Technology plausible and pinned | NuGet pins verified. Three rows are ranges; git, base image and JWT library are missing (M-17). |
| 5 | PRD coverage | No FR is contradicted outright except FR-14 by the lifecycle diagram (H-1) and FR-4/FR-32 by AD-9 (H-4). Weak or missing homes: FR-22 (M-6), FR-25/FR-26 (M-8), FR-44 (M-19), FR-66 (H-2, M-18), FR-16/FR-50 window state (M-16), NFR-6 (M-14), NFR-12 (H-1). |
| 6 | Dimensions | Deployment, environments, providers, data, security, testing: decided. Operations: partial (M-13). Build and CI: silent (M-12). Observability: logs decided, health missing (M-13). |
| 7 | Mermaid | All six parse. Consistency issues: H-1, M-1, M-16, L-2, L-3. |
| 8 | Internal contradictions | AD-1 vs sequence diagram (M-1); AD-2 vs lifecycle diagram (H-1); key convention vs Deferred row (H-2); AD-8 "pins" vs Stack "or later" (M-17); AD-3 "complete set" vs "a notifier adapter" (M-3); Docker objects row vs Deployment (L-1). |
