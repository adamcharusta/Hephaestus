- source_spec: `_bmad-output/implementation-artifacts/spec-1-6-mask-secret-values-wherever-text-is-written.md`
  summary: Mask each line of a multi-line secret (a PEM private key) on its own, with both line endings, when Story 1.7 stores one.
  evidence: The masker matches a value whole, so a tool that logs a stored key one line at a time is not masked; no multi-line secret is stored before Story 1.7 (edge-case review, finding 13).

- source_spec: `_bmad-output/implementation-artifacts/spec-1-9-define-aliases-limits-and-protected-paths.md`
  summary: Decide whether protected paths match case-sensitively when the gateway enforces them.
  evidence: The list stores globs exactly as typed, so `Hephaestus.yml` is not `hephaestus.yml`; on a case-insensitive checkout the file the default entry protects can be reached under another spelling. The enforcing story defines the matcher and must settle the case policy and add a test for the two default entries.

- source_spec: `_bmad-output/implementation-artifacts/spec-1-10-register-a-project.md`
  summary: Warn at `project add` when the repository is archived or disabled, and settle whether a ruleset-only protection shows in the `protected` flag of the branch.
  evidence: The gateway reads the repository from the installation's list and the branch's `protected` flag; an archived repository can neither be pushed to nor receive pull requests, and nothing in Story 1.10's acceptance criteria asks for the check. Whether GitHub sets `protected` for a branch covered only by a ruleset is unverified against live GitHub (review, findings 4 and 9); the message names the flag and claims nothing about what it covers.

