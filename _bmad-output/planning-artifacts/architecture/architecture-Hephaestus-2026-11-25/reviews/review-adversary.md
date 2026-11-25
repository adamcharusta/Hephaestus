# Adversarial review — Architecture Spine, Hephaestus

- **Reviewed:** `ARCHITECTURE-SPINE.md` (draft, 2026-11-25) against `prd.md` (final, 2026-11-25)
- **Method:** build pairs of units one level down that each obey every AD and convention to the letter, and look for the place where they still do not fit; then play a compromised Agent inside a step container that obeys nothing and look for a way out that breaks no Rule as written.
- **Spine not edited.** Every hole carries a proposed rule in the spine's own style.

## Verdict

The spine fixes dependency direction, the credential boundary, and the failure vocabulary well, but it leaves the step execution contract, the meaning of "human intent", the transaction boundary, and the context and result seam open enough that lawful builders will diverge, and its security rules list what a step container lacks rather than what it has, which leaves five practical ways out.

| Tier | Count |
| --- | --- |
| Critical | 5 |
| High | 13 |
| Medium | 17 |
| Low | 10 |

What held under attack: AD-3 (reference direction), AD-11 (closed failure set and the `unknown` default), AD-5's natural-key lookup, AD-14 (no branching on Methodology or Agent), AD-17 (one process, one writer of the file). None of the holes below needs those reversed.

Three facts about third-party behaviour used below are stated from general knowledge and were not verified against the pinned CLI versions or the current GitHub documentation: C-4 (what triggers workflows and with which secrets), C-5 (account-linked tools reachable from a CLI in subscription mode), H-12 (refresh-token rotation). Each belongs in a spike before the rule is adopted as written.

---

## Critical

### C-1 — The step execution contract is stated twice, differently

- **Pair:** orchestrator loop epic vs workspace runtime epic (and the agents epic, caught between them).
- **How both obey:** the loop team follows the "One step across the boundary" diagram: it calls the runtime and receives `events, result, failure class` back. The runtime team follows AD-1: container exit is a source of change, so it "appends a Signal to a persisted inbox". Both are the spine's words.
- **What breaks:** with the first reading the single writer is parked inside one call for as long as a step runs, so a cancel, a withdrawal, a new Marker Label, or an answer to another Task's Escalation is not applied until the step ends; FR-11 (state within one polling cycle), FR-23 (cancel within a bounded time), and FR-48 (other work continues) fail. With the second there is no defined place where the adapter's parser meets the output stream: the runtime "knows nothing about Agents" (AD-10) and adapters cooperate only through Core (AD-3), so neither side may call the other, and nobody is named as the writer of the raw stream.
- **Proposed rule (tightens AD-1, AD-10, replaces the return arrow in the sequence diagram):**
  > The loop never awaits a step. `IWorkspaceRuntime.StartStepAsync` returns when the container is created. The runtime writes the raw stream to `IOutputStore` and appends exactly one `StepExited` Signal (step-run id, exit code, runtime cause: `none`, `timeout`, `cancelled`, `out-of-memory`, `runtime-fault`). Applying that Signal, the loop gives the stored stream to the adapter, validates the result, and writes the step run. `StopStepAsync` is the only way to end a step early and also ends in `StepExited`. Checkpoint and Validation containers follow the same contract.

### C-2 — AD-2 reads human intent as present state; AD-15 reads it as an attributed event

- **Pair:** GitHub gateway and reconcile builder vs admission and intake builder; also v0.1 lifecycle vs v0.2 cancel.
- **How both obey:** AD-2 says GitHub is authoritative for "Marker Label presence, issue and pull request open or closed" and that on every reconcile these facts "override the Task (withdraw, cancel, close)". AD-15 says a Signal from anyone who is not a Trusted User "cause[s] no state change".
- **What breaks:**
  - A user with triage rights who is not a Trusted User removes the Marker Label, or an outside issue author closes their own issue. Reconcile by presence withdraws or closes the Task; admission by event ignores it. Two lawful builds, opposite outcomes, and in the first an untrusted user can stop any Task.
  - A Trusted User removes the label and an untrusted user puts it back: presence says opted in.
  - The Owner cancels from the CLI (FR-23). The Marker Label is still on the issue. A presence-based reconcile sees "opted in" and queues the Task again on the next tick; `Cancelled --> Queued: opted in again` has no defined trigger.
- **Proposed rule (replaces the first and last sentences of AD-2):**
  > Human intent is read only as actor-attributed events, never as present state. The gateway turns each GitHub timeline event (labeled, unlabeled, closed, reopened, review submitted or dismissed, comment created or edited) into one Signal carrying the actor's numeric id. Reconcile never infers intent from the current label set or from open or closed state. An event by anyone who is not a Trusted User changes no Task and is reported on the issue. Opting in again is a Marker Label `labeled` event by a Trusted User later than the cancellation, withdrawal, or failure it follows.

### C-3 — The Task volume carries executable state between steps that no diff ever shows

