---
title: 'Story 3.2: Run every step in a hardened container'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: 'a25cabf3e9a1f495bb04b2d401b88d1be65518f4'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Nothing creates a container: a step has nowhere to run that keeps an Agent away from the Owner's files, other Projects, and Docker itself, the Task volume does not exist, and the C library of a Project's image, which decides which Agent build is mounted, is not established (AD-5, AD-6, AD-7, AD-8, AD-18, FR-17, NFR-3, AR-46).

**Approach:** `IWorkspaceRuntime` in Core and `DockerWorkspaceRuntime` in `Hephaestus.Workspaces`, over Docker.DotNet.Enhanced. The runtime resolves a Project's image to a digest and a C library without running it, creates the Task volume with a root the step user can write, and creates and starts a hardened step container, returning at once. A restart finds a step run's container by its labels. The Compose file gives the Docker socket to the `hephaestus` service alone.

## Boundaries & Constraints

**Always:**
- A step container: user `10001:10001`, all capabilities dropped, `no-new-privileges`, the default seccomp profile, a process limit, no published port, processor and memory from the Task's settings within the installation's, and exactly these mounts: the Task volume at `/workspace`, read-only context, Methodology, and agent binaries, and a tmpfs `HOME` at `/hephaestus/home`.
- Every named volume is mounted `volume-nocopy`. Labels `hephaestus.installation`, `hephaestus.task`, `hephaestus.step-run`; names start with `hephaestus-`.
- The matching Agent builds are mounted at the same paths for glibc and musl; an image whose C library cannot be established, or that is built for another architecture, does not start the Task, and the message says why.
- Every sentence for a person is a message id in `en` and `pl`.