- source_spec: `_bmad-output/implementation-artifacts/spec-2-1-notice-what-happens-on-registered-projects.md`
  summary: Decide how long the Signal inbox keeps applied Signals, and index it for the reads the loop will make.
  evidence: The `Signals` table only grows and has no index but the sequence and the unique source and event id; the loop (Story 2.2) is the first reader and knows what it asks for, and the retention of Signals is not in any story's acceptance criteria (review of Story 2.1).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-1-notice-what-happens-on-registered-projects.md`
  summary: Check against live GitHub that issue events become visible in id order, and add a look-back to the issue-event cursor if they do not.
  evidence: The cursor is the newest event id read, and a cycle stops at that id; an event shown late with a lower id would be skipped for good. The comment stream already overlaps by a second. Nothing in the repository, the research, or the spine says whether GitHub can show events out of order, so the claim is stated as an assumption in `docs/polling.md` (review of Story 2.1, edge-case finding).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-1-notice-what-happens-on-registered-projects.md`
  summary: Tell the Owner, with the time, that a command is waiting for a GitHub rate limit, instead of "GitHub is unreachable"; and consider a gate per installation.
  evidence: The gate is shared and global, so a limit the poller met makes `project add` and `github check` answer as if GitHub were unreachable for up to the stated wait (at most two hours), and GitHub's limits are per installation. The acceptance criteria treat the wait as temporary unavailability, so this is a message and granularity refinement for the command stories (review of Story 2.1).


- source_spec: `_bmad-output/implementation-artifacts/spec-2-2-opt-an-issue-in-with-the-marker-label.md`
  summary: Close the window between the Marker Label being applied and the poller reading the event, in which an edit to the issue's title or body ends up in the snapshot.
  evidence: The snapshot is the issue as GitHub's event data showed it when the event was read, up to one polling cycle after the label. An edit by anyone in that window is hashed as the admitted content, so the hash comparison of AD-15 cannot see it. Story 2.11 owns edit detection and must settle whether to compare the snapshot with the issue's `updated_at` and the edit history (open question 4 of the spine) (review of Story 2.2).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-2-opt-an-issue-in-with-the-marker-label.md`
  summary: Point the container's health check at the loop (`hephaestus health`) so that a stalled loop restarts the container, or decide that it should not.
  evidence: The health check is `hephaestus version`, which only shows that the API answers; `GET /api/health` and the `health` command now report the loop, and exit 1 when it is not alive, but nothing acts on that. Restarting on a stalled loop changes how the installation behaves under a fault and was not asked for by the story (review of Story 2.2).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-2-opt-an-issue-in-with-the-marker-label.md`
  summary: Decide what the Marker Label on an issue that is already closed means.
  evidence: The loop reads intent from events and never from the issue's open or closed state (AD-2), so a Trusted User's label on a closed issue creates a `Queued` Task. Story 2.3 defines a trusted close and reopening; whether a label on a closed issue should wait for a reopening is not in any acceptance criterion (review of Story 2.2).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-2-opt-an-issue-in-with-the-marker-label.md`
  summary: Decide how long the Signal inbox keeps applied and rejected Signals, now that the loop reads it.
  evidence: Story 2.2 added the status index the loop's reads need, but the `Signals` table, and now the issue body each labelled Signal carries, still only grow, and no story's acceptance criteria set a retention (review of Story 2.2).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-4-see-a-task-s-state-on-its-issue.md`
  summary: Add the move of a `Queued` Task (and of `WaitingForCapacity` and `PullRequestOpen`) to `WaitingForOwner` when an effect cannot be completed to the lifecycle diagram of the architecture spine.
  evidence: The diagram lists `Running --> WaitingForOwner: Escalation or effect retry bound` only, but Story 2.4's criteria name this move for a Task that has not started, and a Task that is `Queued` is the only kind that exists until dispatch. The code follows the criteria; the diagram is changed through `bmad-architecture` (review of Story 2.4).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-4-see-a-task-s-state-on-its-issue.md`
  summary: Cut the GitHub requests one label effect costs, now about eight (two reads of the installations and tokens, two label reads, the write).
  evidence: Every gateway call lists the App's installations, mints an installation token, and checks which installation holds the repository, and a change reads the labels again at the end. An installation token lasts an hour and could be kept, and the loop's read and change could share one client. It matters when many Tasks change state together or when the rate limit is near (review of Story 2.4).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-4-see-a-task-s-state-on-its-issue.md`
  summary: Decide whether an effect that GitHub keeps rate-limiting should be given up after some number of waits or some total time.
  evidence: A rate-limit wait counts as no failure and each wait is bounded by two hours, but the number of consecutive waits is not, so an effect that is always rate-limited is never moved to `WaitingForOwner`. Waiting out rate limits is by design (NFR-12); whether it needs an outer bound is not in any acceptance criterion (review of Story 2.4).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-4-see-a-task-s-state-on-its-issue.md`
  summary: Decide how long the effect journal is kept, now that it is append-only by trigger and only grows.
  evidence: Story 2.4 makes the journal append-only in the database, so retention cannot prune it without dropping the triggers. A journal line is small and one pending effect per Task answers hand-made label changes, but no story sets a retention for it (review of Story 2.4).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-5-say-on-the-issue-what-was-ignored-and-why.md`
  summary: Decide whether a rejected comment from a user who is not a Trusted User should be logged as a warning one by one.
  evidence: The criterion requires the Signal recorded as rejected, and the host logs every rejection as a warning, so a busy public repository, or a deliberate flood of comments, writes one line each. Aggregating them, or logging them at a lower level, is not in any criterion (review of Story 2.5).

- source_spec: `_bmad-output/implementation-artifacts/spec-2-5-say-on-the-issue-what-was-ignored-and-why.md`
  summary: Cut the cost of finding a comment by its marker, which reads every comment of the issue on each attempt.
  evidence: The search pages through the whole thread, and it runs before each post and on each retry; a long pull request thread costs several requests. A bounded read would need the comment's time or id, which the journal does not keep (review of Story 2.5).


- source_spec: `_bmad-output/implementation-artifacts/spec-2-10-answer-on-the-issue-to-resume-the-task.md`
  summary: A Trusted User's reply that was edited before the poller read it is dropped with no word on the issue, so the Task keeps waiting.
  evidence: Review found that `GitHubEvents.ReadComment` carries no text for an edited comment (AD-15: the editor is unknown), so nothing is admitted and nothing is said; Story 2.11 decides which edits are accepted and what is said, and should also tell the Owner to reply again.

- source_spec: `_bmad-output/implementation-artifacts/spec-2-10-answer-on-the-issue-to-resume-the-task.md`
  summary: The rule that a reply must be later than the Escalation compares GitHub's comment time with the host clock at the moment the Escalation was raised.
  evidence: Review found that a host clock more than a human's reaction time ahead of GitHub's would demote a genuine answer to context; anchoring on the question comment's own GitHub creation time would remove the dependency, and needs that time to be recorded when the comment is found by its marker.

- source_spec: `_bmad-output/implementation-artifacts/spec-3-2-run-every-step-in-a-hardened-container.md`
  summary: Remove the per-step-run context and Methodology volumes (`hephaestus-context-<id>`, `hephaestus-methodology-<id>`) when the step's container is removed.
  evidence: Story 3.2 creates both for every step run and nothing removes them yet; Story 3.3 removes the container when the step ends and is the first place that knows the step is over, so it must remove the two volumes with it, and recovery (Story 3.17) must remove those a crash left, selecting on the installation label.

- source_spec: `_bmad-output/implementation-artifacts/spec-3-2-run-every-step-in-a-hardened-container.md`
  summary: Report at startup, and in `hephaestus health`, that the Docker socket cannot be used, naming `HEPHAESTUS_DOCKER_GID`.
  evidence: The runtime connects to Docker only when the loop first asks, so a host whose socket group differs from the default 999 starts healthy and fails at its first Task; Story 3.15 (refuse to start what is not ready) is where readiness checks belong.

- source_spec: `_bmad-output/implementation-artifacts/spec-3-3-keep-what-a-step-printed-and-bound-how-long-it-runs.md`
  summary: Cap the size of the output file of a container run, and set how long output is kept and who deletes it.
  evidence: Story 3.3 writes everything a step prints to the data volume, so a step that loops printing fills it before its deadline, and nothing prunes `output/`. Retention is a loop phase in AD-1 and a size limit is AR-70, and no story sets either yet (review of Story 3.3).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-3-keep-what-a-step-printed-and-bound-how-long-it-runs.md`
  summary: Follow the log of a container again, from where it stopped, when the stream breaks while the container still runs, and say on the output file that it is cut short.
  evidence: The watcher reports a log stream that ends with an error and completes the file with what came, so the rest of a long step's output is lost and the file is indexed complete. The Engine restarting under a running step is the case (review of Story 3.3).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-3-keep-what-a-step-printed-and-bound-how-long-it-runs.md`
  summary: Report a timeout, not an exit, for a step found after a restart whose container the earlier watcher stopped at its deadline before the Signal was appended.
  evidence: The cause `timeout` is decided by the watcher that stopped the container; a watcher that starts on the exited container reports `exited` with exit code 137. Story 3.17 reconstructs the step from the container's state and the recorded deadline (review of Story 3.3).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-5-give-every-step-a-fresh-checkout.md`
  summary: Build and publish `deploy/tools.Dockerfile` as the `hephaestus-tools` image beside the `hephaestus` image, in the same channel and under the same tag, for both architectures.
  evidence: Story 3.5's criterion on the publishing workflow of Story 1.13 cannot be met here because that workflow does not exist yet; the Dockerfile and a `tools` Compose build target exist, and Story 1.13 must publish the image under the name `ghcr.io/adamcharusta/hephaestus-tools` that the host's `HEPHAESTUS_TOOLS_IMAGE` defaults to.

