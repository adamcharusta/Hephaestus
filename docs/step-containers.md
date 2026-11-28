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
| Network | the internal network of its step run, `hephaestus-net-<step-run>`, which has no route out and whose only other member is the step run's egress proxy; no port is published |
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

The context of a step run is the file set of [Methodologies](methodologies.md): the template of the step kind as `instruction.md`, and the files of the Task. The context and the Methodology of a step run are placed in two volumes of their own, `hephaestus-context-<step-run-id>` and `hephaestus-methodology-<step-run-id>`, by a container that is created and never started; the step container
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
4. **Removal.** It removes the container, the step run's egress proxy and network, and the step run's context and Methodology volumes. The order is output complete, Signal, removal: a crash after the Signal leaves a labelled container for recovery to find (Story 3.17), and a crash before it
   leaves a container whose log is read again from the start, which replaces the output file.

What fails while a step is watched is reported to the host's log (with the step run's id and no output of the step), and what the step's end depends on is tried again every five seconds until it works or the host stops: the looks at
the container, the stop at its deadline, the start and the completion of the output file, and the append of the Signal. Output that cannot be read to its end, and a removal that fails, are reported once and not tried again; what a
failed removal leaves is for cleanup (Story 3.17). The output is recorded beside the look at the container, so a store that fails does not hold back the deadline; while Docker itself cannot be reached nothing can be stopped, and
the deadline is acted on at the first look after it answers. A file that was never started because the store kept failing is reported, and the step still ends with its Signal. When the host stops, watching stops and the containers are left as they are.

What is not done yet: output has no size limit and is kept without end (a limit and retention are later decisions), a log stream that breaks while the container runs is not followed again, and a step found after a restart that ended
while nobody watched is reported by its state, not as a timeout (Story 3.17).

## What a process inside cannot reach

The container holds no Docker socket, no Hephaestus API socket, and no host path. It is attached to one network, the internal network of its step run, which has no route out; the only other
thing on that network is the step run's egress proxy, so the host, the dashboard port, the Owner's local network, and every other step container are out of reach directly, and the proxy refuses
them too (below). Only the `hephaestus` container holds the Docker socket (`deploy/compose.yaml`); no Task volume is mounted into it, and it is on no network a step container or a proxy is on. The
tests that show this run against a real Docker Engine in CI: `DockerEngineTests` and `EgressProxyEngineTests` start step containers on glibc and musl images, inspect them and their networks, write
to the workspace as the step user, and try to reach a listener on the host, the paths of the sockets, another step container, and private addresses, directly and through the proxy. The Docker socket
makes the orchestrator equivalent to root on the host, and the threat model says so.

## The checkout before every step (AD-6, AD-9, Story 3.5)

Only the committed tree survives a step. Before every step a container of the `hephaestus-tools` image, which holds git and nothing else, makes `/workspace` again from one bundle: the commit that is the Task's last pushed tip, or its start commit on the Base Branch when nothing was pushed yet.
It runs as `10001:10001`, so that what it makes is the step user's, with no network, every capability dropped, `no-new-privileges`, and one mount, the Task volume with `volume-nocopy`. It receives no credential: its environment is the branch, the tip, and `HOME`. The bundle is the one file it is given, in
its own file system, so it mounts neither the data volume nor the mirrors.

What the checkout leaves is a fresh repository: made from an empty template, so it has no hook; with default configuration and no remote; the Task branch checked out at exactly the bundle's tip; no untracked or ignored file. A hook, a configuration entry, or an ignored fake test runner that
the step before planted does not exist afterwards, nor does a directory the step made unreadable. The checkout refuses a bundle that does not verify, has more than one head, or has another tip than the one it was told, and ends with a failure that stops the step from being started. A container a crash left is
replaced by name, and doing the checkout again makes the same workspace.

The gateway keeps the repository the bundle is made from: one bare mirror per Project on the `hephaestus-mirrors` volume, with no hooks and no working tree, which only the `hephaestus` container mounts. The orchestrator runs git only against a mirror, through one runner that is the only code that starts a process,
and never in a directory an Agent can write to. [ADR-0022](adr/0022-checkout-from-a-bundle.md) has the decision. The runtime fetches the `hephaestus-tools` image when the machine lacks it; `HEPHAESTUS_TOOLS_IMAGE` names it (Compose sets the image of the same release), and in a development checkout
`docker compose -f deploy/compose.yaml --profile tools build` builds it. The loop calls the checkout when it starts a step (Story 3.14); until then the tests and the harness do.

