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