- **Attack (breaks no Rule):** AD-6 gives each step a fresh container but the same volume, and AD-9 runs the checkpoint on that volume. A compromised Agent in step N writes `.git/hooks/*`, `core.fsmonitor`, `core.hooksPath`, or a filter driver into `.git/config`, or replaces an ignored file such as `node_modules/.bin/jest` or a virtual environment's interpreter. None of it is tracked, so none of it is in the checkpoint commit, the pull request, or the Reviewer's view.
  1. **The other vendor's credential.** Step N+1 is the Review, in a fresh container holding the other Agent's credential. The first `git status` that CLI runs executes the planted hook beside that credential, with open egress (NFR-4 in v0.1). AD-6 claims to prevent "a long-lived container accumulating both vendors' credentials"; the volume does the accumulating instead.
  2. **A forged Validation.** AD-13 lets only the exit status of the Validation commands decide. The planted `jest` exits 0. The Agent's claim has decided the outcome, invisibly (FR-31).
  3. **A poisoned checkpoint.** The checkpoint container runs `git` against an Agent-written `.git`: hooks and filters shape the commit and the bundle, or an `index.lock` left behind makes every later checkpoint fail, so FR-20 fails and the Workspace can never be removed (FR-22).
  4. **Forged attribution.** "Agents may commit during a step": the Agent writes commits with any author (the Owner's address) and any `Hephaestus-Agent` trailer. An amended earlier checkpoint makes the push a non-fast-forward, which AD-9 rejects with no defined next state.
- **Proposed rule (tightens AD-6, AD-9, AD-13; replaces "Agents may commit during a step" and the attribution sentence):**
  > Only the committed tree survives a step. Before every step a credential-free container re-creates `/workspace` in the Task volume from the bundle of the last pushed tip: a fresh clone, no hooks, default configuration, no untracked or ignored files. Validation always runs on such a clone. The checkpoint container never uses the volume's `.git`: it builds its own git directory from the same bundle outside the Task volume, takes `/workspace` as work tree only, and writes exactly one commit whose parent is the last pushed tip. Commits an Agent made during the step are flattened into it.
- **Cost, stated plainly:** dependencies are restored on every step. If that is refused, the minimum that closes items 1 and 2 is: before any step that carries a different credential than the previous one, and before every Validation, the runtime deletes `.git/hooks`, rewrites `.git/config`, and runs a full clean of untracked and ignored files.

### C-4 — The gateway turns Agent-written code into a trusted same-repository push

- **Attack (breaks no Rule):** AD-9 has the gateway push whatever tree the Agent left, provided it is a fast-forward on the Task branch. The repository's own automation then runs it. A compromised Agent edits a build or test script that an existing workflow calls on `push` or `pull_request`. The branch is in the Project's own repository, not a fork, so the workflow runs with the repository's secrets and its token. From there it can read every secret, push to other `hephaestus/*` branches (another Task's work), and, where the token is write-scoped and the Base Branch is unprotected, push to the Base Branch — a merge with no approval (FR-44) performed by an identity that is not Hephaestus's. If the GitHub App holds the `workflows` permission the Agent can add its own workflow; if it does not, a legitimate Task that touches `.github/workflows` has its push refused by GitHub and no Rule says what state follows.
- **Why no Rule stops it:** AD-4 keeps the credential out of the container; nothing says what the credential may be made to carry. NFR-6 is about Hephaestus's permissions, not about what its pushes set off.
- **Proposed rule (new AD, "A Task branch is untrusted code in a trusted repository"):**
  > The GitHub App is never granted the `workflows` permission. The gateway does not push a bundle whose diff against the Task's start commit touches `.github/**` or `hephaestus.yml`; the loop raises an Escalation and keeps the Workspace. Registering a Project fails unless the Base Branch is protected against direct pushes. The threat model states that any workflow triggered by a `hephaestus/**` branch or its pull request runs Agent-written code with the secrets and token permissions the repository gives it, and setup documentation tells the Owner how to withhold both.

### C-5 — In subscription mode the Agent's reach is the vendor account, not the container

- **Attack (breaks no Rule):** AD-7 makes the container the boundary and AD-4 keeps every GitHub credential out of it. A CLI signed in with a subscription login can be offered tools that run outside the container under the account's authority: account-linked connectors, remote MCP servers registered on the account, cloud task delegation. If the Owner has linked GitHub (or mail, or a drive) to that account, a compromised Agent can act on GitHub as the Owner — who is a Trusted User. It posts an approving review on its own pull request; at the next window FR-41 merges it. NFR-2 and FR-44 are both broken and no credential ever entered the Workspace.
- **Why no Rule stops it:** Open Question 4 covers repository-supplied hooks and MCP servers only. AD-7 switches the Agent's sandbox off inside the container and says nothing about tools that do not run inside it.
- **Proposed rule (adds to AD-7 and AD-10):**
  > The container bounds only what runs in it. Every invocation disables tools that execute outside the step container under the account's authority (account-linked connectors, remote MCP servers, cloud task delegation, vendor-side repository integrations) and loads only the MCP configuration Hephaestus supplies. An adapter that cannot guarantee this for an authentication mode declares that mode unsupported, and it is never run. The injection spike proves it for each pinned CLI version.
