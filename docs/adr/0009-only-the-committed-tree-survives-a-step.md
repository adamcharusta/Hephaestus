# ADR-0009: Only the committed tree survives a step

- **Status:** Accepted (added by review; the owner confirmed it and its cost)
- **Date:** 2026-11-25
- **Spine:** AD-6, AD-9

## Context

ADR-0008 gives each step a fresh container on the same Task volume. An adversarial review showed that the volume itself carries executable state between steps that no diff ever shows:

- An agent can write a git hook or a `core.fsmonitor` entry into `.git`. The next step is the Review, in a container holding the other vendor's credential, and the first `git status` there executes the planted code beside that credential.
- An agent can replace an ignored file such as `node_modules/.bin/jest`. Validation then runs a test runner that always exits 0, and the agent's claim has decided the outcome after all.
- A checkpoint that runs git against an agent-written `.git` can be shaped or blocked by it.

## Decision

Only the committed tree survives a step.

- Before every step, a credential-free container re-creates `/workspace` from the last pushed commit: a fresh clone with no hooks, default configuration, and no untracked or ignored files.
- Only `Implementation`, `Correction`, and `Rework` steps are checkpointed. Whatever any other step leaves in the tree is discarded.
- The checkpoint container never uses the volume's `.git`. It builds its own git directory outside the Task volume, takes `/workspace` as work tree only, and writes exactly one commit on top of the last pushed commit. Commits the agent made during the step are flattened into it.
- Attribution comes from the commit range recorded for each step run, not from commit contents an agent could forge.

## Alternatives considered

- **Keep the volume; scrub it before sensitive steps** (delete hooks, rewrite git configuration, clean untracked and ignored files before any step with a different credential and before every Validation). Since Validation follows almost every step, this cleans almost as often and is easier to get wrong. Rejected.
- **Accept the risk and rely on the container boundary.** Leaves Validation forgeable and both credentials reachable. Rejected.

## Consequences

- **Dependencies are restored on every step.** For a typical project this adds minutes per step. A shared cache would remove the cost and reintroduce the problem, so none is allowed (ADR-0008).
- An agent's own commit history within a step is not preserved; each step becomes one commit.
- A step interrupted before its checkpoint still has its work in the volume until the checkpoint runs, so nothing is lost on a crash.
- The Task volume becomes scratch space that can be re-created from the mirror at any time, which simplifies cleanup.