**Never:**
- Mount a host path, or the Docker socket, into a step container; mount a Task volume into `hephaestus`; run anything of a Project image in the orchestrator (the C library is read from files, in a container that is never started).
- Wait for a step to end, capture its output, bound its time (3.3), start the egress proxy (3.4), check out or checkpoint (3.5, 3.6), choose Agents or build invocations (3.8–3.11), or write Task state.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Start a step | Resolved image, Task volume, request | Container created, started, returned; hardened as above | N/A |
| Restart after a crash | A container of the step run exists (created, running, or exited) | That container is returned; started only if it never ran; none is made | N/A |
| glibc and musl images | Debian and Alpine | The step user writes `/workspace`; the matching Claude Code build is at `/hephaestus/agents/claude-code` | N/A |
| Reach the host | Docker socket, API socket, dashboard port, host path | Each fails | N/A |
| Unknown C library | Image with neither loader | Task does not start | `ImageNotUsableException` naming the image |
| Other architecture | Image built for another platform | Task does not start | `ImageNotUsableException` naming both |
| Unfetchable image | Registry unreachable and image absent | Task does not start | `ImageNotUsableException` with the reason |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Ports/IWorkspaceRuntime.cs`, `src/Hephaestus.Core/Workspaces/` -- the port and its records: `ResolvedImage`, `StepContainerRequest`, `StepLimits` (Project's request capped by the installation's), `WorkspaceFile`, `StepContainerLayout` (paths and the user).
- `src/Hephaestus.Workspaces/DockerWorkspaceRuntime*.cs` -- container definition, mounts (agent builds by volume sub-path), network, files placed by an inert helper container, task volume owner set by a tar entry, image resolution by looking for the dynamic loader. `WorkspaceLabels.cs`, `TarFiles.cs`, `WorkspaceRuntimeOptions.cs`.
- `src/Hephaestus.Host/Hosting/WorkspaceRuntimeSetup.cs`, `HostSettings.cs`, `ControlHost.cs` -- the runtime in the composition root; the installation id.
- `deploy/compose.yaml`, `.github/workflows/ci.yml`, `eng/coverage-exclusions.txt` -- the socket and its group, the CI checks, the Docker client's own sources excluded from coverage.
- `docs/step-containers.md`, `README.md`, `deploy/README.md`, `docs/branching.md`.

## Tasks & Acceptance

**Execution:**
- [x] Port, records, limits resolution, messages in `en` and `pl`, tests.
- [x] Docker runtime: step container, task volume, image resolution; tests against an Engine in memory and against a real Engine in CI.
- [x] Host wiring, installation id, Compose, CI checks, documentation.

**Acceptance Criteria:**
- Given each acceptance criterion of Story 3.2 in `epics.md`, then a test, a CI step, or a document shows it:

| Criterion | Shown by |
|-----------|----------|
| The call returns once the container is created | `StepContainerTests` (no wait or attach; started once), `DockerEngineTests` (the step is still running when the call returns) |
| User, capabilities, `no-new-privileges`, seccomp, process limit, no port, limits | `StepContainerTests` (the definition), `DockerEngineTests` (inspected, and the kernel's own account of `CapEff`, `NoNewPrivs`, `Seccomp` inside) |
| Mounts exactly as named; labels; `hephaestus-` names | `StepContainerTests`, `DockerEngineTests` (inspected mounts and labels) |
| Docker socket, API socket, dashboard port, host path unreachable | `DockerEngineTests` (a listener of the host is out of reach and reachable from an ordinary container; the socket paths are absent; no bind mount) |
| Socket mounted into `hephaestus` only; no Task volume in it | `ComposeFileTests`, CI step "The Docker socket is mounted into this container only" |
| A crash between intent and result makes no second container | `StepContainerTests` (found by labels, created-not-started is started, raced create), `DockerEngineTests` |
| `volume-nocopy`; the step user writes `/workspace` on glibc and musl | `StepContainerTests`, `TaskVolumeTests`, `DockerEngineTests` (Debian and Alpine) |
| C library established and the matching executables mounted at one path | `ImageResolutionTests`, `StepContainerTests` (sub-path per libc and architecture), `DockerEngineTests` |
| An image whose C library cannot be established stops the Task, and the Owner is told why | `ImageResolutionTests` (`ImageNotUsableException` with a message id in `en` and `pl`) |

## Implementation Notes

- Unattended run: no human gate. No Docker Engine is available where this was built, so the tests that need one (`DockerEngineTests`) are skipped here and run on the pull request's CI, as do the Compose checks of the `Image` job.
- Agent builds are mounted as one volume sub-path per Agent (`claude-code`, `codex-cli`) and one for `certs`, so an invocation's paths do not depend on the C library and the layout Story 3.1 fills is unchanged. Docker Engine 29 supports sub-path mounts.
- The Task volume (`hephaestus-ws-<task-id>`) is created and made writable by the runtime here, because the step user's write access cannot be shown without it; Story 3.5 journals that call. The configuration snapshot does not exist yet, so the resolved digest and C library are returned for the loop to record (Story 3.14).
- The workspace network is created internal by the runtime; step containers have no network until the proxy of Story 3.4 joins it.
- The effect key is stored in a fourth label, `hephaestus.effect`, and `hephaestus.role` tells a step container from an inert helper.
- Coverage: Docker.DotNet.Enhanced's source-linked files appear in the coverage reports and are excluded in `eng/coverage-exclusions.txt`.

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass; the real-Engine tests are skipped without Docker
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
- `python3 -m unittest discover -s eng -p "test_*.py"` -- expected: all pass

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| English fragment passed as a message argument | low | patch | A sentence of Hephaestus's own; it has its own id now (`workspace.image.missing`). |
| Comment says secrets are resolved through `ISecretStore` | medium | patch | The runtime resolves nothing; the comment now says the environment is readable by the socket and holds no secret. |
| Docs cite a threat model the diff does not touch | false | none | `docs/security/threat-model.md` lines 73 and 183 already say the orchestrator is root-equivalent. |
| Real-Engine tests skip silently in CI | high | patch | `RealEngine.Required` (`GITHUB_ACTIONS`): a guard test fails without an Engine, and the two skips of the host-reach test fail there. |
| Seccomp not shown positively; muddled host-path check | medium | patch | The kernel's `Seccomp:2`, `NoNewPrivs:1`, `CapEff:0` are asserted from inside; the muddled line is gone. |
| Wrong Docker group fails late and quietly | low | defer | Documented in Compose and the README; the readiness check belongs to Story 3.15. |
| `StepLimits` accepts zero or negative | medium | patch | Zero is no limit to Docker. The runtime refuses a limit of zero or a memory limit below 6 MiB before it creates anything. |
| A test name contradicts its assertion | low | patch | Renamed; the behavior (the installation's limit when the Project names none) is what the Project Configuration reference says. |
| Agent mount selection throws opaquely | false | none | `AgentLock.Parse` demands the whole matrix, so every Agent has exactly one build per C library. |
| Pull timeout and stream errors are not caught | medium | patch | A timeout and an error in the pull's stream are an image that cannot be fetched; the stream is read synchronously. |
| Stale local image used silently after a failed pull | low | none | Stated in `docs/step-containers.md`; an image built on the machine depends on it. |
| Operating system of the image not checked | low | none | The pull names the platform `linux/<arch>`; a Windows image cannot be on that platform. |
| Installation id not validated; changing it orphans objects | low | patch | The documentation says not to change it. The value is a label, which Docker takes whole. |
| Per-step-run volumes have no cleanup | medium | defer | Story 3.3 removes the container when the step ends and is the first to know; logged in `deferred-work.md`. |
| A found container is returned whatever its state | low | none | Removing a container left by an interrupted step is recovery (3.17); one loop writes (AD-1), so two calls for one step run do not race. |
| One 30-minute client limit covers every call | medium | patch | Five minutes for a request; a pull streams and has none. |
| Hardening gaps: writable root, no disk quota | low | patch | Stated in `docs/step-containers.md`; a size limit is AR-70. |
| Engine tests pull mutable tags | low | none | The spike used the same images; the runtime pins what it resolves. |
| Acceptance criteria have no traceability | low | patch | The table above. |
| Probe of an image with no entry point is refused by Docker | high | patch | Docker refuses to create a container from an image with no command; the inert helpers name an entry point. |
| Anonymous volumes of an image's `VOLUME` left behind and mounted in step containers | high | patch | Helpers are removed with their volumes; an image that declares volumes is refused, with a message in `en` and `pl`. |
| A workspace network that is not internal is accepted | high | patch | Inspected, and refused with `WorkspaceRuntimeException`, after a conflict as well. |
| Validation happens after side effects | medium | patch | The definition, limits, and files are checked before anything is created. |
| Helper removal failure other than a Docker error hides the real one | low | patch | Connection and time-out errors are swallowed too. |
| Digest-carrying name with a local copy for another architecture | low | patch | The copy is used only when it is for this machine; otherwise the right one is fetched. |
| Untagged image name makes Docker pull every tag | medium | patch | A name with no tag is pulled as `latest`. |
| File and directory of one name in one archive | low | patch | Duplicate paths and a file that is also a directory are refused. |
| A container that fails to start is retried forever | medium | patch | It is removed, so the retry makes a new one. |
| `/workspace` a symlink in the image | low | none | Not a case a toolchain image has; the step container would be refused by Docker as well. |
| Docker client never disposed | low | patch | The client is its own singleton, so the host disposes it. |
| Port comment overclaims label recovery | low | patch | Reworded: containers by label, other objects by name. |
| Concurrent calls delete each other's helper | false | none | One loop writes (AD-1); the conflict handling is for a restart, not for parallel callers. |
| CI mount check cannot fail; connect-to-nothing test name | low | patch | The mount list is compared with the expected one; the test is renamed. |
| Compose test indexes `volumes` on every service | low | patch | `TryGetValue`. |
| Coverage exclusions may not match real paths | false | none | The gate printed `excluded:` for both on a real run; CI's third-party paths are the same. |
