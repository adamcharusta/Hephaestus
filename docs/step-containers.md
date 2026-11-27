# Step containers

Stories 3.2 and 3.3, AD-6, AD-7, AD-8, AD-18, AR-34. Every step of a Task runs in a container of its own that the orchestrator creates through the Docker socket. The container is the
security boundary: it does not depend on an Agent behaving. This page says what such a container is, how the image of a Project is resolved, and what is checked.
The runtime that creates the containers is `DockerWorkspaceRuntime` in `Hephaestus.Workspaces`, and `StepWatcher` sees each one to its end; the loop that calls the runtime, and the steps themselves, are later stories.

## The container

A step container is created from the image the Task started with and nothing else about it is left to the image or to the Project:

| What | Value |
| --- | --- |
| User | `10001:10001`, in every image |
| Capabilities | all dropped |
| Privileges | `no-new-privileges`; not privileged |
| Seccomp | Docker's default profile (none is named, none is weakened) |
| Processes | at most 512 |
| Processor and memory | the Task's settings, never more than the installation's limits (`hephaestus limit show`); 2 processors and 4 GiB when neither names one; no swap |
| Network | the internal workspace network `hephaestus-workspace`, which has no route out; no port is published |
| Entry point and working directory | replaced: the first element of the invocation and `/workspace` |

The processes inside see exactly these mounts, and no host path:

| Path | Holds | Mode |
| --- | --- | --- |
| `/workspace` | the Task's volume, `hephaestus-ws-<task-id>` | read-write |
| `/hephaestus/context` | the context files of the step run | read-only |
| `/hephaestus/methodology` | the Methodology's files | read-only |
| `/hephaestus/agents/claude-code`, `/hephaestus/agents/codex-cli` | the build of each Agent CLI that matches the image's C library | read-only |
| `/hephaestus/agents/certs` | `ca-certificates.crt`, copied from the `hephaestus` image's own store | read-only |
| `/hephaestus/home` | `HOME`, a tmpfs that is gone with the container | read-write |

Every named volume is mounted with Docker's copy-from-image switched off (`volume-nocopy`), so that what an image holds at a mount point can neither enter a volume nor change the owner
of its root. The Task's volume is made writable for the step user when it is created, which does not depend on the image. An invocation does not depend on the C library of the
image either: Claude Code is at `/hephaestus/agents/claude-code/claude` whether the image uses glibc or musl.

Every container, volume, and network carries the labels `hephaestus.installation`, `hephaestus.task`, and `hephaestus.step-run` (empty where the scope does not apply), and a name that starts with `hephaestus-`.
Two more are added: `hephaestus.role` says what a container is for, and `hephaestus.effect` holds the idempotency key of the effect that created it, so that the journal and Docker can be read against each other.
The installation label has the value of `HEPHAESTUS_INSTALLATION_ID` (`hephaestus` by default). A second installation on one Docker host names its own, and neither selects the other's objects. Do not change it on an installation that has run
Tasks: recovery and cleanup select on the label, so the containers and volumes made under the old value would no longer be found.

The runtime does not limit disk: a step can fill the Task volume or its own container's layer, and a size limit for Task volumes is a later decision (AR-70). The root file system of the container is writable, because a toolchain writes to it.

The context and the Methodology of a step run are placed in two volumes of their own, `hephaestus-context-<step-run-id>` and `hephaestus-methodology-<step-run-id>`, by a container that is created and never started; the step container
mounts them read-only. The runtime removes them with the container when the step ends.

The call that starts a step returns as soon as the container is started; it does not wait for the step to end (AD-18). When a container of the step run exists already, which is what a
restart after a crash between the intent to create it and its result finds, that container is returned, started if it had not been, and a second one is not made.

## How a step ends (AD-18, FR-19, FR-53)

`StartStepAsync` hands the container it started to the `StepWatcher` and returns; the loop never waits for a step. The watcher is the only code that sees a step container to its end, and it does this for each one:

1. **Output.** It follows the container's log from its start, standard output and standard error together, in the order they arrived, decodes it as UTF-8 (a byte that is not UTF-8 is stored as U+FFFD), passes it through the one masker, which
   finds a secret value that a chunk boundary splits, and writes the result to `IOutputStore`: one file for the container run, `output/<task-id>/<step-run-id>.log` under the data directory, indexed by a row of the `OutputFiles` table (the step run, the Task, the container,
   the size, when it started, and when it was completed). Bytes are in the file when they are written, so what a step printed before it was stopped is kept; a file the host stopped writing to is indexed as not complete, and the size
   in the row is that of the last time the row was written, which a killed host leaves behind the file. Output is not streamed
   while a step runs (AD-18). The Docker daemon must keep a log that `docker logs` can read; when it cannot be read, the runtime reports that the output could not be read to its end in the host's log, and nothing of it is stored.