## The checkpoint after a step (AD-9, FR-20, Story 3.6)

Whatever a step of kind `Implementation`, `Correction`, or `Rework` changed is kept as one commit, however the step ended: normally, by timeout, by crash, or by cancellation. A container of the `hephaestus-tools` image runs as `10001:10001` with no network, no credential, every capability dropped, `no-new-privileges`, and one
mount, the Task volume, read-only and with `volume-nocopy`. It is given the bundle of the Task's last pushed tip, in its own file system, makes a git directory of its own from it outside the volume, and takes `/workspace` as work tree only. It never uses `/workspace/.git`: a configuration entry, a hook, or an attribute driver
an Agent left there is not read and nothing in it runs, and the volume is not written to.

The commit has exactly one parent, the last pushed tip, so what the Agent committed during the step is flattened into it. The tree is the work tree as it is, with `.gitignore` respected, so an ignored file such as `node_modules` is not committed. A repository the Agent made inside the workspace is not committed as a link, and its files are not captured. A tree
that equals the tip's makes no commit and no bundle. The message and the identity come from Core: a subject in the Task's language, then the trailers `Hephaestus-Task`, `Hephaestus-Step`, `Hephaestus-Agent`, `Hephaestus-Model`, and `Hephaestus-Effort`, by the bot user of Hephaestus's GitHub App. The checkpoint container has
exactly one regular file to leave, `/checkpoint/result.bundle`, a bundle of one head, `refs/heads/<Task branch>`, with the tip as its one prerequisite.

The runtime copies that one file out of the stopped container and hands it on as a bundle reference with its path under the bundles directory, its SHA-256, its tip, and its prerequisite. It refuses, as an infrastructure error, an archive with a link entry, a parent path, an extra entry, another name, or a file over the bound of 2 GiB, and a bundle whose header is not
that one head and that one prerequisite; nothing of a refused archive stays on the data volume. A step of another kind is not checkpointed: what it left in the tree is discarded by the next checkout. The gateway imports and checks the bundle before anything is pushed (see below); the loop asks for the checkpoint when a step has ended (Story 3.14), and until then the
tests and the harness do. [ADR-0023](adr/0023-checkpoint-as-one-commit.md) has the decision. `CheckpointScriptTests` run the script with the git of the machine; `CheckpointEngineTests` run it in the real tools image in CI.

## The push after a checkpoint (AD-4, AD-9, AD-19, FR-4, FR-44, Story 3.7)

Nothing a step container made reaches GitHub except as the bundle of its checkpoint, and the gateway is the only code that pushes it. It does so only after the checks below, in this order, and the first that fails blocks the push: the bundle's file is the file that was written (a regular file, its checksum, its size); the Task branch on GitHub is at the last pushed tip,
or absent when nothing was pushed; the bundle has one head, the Task branch; the bundle imports into a quarantine ref with git's strict object checking, and into no other ref; the new tip is a fast-forward of the last pushed tip; no new commit, tree, or blob holds a stored secret value, in plain, base64, or URL-encoded form; and the diff against the Task's start commit touches no protected path
(`.github/**` and `hephaestus.yml` unless the Owner changed the list), matched without regard to case. A branch that is already at the bundle's tip is not pushed to again: that is how a push that was made and not recorded is resolved after a crash.

The push is one refspec from the quarantine ref to `refs/heads/hephaestus/<issue>-<slug>`, with no plus, no force, and no delete, so GitHub refuses what is not a fast-forward as well. The loop journals it as an effect whose intent names the step run, the bundle's checksum, the tip before and the tip after, and the start commit, makes it, and deletes the bundle's file once its result is journaled.
A failed check ends the effect as refused and raises an Escalation that names the check; the Task waits for the Owner and its Workspace is kept. A Task branch that someone else pushed to is not pushed to or run on: the Escalation names the commits Hephaestus did not push, and a reply does not end the wait, only a Trusted User's `confirm` does (Story 3.18).
[ADR-0024](adr/0024-push-through-the-gateways-checks.md) has the decision. `GitHubPushTests` push to a repository on the machine with real git; the loop calls the push when a step has ended (Story 3.14), and until then the tests and the harness do.