- source_spec: `_bmad-output/implementation-artifacts/spec-3-5-give-every-step-a-fresh-checkout.md`
  summary: Call the mirror fetch, the checkout bundle, and the checkout from the loop when a Task starts a step, and delete the Task's bundles and mirror leftovers during cleanup.
  evidence: Story 3.5 adds `IRepositoryMirrors` and `IWorkspaceRuntime.CheckoutAsync` and shows them with tests; the loop that sequences them with the step is Story 3.14, and the runtime deletes the bundle it consumes, but a bundle whose checkout was never asked for, and a `refs/hephaestus/bundle/*` name a crash left in a mirror, are Story 3.17's.

- source_spec: `_bmad-output/implementation-artifacts/spec-3-5-give-every-step-a-fresh-checkout.md`
  summary: Decide how a Project with git submodules or Git LFS files gets a complete checkout, and say it in the Project configuration.
  evidence: The checkout resets the work tree with no network and no filter, so a submodule's directory is left empty and an LFS file stays a pointer, and a build that needs either fails in a way that looks like the Agent's fault (review of Story 3.5).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-5-give-every-step-a-fresh-checkout.md`
  summary: Set a policy for the growth of the mirrors volume: prune what Projects no longer need and remove the mirror of a removed Project.
  evidence: A mirror only grows with its repository, and a removed Project's mirror is never removed; the volume is a cache and can be emptied by hand, but nothing bounds it (review of Story 3.5).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-6-checkpoint-a-step-s-work-as-one-commit.md`
  summary: Call the checkpoint from the loop when a checkpointed step has ended, hand the bundle to the gateway's import (Story 3.7), and delete the result bundle once the push is journaled.
  evidence: Story 3.6 adds `IWorkspaceRuntime.CheckpointAsync` and shows it with tests; the loop that sequences it after the step-exited Signal is Story 3.14, the import is Story 3.7, and a result bundle whose push was never journaled is Story 3.17's leftover to clean.