- **Unverified:** which of these each pinned CLI exposes in non-interactive mode. Add to Open Question 1.

---

## High

### H-1 — No transaction spans the three stores; Signal order and identity are undefined

- **Pair:** persistence epic vs core loop epic; gateway poller vs core loop.
- **How both obey:** AD-3 lists `ITaskStore`, `IEffectJournal`, `ISignalInbox` as three ports. Persistence implements each with its own save. Core assumes "apply Signal, write Task, record intent" is one step. A crash between saves leaves a Signal applied with no Task change, or a Task change with no intent, and AD-5's recovery has nothing to find.
- **Also:** "applies Signals in order" does not say which order. UUID version 7 ids from two threads in the same millisecond are not ordered; GitHub events arrive per endpoint, out of true order. A poll repeated after a restart delivers the same event twice, so a Rework Command or an answer is applied twice.
- **Proposed rule (adds to AD-1):**
  > One transaction per Signal: marking it applied, every Task and step-run write it causes, and the intents of the effects it causes commit together, through one unit of work shared by `ITaskStore`, `IEffectJournal`, and `ISignalInbox`. Effects run only after that commit; each result is its own transaction. Inbox order is a database-assigned integer sequence. Every Signal has a unique `(source, source-event-id)` and a duplicate append is a no-op. The poller appends first and advances its cursor second.

### H-2 — What crosses out of a container is called "a bundle" but arrives as an archive from a hostile source

- **Attack (breaks no Rule):** AD-9 forbids the orchestrator to run a tool "in an agent-writable directory". It does not constrain reading what a container hands back. The topology diagram says "copy bundle"; Docker returns a tar stream. Entries with link types, parent paths, or unbounded size are unpacked by the process that holds the GitHub credential and the Docker socket. One written file in a mirror's `config` (`core.fsmonitor`, `core.sshCommand`) is code execution in the trusted computing base. A v0.3 builder doing a stack update "in mirrors it owns" needs a working tree there, which runs repository-defined attributes and checks out Agent content beside the credential — still lawful under AD-9.
- **Proposed rule (tightens AD-9):**
  > A bundle leaves a container as one regular file of bounded size, read from a stopped container: the runtime accepts exactly one archive entry, of type regular file, under a name it chose, and streams it to a path it chose on the data volume; anything else is `infrastructure-error`. A Task volume is never mounted into the `hephaestus` container. Mirrors are bare, with no hooks and no working tree. The gateway runs only: fetch, bundle create, fetch from a bundle with object checking into `refs/hephaestus/incoming/<task-id>`, merge-base, and push. Anything that needs a working tree runs in a Workspace.

### H-3 — The lifecycle diagram forbids transitions the PRD requires and allows two Tasks to be Running

- **Pair:** v0.1 lifecycle vs v0.2 scheduling and Handoff vs v0.3 Rework; each reads the diagram as complete.
- **Gaps:**
  - `WaitingForCapacity --> Running` and `PullRequestOpen --> Running` enter Running without passing admission. If another Task was dispatched meanwhile, two Tasks run (FR-14). UJ-3 says Rework happens "in the next window".
  - FR-15 (interrupted work first) against FR-29 (resume within one polling cycle) against FR-12 ("every remaining Task is waiting"): one admit builder blocks the installation while a Task waits for capacity, another lets other Tasks pass.
  - No cancel from `WaitingForOwner` or `WaitingForCapacity`; FR-23 needs it.
  - Marker Label removed by a Trusted User while Running, waiting, or with a pull request open: AD-2 says "override", the diagram has only `Queued --> Withdrawn`.
  - Issue closed outside `PullRequestOpen`: no transition.
  - `Failed` has no way out. FR-40 says a draft stays a draft "until the Task below it is fixed"; SM-3 counts manual restarts.
  - `Running --> Failed: bound reached, draft pull request` cannot happen when Readiness or the first Implementation fails with nothing committed: GitHub will not open a pull request with no diff.
  - `Withdrawn` is final, yet the issue can be labelled again. "One issue is one Task" and the stored branch name collide with a second Task for the same issue; AD-5's "pull request by head branch" would find the first Task's pull request.
