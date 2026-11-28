# Architecture Decision Records

Each record states one decision, the alternatives weighed, and what follows from it. The terse, binding form of the same decisions is the architecture spine (identifiers AD-1 to AD-20); [architecture.md](../architecture.md) tells the whole story in order.

**Status values.** *Accepted* means the project owner approved the decision on the date shown; where an independent review of the design later tightened a rule, the record says so. *Conditional* means the decision depends on a technical spike that has not been run.

| # | Decision | Status | Spine |
| --- | --- | --- | --- |
| [0001](0001-modular-monolith-with-hexagonal-boundaries.md) | Modular monolith with hexagonal boundaries enforced by project references | Accepted | AD-3 |
| [0002](0002-single-writer-orchestrator-and-fixed-lifecycle.md) | Single-writer orchestrator over a durable state machine; fixed lifecycle | Accepted | AD-1, AD-18 |
| [0003](0003-source-of-truth-split-between-github-and-sqlite.md) | Source of truth split between GitHub and SQLite | Accepted | AD-2 |
| [0004](0004-agents-as-cli-processes-behind-pure-adapters.md) | Agents as CLI processes behind pure adapters | Accepted | AD-10, AD-13 |
| [0005](0005-artifact-sufficient-steps-without-session-resume.md) | Artifact-sufficient steps without session resume | Accepted | AD-12 |
| [0006](0006-github-behind-a-single-gateway.md) | GitHub as the first task provider, behind a single gateway | Accepted | AD-4 |
| [0007](0007-effect-journal.md) | Effect journal for idempotent outside effects | Accepted | AD-5 |
| [0008](0008-workspace-shape-and-container-boundary.md) | Workspace shape and the container as security boundary | Accepted | AD-6, AD-7 |
| [0009](0009-only-the-committed-tree-survives-a-step.md) | Only the committed tree survives a step | Accepted | AD-6, AD-9 |
| [0010](0010-bundle-boundary-and-checkpoints.md) | Bundle boundary and checkpoint commits | Accepted | AD-9 |
| [0011](0011-agent-clis-pinned-and-injected.md) | Agent CLIs pinned and injected by Hephaestus | Accepted | AD-8 |
| [0012](0012-methodology-as-data.md) | Methodology as data | Accepted | AD-14 |
| [0013](0013-configuration-secrets-and-trust.md) | Configuration split, secrets, and trust admission | Accepted | AD-15, AD-16 |
| [0014](0014-local-persistence.md) | Local persistence in SQLite | Accepted | AD-16 |
| [0015](0015-one-host-process-and-cli-as-api-client.md) | One host process; the CLI and the Dashboard are API clients | Accepted, amended 2026-11-25 | AD-17 |
| [0016](0016-runtime-and-libraries.md) | Runtime and libraries | Accepted | Stack |
| [0017](0017-failure-classes-bounds-and-capacity.md) | Failure classes, bounds, and capacity | Accepted | AD-11, AD-20 |
| [0018](0018-task-branch-is-untrusted-code.md) | A Task branch is untrusted code in a trusted repository | Accepted | AD-19 |
| [0019](0019-language-is-data.md) | Language is data; the machine surface has one language | Accepted | AD-21 |
| [0020](0020-branches-quality-gate-and-image-channels.md) | Branches, the quality gate, and image channels for Hephaestus's own repository | Accepted | Conventions, NFR-18 |
| [0021](0021-egress-proxy.md) | The egress proxy: one per step run, built into the hephaestus image | Accepted | AD-7, AD-18, NFR-4 |
| [0022](0022-checkout-from-a-bundle.md) | The checkout of every step: a bare mirror, one bundle, a tools container | Accepted | AD-4, AD-6, AD-9 |
| [0023](0023-checkpoint-as-one-commit.md) | The checkpoint of a step: one commit made outside the volume's git directory, one bundle file read back | Accepted | AD-6, AD-9, AD-18 |
| [0024](0024-push-through-the-gateways-checks.md) | Push a Task branch only through the gateway's checks, as a journaled fast-forward | Accepted | AD-4, AD-5, AD-9, AD-19 |
| [0025](0025-drive-claude-code-through-its-official-interface.md) | Drive Claude Code with one hardened invocation and read what it printed, and name a failure only when the stream does | Accepted | AD-7, AD-10, AD-11, AD-12 |
| [0026](0026-agent-sign-in-and-the-probe.md) | Choose each Agent's sign-in from what its adapter supports, resolve its credential only when a container starts, and probe it in a fixed image with no Task volume | Accepted | AD-10, AD-16, AD-20 |
| [0027](0027-one-instruction-per-step-kind.md) | One instruction file per step run, from the Methodology's template for the step kind and nothing that depends on the Agent | Accepted | AD-12, AD-13, AD-14, AD-21 |
| [0028](0028-validation-decided-by-exit-status.md) | Decide Validation by the exit status of each of the Project's commands, run one container each on a fresh checkout | Accepted | AD-6, AD-11, AD-13, AD-18 |
| [0029](0029-publish-one-pull-request-per-task.md) | Publish one pull request per Task as a journaled effect of the loop, composed by Core from the facts its intent carries, found by its head branch | Accepted | AD-1, AD-4, AD-5, AD-19 |

The brief asked for records on eight topics. They map as follows: modular monolith (0001); agent integration strategy (0004, 0005, 0011); task provider abstraction (0006); local persistence (0014); workspace isolation (0008, 0009, 0010); workflow execution (0002); control plane and execution plane separation (0015, 0008); security boundaries (0008, 0010, 0013, 0018).