- source_spec: `_bmad-output/implementation-artifacts/spec-3-6-checkpoint-a-step-s-work-as-one-commit.md`
  summary: Decide how a repository an Agent made inside the workspace, and a submodule, are kept by a checkpoint.
  evidence: The checkpoint removes the entry of a nested repository from the index instead of committing a link to a commit that nobody can fetch, so the files of such a directory are not captured; they stay in the Task volume, which is kept until the push is journaled (AD-6).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-6-checkpoint-a-step-s-work-as-one-commit.md`
  summary: Give the checkpoint commit the bot user's id in its address, `<id>+<slug>[bot]@users.noreply.github.com`, so that GitHub links the commit to the App's avatar.
  evidence: `GitHubIdentity` carries the App id and slug and not the bot user's id, which GitHub serves from `GET /users/<slug>[bot]`; the address without the id is accepted but does not link. Attribution is informational (AD-9).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-6-checkpoint-a-step-s-work-as-one-commit.md`
  summary: Bound the disk a checkpoint container may write, and give the checkpoint its own memory and processor limits.
  evidence: The 2 GiB bound is enforced when the result is read back; inside the container `git add` and `git bundle` write to its layer with no quota, and `StorageOpt` works on some storage drivers only. The checkpoint shares the limits of the checkout, which read no tree (review of Story 3.6).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-7-push-task-branches-through-the-gateway-s-checks.md`
  summary: Call the branch check before every step and the push after every checkpoint from the loop, record the Task's start commit, and give the step run a record of its own with the tip before and after.
  evidence: Story 3.7 adds `ITaskBranches` and the loop's `Push` effect with its intent (`PushIntent`), and shows them with tests; journaling the intent in the transaction of the step-exited Signal, calling `CheckBranchAsync` before a step, holding the Task when the branch moved, and storing the commit a Task started from (the push needs it for the protected-path diff) are Story 3.14's, and the step run's record is Story 3.19's reading of the journal until then.

- source_spec: `_bmad-output/implementation-artifacts/spec-3-7-push-task-branches-through-the-gateway-s-checks.md`
  summary: Adopt the commits on a Task branch that Hephaestus did not push with the `confirm` command, and resume the Task from the remote tip.
  evidence: A branch that moved raises an Escalation whose resume condition is `confirm`, which a reply does not meet, so the Task stays held until Story 3.18 delivers the command; today the Owner can only cancel the Task by removing the Marker Label.

- source_spec: `_bmad-output/implementation-artifacts/spec-3-7-push-task-branches-through-the-gateway-s-checks.md`
  summary: Say, in the comment of an Escalation for a blocked push, which stored secret was found, by its name, and scan the forms the masker does not know (UTF-16, split lines).
  evidence: The scan finds the masker's forms in UTF-8 text and says the path of the object and never the value; the secret's name is not known to the masker, and a secret in UTF-16 or split over lines is not found (review of Story 3.7).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-7-push-task-branches-through-the-gateway-s-checks.md`
  summary: Delete the bundle files that a crash left, and the `refs/hephaestus/quarantine/*` names that a crash left in a mirror.
  evidence: The loop deletes a push's bundle once its result is journaled and the gateway removes the quarantine ref in a `finally`, but a host that stops between the import and the removal leaves both, and a bundle whose push was never journaled stays on the data volume; this is Story 3.17's cleanup, with the leftovers of Stories 3.5 and 3.6.

- source_spec: `_bmad-output/implementation-artifacts/spec-3-7-push-task-branches-through-the-gateway-s-checks.md`
  summary: Tell the Owner that the work of a step was not pushed when its check fails on a Task that already waits for the Owner for another reason.
  evidence: One Escalation asks for one decision, so a blocked push on a Task in `WaitingForOwner` ends the effect as refused (the journal names the check, the host logs an error with the Task and the key) and the bundle is deleted, but nothing is said on the issue; whether to hold the effect until the Task can be escalated again, or to add the finding to the open Escalation, is not in any criterion and changes how waiting works (review of Story 3.7, unverified intent).

- source_spec: `_bmad-output/implementation-artifacts/spec-3-7-push-task-branches-through-the-gateway-s-checks.md`
  summary: Give the reasons a push cannot be made for a reason that is not GitHub's their own message, so that a removed Project or a commit missing from the mirror is not reported as GitHub refusing the change.
  evidence: `CommitMissing`, `UnknownProject`, and `TooLarge` end as the reason `rejected`, which the comment words as GitHub's refusal; they are rare after the mirror was fetched, and the journal keeps the real cause (review of Story 3.7).