- **Proposed rule (replaces the affected arrows; adds to AD-1):**
  > Running is entered only from Queued, by dispatch; at most one Task is Running. `WaitingForCapacity --> Queued` when capacity returns; `PullRequestOpen --> Queued` on a Rework Command. Dispatch order: Tasks whose last step run was interrupted (restart or `usage-limit`), which ignore Schedules; then, inside an open window of their Project, Tasks with a Priority Label, then by issue number. A Task waiting for capacity does not hold the slot. Cancel applies in Queued, Running, WaitingForOwner, and WaitingForCapacity. A trusted Marker Label removal is Withdrawn from Queued and Cancelled from any other state that is not final. A trusted issue close is Closed from any state that is not final. `Failed --> Queued` and `Withdrawn --> Queued` on a trusted opt-in again; `Failed --> Merged` and `Failed --> Closed` follow the pull request. A Task is (Project, issue number) for life: opting in again reuses the Task, its branch, and its pull request, and continues its step-run sequence. A Task with no commit ahead of its start commit fails with an issue comment and no pull request.
- **Needs the Owner:** whether a Task waiting for capacity should block other Tasks is a product decision the PRD does not settle; the wording above picks "does not block".

### H-4 — "Reviewer choice" is a named policy with undefined inputs, and a Review can change the code

- **Pair:** intake and alias resolution vs reviewer policy vs Handoff (v0.2) vs the Review step.
- **Gaps:**
  - "The one that wrote the latest changes" is not defined. After a Handoff where the Fallback Agent ran and changed nothing, one builder takes the Agent of the last implementing step run, another the Agent of the last checkpoint that changed the tree. They choose different Reviewers.
  - `primary:opus` and `fallback:sonnet` can resolve to the same Agent. The Labels convention has no reviewer role, so the Reviewer's Model and Effort have no source.
  - FR-34 returns Findings to "the implementing Agent". After a Handoff that is ambiguous between the Primary Agent and the Agent that wrote last.
  - The Review container has the volume writable. A Reviewer that edits a file becomes, after the checkpoint, "the one that wrote the latest changes" and must then not review; its edits ship unreviewed.
- **Proposed rule (adds to Core policies; tightens AD-6):**
  > The author is the Agent of the latest step run of kind Implementation, Correction, or Rework whose checkpoint changed the tree. The Reviewer is the other of the Task's two Agents, run with the Model and Effort of whichever of `primary` and `fallback` resolves to it. `primary` and `fallback` must resolve to different Agents, or the Task does not start. Correction goes to the author; when the author has no capacity the other Agent corrects and becomes the author. Readiness and Review mount the Task volume read-only.

### H-5 — Context files, templates, and result schemas meet at a seam with no names on it

- **Pair:** methodology templates vs Core context assembly vs both adapters vs result schemas.
- **Gaps:**
  - AD-12 lists what the context set contains, not what the files are called or what format they have. A template "only points at" files whose names its author must guess.
  - "The same rendered instruction": rendered from what, with which variables? One builder substitutes, another copies.
  - AD-13 names four schemas (readiness verdict, plan, implementation summary, Findings) for six Agent step kinds; Correction and Rework have none.
  - FR-63 and FR-65 need an Escalation from Planning and from any BMAD workflow that wants a decision. The step diagram raises one only from Readiness and no schema can say "I need a decision"; a template that asks for it produces a result that fails validation and becomes `agent-error`.
  - The Reviewer needs the start commit to see the Task's own diff (in a Stack it is not the Base Branch); nothing carries it.
  - FR-65 puts the plan "in the pull request": as a committed file or in the description? One builder commits it, another renders it.
- **Proposed rule (new convention row; tightens AD-12 to AD-14):**
  > Context files have fixed names under `/hephaestus/context`: `instruction.md` (the template, verbatim), `task.json` (ids, branch, start commit, Base Branch, step kind, round), `issue.md`, `comments.json`, `plan.md`, `findings.json`, `validation.json`, `handoff.md`, `result.schema.json`. An input that does not exist is an absent file. Templates contain no substitution and refer only to these paths. Results by step kind: Readiness gives `ReadinessVerdict`; Planning gives `Plan`; Implementation, Correction, and Rework give `ImplementationSummary`; Review gives `ReviewFindings`. Every result has `outcome: completed | needs-decision`; `needs-decision` carries questions and options and becomes an Escalation from any Agent step. The plan lives in the database and is rendered into the pull request description; it is not committed. The adapter's prompt is one fixed sentence pointing at `instruction.md`.

### H-6 — Three components classify a failure, two enforce a timeout, and two bounds are spoken of as one

- **Pair:** workspace runtime vs agent adapters vs core loop.
- **Gaps:**
  - AD-6 has the runtime set the time limit; Docker has none, so the runtime needs a timer. AD-1 says "no in-memory timer carries meaning" and lists "the clock" as a Signal source. One builder puts the deadline in the runtime, another in the loop; a step can be stopped twice and classified twice.
  - A container killed at its deadline exits with a truncated stream. The runtime says `timeout`; the adapter, reading the stream, says `unknown` or `agent-error`. An out-of-memory kill and a `usage-limit` message before a non-zero exit pose the same question. AD-11 gives the set, not the precedence.
  - AD-1 retries an interrupted step "within its bound". The PRD has one bound, the Correction Loop (FR-34). Whether a restart or an `agent-error` spends a Correction round is undefined, as is the class of a step run interrupted by a restart.