2. **Deadline.** The request carries the instant the loop recorded on the step run before it asked for the container. The runtime looks at the container once a second, and when the instant has passed and the container still runs, it stops it,
   with ten seconds before the kill. Nothing else stops a container early. The wait is not Docker's own wait call, because a request of the client is limited to five minutes and a step may run for hours.
3. **One Signal.** It appends one step-exited Signal: source `runtime`, source event id the step run's id (so the inbox refuses a second one, which a restart could make), the exit code, and the runtime's own cause:

   | Cause | Meaning |
   | --- | --- |
   | `exited` | the process ended by itself, with any exit code |
   | `timeout` | the deadline passed while it ran, and the runtime stopped it |
   | `out-of-memory` | the kernel ended it for using more memory than its limit |
   | `lost` | the container was gone when the runtime looked; no exit code is known |

   Whether a CLI that ended by itself ended well is for its adapter to say (Story 3.16). The loop applies the Signal like any other, within a tick, whatever else is running.
4. **Removal.** It removes the container and the step run's context and Methodology volumes. The order is output complete, Signal, removal: a crash after the Signal leaves a labelled container for recovery to find (Story 3.17), and a crash before it
   leaves a container whose log is read again from the start, which replaces the output file.

What fails while a step is watched is reported to the host's log (with the step run's id and no output of the step), and what the step's end depends on is tried again every five seconds until it works or the host stops: the looks at
the container, the stop at its deadline, the start and the completion of the output file, and the append of the Signal. Output that cannot be read to its end, and a removal that fails, are reported once and not tried again; what a
failed removal leaves is for cleanup (Story 3.17). The output is recorded beside the look at the container, so a store that fails does not hold back the deadline; while Docker itself cannot be reached nothing can be stopped, and
the deadline is acted on at the first look after it answers. A file that was never started because the store kept failing is reported, and the step still ends with its Signal. When the host stops, watching stops and the containers are left as they are.

What is not done yet: output has no size limit and is kept without end (a limit and retention are later decisions), a log stream that breaks while the container runs is not followed again, and a step found after a restart that ended
while nobody watched is reported by its state, not as a timeout (Story 3.17).

## What a process inside cannot reach

The container holds no Docker socket, no Hephaestus API socket, and no host path; the workspace network has no route to the host, so the dashboard port and every other port of the host are
out of reach too. Only the `hephaestus` container holds the Docker socket (`deploy/compose.yaml`); no Task volume is mounted into it. The tests that show this run against a real
Docker Engine in CI: `DockerEngineTests` starts step containers on glibc and musl images, inspects them, writes to the workspace as the step user, and tries to reach
a listener on the host and the paths of the sockets. The Docker socket makes the orchestrator equivalent to root on the host, and the threat model says so.

The egress proxy, the only way out of the workspace network, is a later story (3.4). Until then a step container has no network at all.

## The Docker socket in Compose

`deploy/compose.yaml` mounts `/var/run/docker.sock` into the `hephaestus` service. The host runs as a user that is not root, so the service joins the group that owns the socket:

```bash
export HEPHAESTUS_DOCKER_GID=$(stat -c %g /var/run/docker.sock)   # Linux; the default, 999, is the usual group of the docker package
docker compose -f deploy/compose.yaml up -d
```

Docker Desktop publishes the socket in its virtual machine with a group the same command may not name; set the variable to the group of the socket as the container sees it.

## The Project's image (AD-8)

When a Task starts, the runtime resolves the image the Project Configuration names, without running anything of it:

1. It fetches the image for this machine's platform, and uses the copy that is on the machine when the registry cannot be reached. A name that carries a digest and is on the machine is not fetched.
2. It pins the image: every container of the Task is created from `name@sha256:...` (or the image id of an image built on the machine, which no registry knows), so a tag that moves later changes nothing for a Task that has started.
3. It checks that the image is built for this machine, `amd64` or `arm64`, and that it declares no volumes: Docker mounts an anonymous volume for every path an image declares with `VOLUME`, beside the mounts Hephaestus names, so
   such an image cannot give the isolation described above.
4. It establishes the C library from the dynamic loader the image holds, in a container that is created and never started: a musl loader (`/lib/ld-musl-*`) means musl, a glibc loader (`/lib64/ld-linux-x86-64.so.2`
   or `/lib/ld-linux-aarch64.so.1`) means glibc. An image with both is musl, because a musl build runs where glibc is only emulated and a glibc build does not run on musl alone.

An image that cannot be fetched, is built for another architecture, declares volumes, or holds neither loader does not start the Task, and the Owner is told why, in the installation's language. The image contract is Linux, glibc or musl, and
`bash`: an image built from scratch, or from a distroless static base, is refused. The result, the digest and the C library, is what the loop records in the Task's configuration snapshot (Story 3.14).
