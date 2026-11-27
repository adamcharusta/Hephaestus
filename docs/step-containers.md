# Step containers

Story 3.2, AD-6, AD-7, AD-8, AD-18, AR-34. Every step of a Task runs in a container of its own that the orchestrator creates through the Docker socket. The container is the
security boundary: it does not depend on an Agent behaving. This page says what such a container is, how the image of a Project is resolved, and what is checked.
The runtime that creates the containers is `DockerWorkspaceRuntime` in `Hephaestus.Workspaces`; the loop that calls it, and the steps themselves, are later stories.

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
mounts them read-only. Removing them when the step ends is the story that stops and removes the container (3.3).

The call that starts a step returns as soon as the container is started; it does not wait for the step to end (AD-18). When a container of the step run exists already, which is what a
restart after a crash between the intent to create it and its result finds, that container is returned, started if it had not been, and a second one is not made.

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
