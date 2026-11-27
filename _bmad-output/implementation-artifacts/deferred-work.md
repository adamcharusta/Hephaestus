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
