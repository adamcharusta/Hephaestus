# Epic 3 Context: From issue to validated pull request

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

A queued Task runs end to end without a human in the loop: each step in a hardened container, the Agent CLIs pinned and supplied by Hephaestus, code crossing the Workspace boundary only as a git bundle, Validation by the Project's own commands, and one pull request per Task. The epic builds the execution spine: the Docker runtime, the egress proxy, checkout and checkpoint, the gateway's push checks, both Agent adapters, context assembly, failure classes with their bounds, and recovery after a restart.

## Stories

- Story 3.1: Pin, fetch, and verify the Agent CLIs
- Story 3.2: Run every step in a hardened container
- Story 3.3: Keep what a step printed and bound how long it runs
- Story 3.4: Route all Workspace traffic through an egress proxy
- Story 3.5: Give every step a fresh checkout
- Story 3.6: Checkpoint a step's work as one commit
- Story 3.7: Push Task branches through the gateway's checks
- Story 3.8: Drive Claude Code through its official interface
- Story 3.9: Drive Codex CLI through its official interface
- Story 3.10: Choose how each Agent signs in and check that it is ready
- Story 3.11: Give every Agent the same instructions for the same step
- Story 3.12: Let the Project's Validation commands decide
- Story 3.13: Publish one pull request per Task
- Story 3.14: Start a Project on demand and get a pull request
- Story 3.15: Refuse to start what is not ready and record who did what
- Story 3.16: Classify every failure and bound every retry
- Story 3.17: Lose nothing and duplicate nothing across a restart
- Story 3.18: React to what happens on GitHub during and after a Task
- Story 3.19: Reconstruct what happened to a Task
- Story 3.20: Remove a Workspace only when its work is safe
- Story 3.21: State the security assumptions and prove the claims made
- Story 3.22: Follow a Task's history in the Dashboard

## Requirements & Constraints

- The container is the security boundary, not an Agent's own sandbox: fixed non-root user, every capability dropped, no new privileges, the default seccomp profile, a process limit, no published port, limits from configuration within the installation's. No host path is ever mounted; only the orchestrator holds the Docker socket and no Task volume is mounted into it.
- A Task owns one named volume; each step runs in a fresh container removed when the step ends; a Task volume is always mounted with copy-from-image switched off.
- No GitHub credential ever enters a Workspace; a step container gets only the credential of the Agent performing the step.
- Every effect is journaled before and after; a Docker object is found again by its labels after a crash, and no second one is made.
- Every container, volume, and network carries the labels `hephaestus.installation`, `hephaestus.task`, `hephaestus.step-run`, and a name that starts with `hephaestus-`.
- The loop never awaits a step: the runtime starts a container and returns; it alone stops one at its deadline and appends one step-exited Signal.
- Every sentence for a person is a message id in `en` and `pl`; a security claim in user-facing text needs a passing test.

## Technical Decisions

- Step container layout: repository at `/workspace`, context at `/hephaestus/context`, Methodology at `/hephaestus/methodology`, Agent binaries at `/hephaestus/agents` with the CA bundle under `certs/`, `HOME` a tmpfs at `/hephaestus/home`.
- Agent binaries live in the `hephaestus-agents` volume in the layout `<agent>/<arch>-<libc>/<file>`; the runtime mounts the build that matches the Project image's C library, established when the image digest is resolved at Task start.
- Adapters are pure and know nothing of Docker; the runtime knows nothing of Agents. Docker access is through `Docker.DotNet.Enhanced`, tested against an Engine in memory and a real one in CI.
- Checkout and checkpoint containers run the `hephaestus-tools` image (git only). Bundles are the only way code crosses; the orchestrator runs no git command in a directory an Agent can write to.
- Workspace network is a Docker internal network; its only exit is the egress proxy, a separate unprivileged container with no credential, volume, or socket.

## Cross-Story Dependencies

- 3.2 gives 3.3 (output and deadline), 3.4 (proxy joins the workspace network), 3.5 (checkout, which creates the Task volume as a journaled effect), 3.8 and 3.9 (invocations), and 3.14 (snapshot of the image digest and C library) the runtime they build on.
- 3.1 supplies the agents volume that 3.2 mounts read-only.