- **Proposed rule (adds to AD-11; tightens AD-1 and AD-6):**
  > A step run records `deadlineAt` before its container starts. The runtime alone stops a container at its deadline; a deadline found passed at startup is handled as an interrupted step. The class is decided in one order: the runtime's cause first (`cancelled`, `timeout`, and out-of-memory or Docker faults as `infrastructure-error`), then the adapter's reading of a CLI that exited by itself, then Core (`agent-error` for a missing or invalid result). A step run interrupted by a restart is `infrastructure-error`. Two bounds: failed Validation and blocking Findings spend Correction rounds; `timeout`, `agent-error`, `infrastructure-error`, and `unknown` spend the step retry bound of that step kind (configurable, default 2). Exhausting either ends in Failed. `usage-limit` spends neither; Handoffs per Task have their own bound.

### H-7 — Two writers of the effect journal, and an idempotency key that most effects cannot form

- **Pair:** GitHub gateway vs core loop; workspace runtime vs core loop; Methodology epic (FR-66) vs everyone.
- **How both obey:** AD-5 says "the loop records an intent". AD-4 says every outbound call is made "under an effect-journal entry"; `IEffectJournal` is a Core port any adapter may call. A careful gateway team journals inside the adapter, the loop team journals outside: two entries per effect, or none when each relies on the other. "Every outbound call" read literally journals every poll.
- **Key shape:** `<task-id>:<step-run-sequence>:<effect-kind>:<ordinal>` cannot be formed for a state label change on a Queued Task, a rejection report before a Task exists (FR-6, FR-7), a re-asserted label, a window-end summary (FR-50), or an issue created from a Story (FR-66, no Task at all). Each builder will invent a shape, and the hidden marker carries it.
- **Not covered by AD-5's list:** volume and container removal, pull request update, and the natural key for merge and notification.
- **Proposed rule (tightens AD-4 and AD-5; replaces the Idempotency key row):**
  > Only the loop calls `IEffectJournal`. Every effecting port method takes the idempotency key as a parameter; adapters neither write the journal nor derive keys. Reads (polling, lookups, probes) are not effects. Key: `<scope>:<cause>:<effect-kind>:<ordinal>`, where scope is a Task id, `project:<id>`, or `installation`, and cause is `s<step-run-sequence>` or `g<signal-sequence>`. Removal of a volume or container, pull request update, merge, and notification are effects. On recovery a merge is resolved by the pull request's state, a removal by absence, and a notification is not sent again.

### H-8 — An invocation that carries secret values puts them in the database

- **Pair:** agent adapters vs workspace runtime vs persistence.
- **How both obey:** AD-10 has the adapter describe "environment, credential requirements, files". `ISecretStore` is a Core port, so an adapter may read the value and put it in the environment it returns. The loop then records "intent (run step)" with that invocation. AD-16's "the database holds secret names" is broken by code that obeyed AD-10 and AD-5.
- **Proposed rule (adds to AD-10 and AD-16):**
  > An invocation carries secret references — a name and a target environment variable or file path — never values. Only `IWorkspaceRuntime` resolves a reference, through `ISecretStore`, when it starts the container. Adapters and the loop never read a secret value. No value is written to the journal, a step-run record, a Docker label, or a container name.

### H-9 — Trust is decided on the author at fetch time, not on the text at the time it was vouched for

- **Attacks (break no Rule):**
  - AD-15 snapshots the body "when a Trusted User applies the Marker Label". Hephaestus learns of the label one polling interval later and reads the body as it is then. An outside issue author edits in that interval; unvouched text reaches the Agent (NFR-1).
  - A comment by a Trusted User is later edited by a collaborator who is not one. Its author is unchanged; the text is not theirs. AD-15 takes the actor "from GitHub's own event data", which for a comment is the author.
  - The title feeds the branch slug and the Agent; only the body is snapshotted.
  - A Trusted User renames their account; someone else takes the old login.
  - Hephaestus's own comments carry Agent-derived text; nothing says its identity is never trusted.
- **Proposed rule (tightens AD-15):**
  > Trusted Users, and actors on Signals, are compared by GitHub numeric user id; a login is display only. A title, body, or comment is admitted only as the exact revision whose author and every editor are Trusted Users. The issue snapshot is the revision current at the time of the Marker Label event, taken from GitHub's edit history, not the text present at poll time; where the history cannot establish it, nothing is admitted and the Task waits for the Owner. A later edit is a new Signal attributed to its editor. Hephaestus's own identity is never a Trusted User and its content is never admitted.

### H-10 — A Task branch can still change the rules of its own run, through Methodology and instruction files

