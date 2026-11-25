# ADR-0010: Bundle boundary and checkpoint commits

- **Status:** Accepted (import hardening and the pre-push secret scan were added by review)
- **Date:** 2026-11-25
- **Spine:** AD-9

## Context

A step container has no GitHub credential, so the orchestrator must push the work. The orchestrator holds the GitHub credential and the Docker socket. The agent controls the whole working copy, including `.git/config` and hooks; running git there executes whatever it planted.

Work must also never be lost when a step is interrupted, and the Owner must be able to tell which agent produced which commit.

## Decision

**Boundary.** Code enters and leaves a Workspace only as a git bundle. The orchestrator never runs git or any other tool in a directory an agent could write to. Mirrors are bare, with no hooks and no working tree.

**Checkpoint.** After every step that may change code, including one ended by timeout, crash, or cancellation, a credential-free container writes one commit and one bundle file. An unchanged tree produces neither. The core supplies the commit message, which carries informational trailers naming the Task, step, agent, model, and effort.

**Crossing.** The runtime takes exactly one regular file of bounded size, under a name it chose, from a stopped container. Anything else is an infrastructure error.

**Import and push.** The gateway imports the bundle into a quarantine ref with strict object checking before any other ref moves, accepts only the Task branch, verifies it is a fast-forward of the last pushed tip, scans every new blob and commit message for secret values, and pushes. A failed check blocks the push, keeps the Workspace, and raises an Escalation.

**Branches.** Task branches are named `hephaestus/<issue-number>-<slug>` and are pushed fast-forward only.

## Alternatives considered

- **The orchestrator mounts the Workspace and runs git there.** Simplest, and exactly the code-execution path described above. Rejected.
- **A push-only token inside the Workspace.** Breaks the rule that agents hold no GitHub credential. Rejected.
- **Let the agent commit and push whatever is on the branch.** The agent may not commit, may rewrite history, and attribution becomes guesswork. Rejected.

## Consequences

- The branch on GitHub always holds the work done before an interruption.
- Git 2.48 or later is required in the orchestrator and tools images: earlier versions ignore object checking when fetching from a bundle.
- A stack update that needs a working tree (v0.3) must run in a Workspace, not in a mirror.
- How stacks are updated after rework and merge, and whether that needs an exception to fast-forward-only, is deferred to v0.3.