## The egress proxy (AD-7, Story 3.4)

A step container's only way to the network is its egress proxy, reached at **`http://hephaestus-egress:3128`**. That host name and port are part of the versioned public surface: a build tool that does not
honour the standard proxy variables, such as Maven or Gradle, needs them written into the Project's image (`docs/project-configuration.md`). [ADR-0021](adr/0021-egress-proxy.md) records the decision.

**One network and one proxy per step run.** The runtime creates an internal network `hephaestus-net-<step-run>` and a proxy container `hephaestus-egress-<step-run>` for each step run, before the step
container, and removes both with it. The proxy is `hephaestus egress-proxy`: a command of the same image the orchestrator runs (`HEPHAESTUS_EGRESS_PROXY_IMAGE`, set by Compose to the service's own image),
in a container of its own as user `10001:10001` with every capability dropped, `no-new-privileges`, a read-only root file system, no mount but a small memory-only `/tmp`, no credential, no Docker socket, and no
published port. It is attached to the step run's network under the name `hephaestus-egress` and to `hephaestus-outbound`, the one network with a route out, which the runtime also creates. The step container is
created only once the proxy has written `listening 3128` to its log; a proxy that does not, within a minute, stops the step with `workspace.egress-proxy.not-ready`, which names the image to check for.

**What the proxy refuses.** It resolves the destination's name itself and refuses the request when any address in the answer is a loopback, link-local, private, or otherwise non-public address, whatever the
Project's list says; it connects to the address it tested, never to the name again. With a list of allowed hosts it refuses every host not on the list, by exact name in any case; with no list it passes any public
host. A refusal is a `403` to the client and one line in the proxy's log, `refused <host> not-allowed` or `refused <host> private-address`, each host once and at most a hundred.

**Whose list.** The hosts a step run may reach are the Project's `allowedEgressHosts` plus, only for a container that runs an Agent invocation, the hosts the Agent's adapter declares for it; a Validation container
gets the Project's list alone, so it cannot reach an Agent vendor's host the Project did not list. The installation's egress default is one constant, `EgressPolicy.Default`: open unless a list is set, in v0.1.

**What it carries.** HTTPS as a `CONNECT` tunnel it does not look into, and plain HTTP as one request per connection, rewritten to origin form with the host it checked (`dotnet restore` fetches a certificate
revocation list that way). Nothing else: a protocol an HTTP proxy cannot carry has no way out.

**What every step container is told.** `HTTP_PROXY`, `HTTPS_PROXY`, and their lower-case forms point at the proxy; `NO_PROXY` and `no_proxy` exclude `localhost,127.0.0.1,::1`, so a test database on the
container's own loopback still works; `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_0`/`VALUE_0`, and `GIT_CONFIG_KEY_1`/`VALUE_1` make git fetch `git@github.com:` and `ssh://git@github.com/` addresses over
HTTPS. These replace any variable of the same name an invocation sets. Nothing specific to a language or build tool is set.

**Refused hosts are recorded.** When the step ends, the runtime reads the proxy's log and carries the refused hosts on the step-exited Signal (`refusedHosts`), before the proxy and the network are removed. A
host an Agent's CLI needs that its adapter does not declare therefore shows up there, not as an obscure failure.

## Validation (AD-6, AD-13, FR-31, Story 3.12)

Whether work is acceptable is decided by the exit status of the Project's `validation` commands and by the Reviewer's Findings, and by nothing an Agent reported ([ADR-0028](adr/0028-validation-decided-by-exit-status.md)). The call is `IWorkspaceRuntime.StartValidationAsync`; the sequence of
commands and the decision are `ValidationRun` in Core. Nothing calls them in a Task's life yet: the loop does that in Story 3.14.

**One container for each command.** A command is `bash -c "<command>"` as the container's entry point, in the image the Task started with (`name@sha256:...`), as user `10001:10001` with the same hardening as any step container. It is its own container run with its own step run id, so it has one output file, a name
of its own, and its own step-exited Signal, and the runtime stops it at its own deadline. The deadline is the Task duration the Project asks for within the installation's, never more than 30 minutes (`ValidationRun.CommandLimit`).

**A fresh checkout first.** `StartValidationAsync` makes `/workspace` again from the bundle of the pushed tip, exactly as before any step ([above](#the-checkout-before-every-step-ad-6-ad-9-story-35)), and only then starts the command, so that a command never sees what an earlier command or step left in the tree.
When a restart finds the command's container, the tree is not made again beneath it.

**What it has and has not.**

| | Validation container | Agent step container |
| --- | --- | --- |
| Mounts | the Task volume, read-write, and the `HOME` tmpfs | the Task volume, the context, the Methodology, the Agent CLIs, and the `HOME` tmpfs |
| Credential | none | the credential of the Agent performing the step |
| Egress | the Project's `allowedEgressHosts` alone | the Project's list plus the hosts of the Agent's invocation |
| Environment | the proxy variables, git's rewrite, and `HOME` | the invocation's, besides those |

A request for a Validation container that holds a context file, a Methodology file, or a secret is refused before anything is created.

**Deciding.** `ValidationCommandResult.Ended` takes the exit status and the cause the runtime saw. Validation passes only if every command exited with status 0 (`passed`). A command that exits with another status ends it as `validation-failure`
and the commands after it are recorded as `not-run`. A command the runtime stopped at its deadline ends it as `timeout`, whatever status the stop gave it, and its output so far is kept. A command whose container was killed for using more memory, or was gone when the runtime
looked, ends it as `infrastructure-error` (AD-11): no exit status of the command is known, so it is neither a pass nor a failure of the Project's code. The first three are the classes `validation-failure`, `timeout`, and `infrastructure-error` of a failed step run.

**What is recorded.** For each command: its position, the command, its status (`passed`, `failed`, `timeout`, `interrupted`, or `not-run`), its exit status, its duration, and the step run whose output file holds what it printed (masked, as every container's output is). `ValidationRun.ToJson` writes it as `validation.json`, which a later step's
context carries ([Methodologies](methodologies.md)); there is no table for step runs yet (Story 3.14), so until then the results are what the caller keeps.

**Nothing is kept.** Validation is not a step kind that is checkpointed: no `CheckpointRequest` is made for it, so what a command wrote, built, or installed in the tree is not a commit and is not pushed. The tree is made anew by the next checkout.

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

## The credential of an Agent, and the probe

Story 3.10, FR-18, AD-16, AD-20, [ADR-0026](adr/0026-agent-sign-in-and-the-probe.md). `StepContainerRequest.SecretReferences` names the secrets of the invocation the container runs, and nothing else. `StartStepAsync` reads exactly those through `ISecretStore` once it has found that no container of the step run exists, and before it creates a network, a proxy, or a volume; a secret that is not stored refuses the start with `workspace.secret.missing`. The value is added to the container's environment, replacing a variable of the same name the invocation set, and goes nowhere else: no label, name, mount, volume, or proxy variable holds it, and a container that already exists is found, not given the value again. A reference whose target is a file, `HOME`, or a variable the runtime sets itself is refused with an `ArgumentException` before anything is created.
The configuration of a container is readable by whoever holds the Docker socket, which only the `hephaestus` container does.

`RunProbeAsync` runs an Agent probe to its end (AD-20): the fixed probe image ([`docs/agent-sign-in.md`](agent-sign-in.md)), the container of a step with the Task volume, the context, and the Methodology left out, and the labels `hephaestus.task` and `hephaestus.step-run` empty and `hephaestus.role=agent-probe`; its own internal network and egress proxy, named after the probe's id, with the hosts of the invocation as the proxy's whole list. It waits for the container to end, up to the request's limit, reads what it printed through the masker, cuts it to its last 256 KiB, and returns the exit code, why it ended, and the lines. A probe that is still running at its limit is removed by force and reported as timed out. The container, the proxy, and the network are removed whatever came of it. It appends no Signal, writes no journal line, and is not watched: it is not a step run.