- **Attack (breaks no Rule):** AD-16 pins `hephaestus.yml` to the Base Branch. BMAD's workflows live in the Project's repository (FR-61: "not available in the Project"; FR-64: "no Methodology files in its repository"), so the template shipped in the image points at files in `/workspace`. The implementing Agent rewrites the review workflow there; the Reviewer, a different Agent, follows it and reports nothing blocking. The same works through each CLI's own repository instruction files, which also means the two Agents do not receive the same instructions (FR-62), whatever AD-14 renders.
- **Proposed rule (adds to AD-14 and AD-16):**
  > Methodology files a Project supplies are read from the Base Branch at the commit of the Task's configuration snapshot and placed read-only at `/hephaestus/methodology`; templates refer to that path, never to a copy in `/workspace`. Adapters switch off loading of repository-supplied Agent instructions and extensions (memory files, skills, hooks, MCP, settings). What a CLI cannot switch off is declared as such, and the pull request names every such file the Task changed.

### H-11 — AD-7 lists what a step container lacks, not what it has

- **Attacks (break no Rule):**
  - **Shared volumes.** "A Task owns exactly one named volume" does not forbid a second mount. A per-Project package cache is the first optimisation a runtime builder will add: one Task poisons it, every later Task's Validation executes it. Mounting the data volume to write output directly is equally lawful.
  - **Root inside.** Project images commonly ship a set-user-id `sudo`. "Fixed non-root user" without `no-new-privileges` and with Docker's default capabilities is root in the container one command later.
  - **The host by network.** "No route to the Hephaestus API" is empty: the API is a Unix socket. Nothing denies the host gateway, link-local addresses (a cloud instance's metadata service), the Owner's local network, or a Docker daemon exposed on TCP — the control interface FR-17 forbids, reached without the socket.
  - **Exhaustion.** Time, processor, and memory are limited; process count and disk are not.
  - **Configuration.** The Project Configuration schema is open; a builder adding `volumes`, `services`, or `network` there obeys every AD.
- **Proposed rule (replaces the second sentence of AD-7):**
  > A step container is created with exactly these mounts: the Task volume at `/workspace`, context and agent binaries read-only, a temporary `HOME`. No other volume — no cache, data, mirrors, or secrets volume. All capabilities dropped, `no-new-privileges`, the default seccomp profile, a process-count limit, no published port, fixed user `10001:10001`. Project Configuration can set the image, resource limits within the installation's, Validation commands, and allowed egress hosts, and nothing else about the container. The workspace network denies the host gateway, loopback, link-local, and private ranges and traffic between containers; only public egress leaves it.

### H-12 — Credential write-back lets a compromised step replace the Owner's vendor account

- **Attack:** Open Question 3 asks how to write a refreshed credential back "without trusting the step container". No Rule forbids it meanwhile, and it is needed wherever a refresh rotates the token: the copy inside the step is refreshed, the stored one goes stale, and every later step fails. A builder who solves that by copying the file back gives a compromised Agent the write: it substitutes a credential for an account it controls, and every later Task's code and context is sent to that account — another Task's work, reached without leaving the container. FR-25 also has no home: "the CLI does nothing except call the API" and the secrets volume is mounted only into `hephaestus`, so there is nowhere lawful to run the vendor's own sign-in.
- **Proposed rule (adds to AD-6 and AD-16; closes Open Question 3 in principle):**
  > Nothing produced by a step container is ever written to the secrets volume. Sign-in and token refresh run the pinned CLI in a credential container with no Task volume, no repository content, and no context; it is the only source of that Agent's credential, copied out under the single-file rule (H-2). A step container receives a copy that is discarded with it. An authentication mode that cannot work without write-back from a step is unsupported.

### H-13 — The masking registry has no owner, misses split values, and never sees a push

- **Pair:** persistence vs gateway vs host logging vs runtime; each "passes text through the registry", none is said to hold it.
- **Gaps:**
  - It is not among AD-3's ports. Three adapters that may reference only Core each need the same instance.
  - A value split across two stream chunks passes a per-chunk filter. A refreshed token was never loaded. An encoded form (the same key in a basic-auth header) is a different string.
  - AD-4 masks "outbound text". A push is not text. An Agent that dumps its environment into a tracked file has it committed by the checkpoint and pushed by the gateway; FR-4's "a search of repository contents … finds nothing" fails with every Rule kept.
- **Proposed rule (tightens AD-4 and AD-16):**
  > The masker is one Core service built from `ISecretStore`, rebuilt when a secret changes. It matches across chunk boundaries and also each value's base64 and URL-encoded forms. It is applied where text is written: by the runtime before raw output reaches `IOutputStore`, by the gateway before every outbound body, by the logger. Before a push the gateway scans every new blob and commit message of the bundle with the same masker; a match blocks the push and raises an Escalation.

---

## Medium

### M-1 — Who polls, and where the cursor lives
The poller sits in `Hephaestus.GitHub`; the loop has a "reconcile" phase that reads human intent. One builder gives the adapter a background poller, another has the loop call `ICodeHost` each tick. Conditional-request state has no entity and the adapter cannot reference Persistence.
> The poller is the gateway's only background activity. Host starts it; it keeps its cursors through a Core port `IPollCursorStore` implemented in Persistence, and does nothing but append Signals. The loop never polls.

### M-2 — The clock as a Signal source
AD-1 lists "the clock" beside the poller and the API. A builder appends a tick Signal per interval; another evaluates due conditions inside the loop.
> The clock is not a Signal source. On each tick the loop compares recorded deadlines, resume conditions, and windows with `TimeProvider`. Signals come only from the poller, the API, and the runtime.

### M-3 — AD-16 and AD-17 disagree on how configuration is written; mutating calls have no reply contract
AD-16: Installation Configuration "is changed only through the local API". AD-17: handlers "that change anything append a Signal". Direct write, or Signal applied later while `config get` still shows the old value? The CLI builder expects an outcome, the API builder returns "accepted".
> Installation Configuration and secrets are written by API handlers directly, in their own transaction; they are not Task state, and the loop reads them at the start of each tick. Everything that affects a Task or a Project's work is a Signal: the endpoint returns 202 with the Signal id, and `GET /signals/{id}` reports `pending`, `applied`, or `rejected` with problem details.

### M-4 — Ports without a home; two candidate parsers of YAML
`ISecretStore`, `IOutputStore`, and `INotifier` are in AD-3 but in no row of the layer table ("a notifier adapter"). `hephaestus.yml` is "fetched by the gateway": parsed there, or in Core, which may reference "no infrastructure package"?
> `ISecretStore` and `IOutputStore` are implemented in `Hephaestus.Persistence`; `INotifier` in a new `Hephaestus.Notifications`, added to the reference diagram. The gateway returns configuration as raw bytes with its commit id; Core parses and validates it and every Methodology manifest. YamlDotNet is allowed in Core.

### M-5 — The bundle has no type at the port
The gateway makes the inbound bundle in its mirror; the runtime consumes it; they meet only through Core. A path (on which volume?), a stream, a byte array?
> A bundle crosses a port as `BundleRef { path, sha256, tip, prerequisite }` under `hephaestus-data/bundles/<task-id>/`. The producer writes it, the consumer reads it, the loop deletes it once the push result is journaled. An inbound bundle carries the Task branch and the Task's start commit as `refs/hephaestus/start`.

### M-6 — Checkpoint edge cases
"After every step" read literally gives an empty commit after Readiness and a container after Publish. Attribution "by the later step" has no recorded range to read. The trailers need Agent names the runtime must not know.
> A checkpoint with an unchanged tree makes no commit and no push. Checkpoints follow only steps that ran in a container on the Task volume. Each step run records `tipBefore` and `tipAfter`; attribution is read from those, never from trailers. Core supplies the whole commit message and identity; the runtime adds nothing to it.

### M-7 — Agent-derived text posted by Hephaestus can forge markers and trigger GitHub behaviour
Summaries and Findings are posted under Hephaestus's identity. The Task id is in every commit trailer and the key is predictable, so an Agent can emit `<!-- hephaestus:effect=<future key> -->`; recovery "by hidden marker" then finds it and never posts the real comment. A closing keyword with another issue's number closes that Task's issue on merge. Mentions notify strangers.
> Agent-derived text is neutralised before it is posted: HTML comments are escaped, mentions and issue-closing keywords are broken, command keywords are fenced. A hidden marker counts only as the final line of a body written by Hephaestus's identity whose key has a journaled intent.

### M-8 — Entities the PRD's history needs are missing; capacity is filed under the wrong owner
FR-53 keeps Handoffs and Escalations; neither is an entity. A Usage Limit belongs to an Agent credential, not to the Task that hit it; with the resume condition only on the Task, the next Task is dispatched to the same exhausted Agent.
> Add `ESCALATION`, `HANDOFF`, `AGENT_CAPACITY` (per Agent credential: exhausted-until, last probe), `WINDOW`, `ISSUE_REVISION`, and `POLL_CURSOR`. A Task's capacity resume condition refers to an `AGENT_CAPACITY` row; dispatch consults it before choosing an Agent.

### M-9 — A window is behaviour with no record
An on-demand start (FR-16, v0.1) "ends when the Queue is empty or the Owner stops it"; FR-13, FR-41, FR-50, and SM-5 all hang on a window's start and end. v0.1 will keep it in memory; v0.2 needs it to survive a restart.
> Dispatch of a Task with no step run requires an open `WINDOW` row for its Project, scheduled or on demand. Opening, closing, and the reason are recorded. The window-end summary and the merge pass at window start are driven from these rows.

### M-10 — Workspace removal is unjournaled and "ended" is undefined
AD-5 lists creation, not removal. FR-22 removes "after its Task has ended"; `PullRequestOpen` is not an end once Rework exists. v0.1 removes at the pull request, v0.3 expects the volume.
> The Task volume is removed, as a journaled effect, when the Task leaves Running and the journal shows its last checkpoint tip pushed; never otherwise. Any later step re-creates it from the mirror.

### M-11 — Two installations on one Docker host reap each other
"Development uses the same Compose file." Recovery finds containers and volumes "by Docker label"; the labels do not say whose.
> Every Docker object also carries `hephaestus.installation=<installation id>`; recovery and cleanup select on it.

### M-12 — v0.3 seams v0.1 will already have closed the wrong way
A dependency "has produced a branch" after its first checkpoint, long before it is reviewable; with one Task at a time and a dependency waiting for the Owner, the dependent Task starts on half a branch. A merge raced by a push merges an unapproved commit.
> A dependency is satisfied only when the Task it names is PullRequestOpen or Merged, or is Failed and FR-40 lets the dependent proceed. Every merge call carries the approved head commit id. Approval is evaluated at merge time from the current reviews, by numeric id.

### M-13 — Handoff: who writes the summary, how often, and FR-26 against AD-11
AD-12 lists a "handoff summary"; the outgoing Agent is out of usage and cannot write it. Limits that alternate hand the Task back and forth without end. FR-26 treats an unavailable Agent "as having no capacity"; AD-11 lets only `usage-limit` lead to Handoff or waiting. An availability check needs the CLI run in a container, which is no step kind.
> The handoff summary is assembled by Core from recorded data only; no Agent writes it. An Agent found unavailable before dispatch has no capacity: that is a capacity fact, not a failed step run, and AD-11 does not govern it. Availability and capacity checks are a `Probe` invocation the adapter describes and the runtime runs with no Task volume.

### M-14 — Findings have no identity across rounds
FR-34 records "what changed" per round and FR-35 lists "which Findings remain open". A second Review returns a fresh list; nothing ties it to the first.
> Core gives each Finding an id. A Review after a Correction receives the open Findings and returns `resolved` or `open` for each, plus any new Findings.

### M-15 — An outage has no state
GitHub is down when a checkpoint is to be pushed (NFR-12). "Every wait is a Task state"; none of the ten fits, and "bound reached" leads to Failed and a draft pull request that cannot be opened either.
> An effect that fails because the far side is unreachable is retried with a recorded next-attempt time and bound; the Task keeps its state. At the bound the Task moves to WaitingForOwner with the reason, not to Failed.

### M-16 — Timers and recovery outside the loop
The retention job has no owner; "Startup … reconcile unfinished effects and step runs" sits in the Host's list, yet only the loop may write step runs.
> Apart from the poller, no module acts on a timer. Startup recovery is the loop's first tick. Retention is a loop phase calling `IOutputStore.PurgeAsync(olderThan)`.

### M-17 — One schema must satisfy two CLIs; the invocation record is undefined
"Result schemas are generated from Core record types": each vendor's schema-constrained output accepts a different subset. What an adapter hands the runtime is described in prose only; a CLI that writes its result to a file needs the runtime to collect a path it was never told about.
> Result record types use only an object root, required properties, strings, integers, booleans, enums, arrays, nested objects, and nullable members. `StepInvocation { argv, env, secretRefs, files, resultSource }` is all an adapter hands over; `resultSource` is the output stream or one named file under `HOME`.

---

## Low

- **L-1 — `DateTimeOffset` in SQLite.** The provider cannot compare or order it in a query; "resume conditions due before now" will not translate. *Rule:* timestamps are stored as UTC ISO 8601 text with a fixed width through one value converter, so string order is time order.
- **L-2 — Copying a live SQLite file.** A file copy with a write-ahead log outstanding is not a consistent copy. *Rule:* the pre-migration copy and any backup are made with SQLite's backup operation, with the loop stopped.
- **L-3 — Usage shape.** One CLI reports cost, the other tokens. *Rule:* every usage field is nullable; absent is never stored as zero (FR-55).
- **L-4 — Effort after a Handoff.** The same Effort alias must mean something to both Agents. *Rule:* Effort is a closed Core enum; each adapter maps every member or declares it unsupported, checked at intake, not at step time.
- **L-5 — Mutable image tags.** The snapshot names a tag; the image behind it can change mid-Task. *Rule:* the configuration snapshot records the image digest resolved at Task start, and every step of the Task runs that digest.
- **L-6 — Entering a secret.** `docker exec … secret set NAME value` leaves the value in shell history and the process list. *Rule:* secret values are read from standard input only.
- **L-7 — Project identity.** A renamed or transferred repository. *Rule:* a Project is identified by GitHub repository id; owner and name are display only.
- **L-8 — Merge outside Running.** `Merge` is a step kind, yet FR-41 merges while the Task is PullRequestOpen. *Rule:* Merge and Publish are effects of the loop, not step runs, and do not take the single slot.
- **L-9 — Disk.** A Docker named volume has no quota; a filled disk stops SQLite. *Rule:* the runtime measures the Task volume after each step; over the configured size the step fails as `infrastructure-error` and the Owner is told.
- **L-10 — Output cardinality.** `STEP_RUN ||--o| OUTPUT_FILE` allows one file; a Validation step runs several commands. *Rule:* one output file per container run, each Validation command in its own container run, all indexed under the step run.
