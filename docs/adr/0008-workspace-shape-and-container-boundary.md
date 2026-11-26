# ADR-0008: Workspace shape and the container as security boundary

- **Status:** Accepted (the exact container definition was tightened by review; the owner later chose the egress proxy; confirmed by the injection spike on 2026-11-26)
- **Date:** 2026-11-25
- **Spine:** AD-6, AD-7

## Context

Agents execute arbitrary commands and process untrusted content. A Task must not reach the host's files, another Project, or the container runtime. Agents from two vendors work on the same Task, each with its own credential. The agent must be able to run the project's build and tests itself, in the project's own toolchain.

Both vendors document that, inside a container meant to be the boundary, the agent's own sandbox should be switched off: Codex's sandbox commonly fails under Docker's default profile, and Claude Code's permission bypass expects isolation and a non-root user.

## Decision

**Shape.** One named Docker volume per Task. One fresh container per step, removed when the step ends. Workspaces are never host bind mounts. Every container that mounts a Task volume mounts it with Docker's copy-from-image switched off (`volume-nocopy`). Without it Docker fills the empty volume from the image on first use, the volume's root ends up owned by root, and the step user cannot write to its own workspace.

**Credentials.** The container running an agent step receives only that agent's credential. Containers that run Validation, check out code, or commit receive none.

**Boundary.** The container contains the agent; the agent's own sandbox and permission prompts are off inside it and untouched everywhere else. A step container is created with:

- exactly these mounts: the Task volume, and read-only context, methodology, and agent binaries, plus a temporary home directory held in memory; no other volume, including no shared cache;
- a fixed non-root user, all capabilities dropped, `no-new-privileges`, the default seccomp profile, limits on time, processor, memory, and process count;
- a network from which the host, loopback, link-local and private ranges, and other containers are unreachable.

**Network.** Step containers are attached to an internal Docker network and to nothing else. The only exit is an egress proxy: a separate, unprivileged container with no credential, no volume, and no Docker socket. The proxy resolves names itself and refuses any destination that resolves to a loopback, link-local, or private address. It is never part of the orchestrator process.

Allowed hosts are set per step and applied by host name: the Project's list, plus the hosts an agent's adapter declares, the latter only for the container that runs that agent. A Validation container gets the Project's list alone. With no Project list the proxy passes any public host.

In every step container the Workspace runtime sets the proxy variables, with the container's own loopback excluded, and configures git through environment variables to fetch `github.com` over HTTPS where an SSH address is given. It sets nothing specific to a language or build tool. Hosts the proxy refused during a step are recorded with that step.

A step container reaches the proxy at one fixed host name and port. They are documented and part of the versioned public surface, because a Project that builds with a tool needing its own proxy settings writes them down. The values are chosen with the proxy.

Project Configuration can set the image, resource limits within the installation's, Validation commands, and allowed egress hosts, and nothing else about the container.

**Docker socket.** Only the orchestrator container holds it. A Task volume is never mounted into the orchestrator.

**Outside the container.** Every invocation disables tools that would act outside the container under the vendor account's authority, and switches off repository-supplied hooks, MCP servers, and settings. An invocation takes agent settings only from the temporary home directory, which Hephaestus controls, and from nothing in the repository. The tools it offers the agent are a closed list fixed by the adapter.

The switches that held in the injection spike, for the pinned versions:

- **Claude Code, with an API key and with a subscription sign-in:** `--safe-mode` with `--setting-sources user`, `--strict-mcp-config`, settings that disable all hooks, account connectors, and project MCP servers, and a tool list of `Bash`, `Read`, `Edit`, and `Write`. `--bare` or `--safe-mode` alone is not enough: either still applies the `env` block of the repository's settings, which could redirect where requests, and the credential with them, are sent. `--setting-sources user` is what stops it.
- **Codex CLI, with an API key:** the project marked untrusted, user configuration and rules ignored, and hooks, plugins, apps, web search, and the other account features disabled.

"Guaranteed" means shown by the planted-configuration test for the pinned CLI version and the authentication mode. An adapter that cannot show it declares the mode unsupported, and a change of pin, of a switch, or of the tool list repeats the test. For repository plugins the test is owed: no pinned version loaded one from a repository's settings, so nothing shows that a switch stops one, and no claim is made about them. Instruction files and skills in a repository are prompt content, not executable configuration; see the threat model, T2.

## Alternatives considered

- **One long-lived container per Task.** Simpler, but it accumulates both vendors' credentials and lets Validation code read agent tokens. Rejected.
- **Separate agent and toolchain containers.** The agent could not run build or tests without Docker access, which is forbidden. Rejected.
- **Docker-in-Docker, or a socket proxy for the orchestrator.** Does not change the fact that whoever creates containers can mount host paths. Not adopted.
- **Virtual-machine isolation** (gVisor, Kata). Stronger, but a host-setup choice, not something a default installation can require. Left to the Owner.
- **Firewall rules on the Docker host** for the network rule. They live outside what a Compose installation controls and differ between Docker Engine and Docker Desktop. Rejected.
- **DNS-based filtering** for the egress allowlist. It does not stop a connection made to an address directly. Rejected.
- **A proxy inside the orchestrator process.** It would put untrusted traffic into the process that holds the Docker socket and the GitHub credential. Rejected.
- **Transparent interception**, so that tools need no proxy settings. It needs network privileges inside the step container or firewall rules on the host, and both are ruled out above. Rejected for v1.
- **Requiring each Project to list vendor hosts.** Every Project would have to track hosts that change with the CLI version, and a missing one would look like an agent failure. Rejected.

## Consequences

- The orchestrator is equivalent to root on the host and is the trusted computing base. The threat model states this.
- A standard container is not a virtual machine; a runtime escape defeats these controls.
- No shared package cache means slower builds.
- One mechanism, the egress proxy, enforces both the network rule and the egress allowlist. It depends on both agent CLIs working through an HTTP proxy, which the injection spike showed: both honor the generic proxy variables with no setting of their own.
- A tool that cannot use an HTTP proxy has no network access from a step container. Traffic that is not HTTP or HTTPS does not leave it in v1.
- Proxy settings specific to one ecosystem belong to the Project; installation documentation must list the common ones. The injection spike ran seven package managers behind the proxy. npm, `dotnet restore`, pip, cargo, and go work with the generic variables alone. Gradle needs the proxy as JVM system properties. Maven needs a `<proxy>` element in its settings. Both name the proxy by host and port, which is why that address is fixed.
- `dotnet restore` fetches a certificate revocation list over plain HTTP, so the proxy must pass plain HTTP to an allowed host.
- Dependencies that need credentials stay unreachable, as before: a step container holds none.
- An allowlist names hosts, not what is sent to them: a credential can still be sent to a host on the list.
- Because allowed hosts differ between steps of one Task, the proxy is configured per step.
- Still to be decided with the v0.1 execution work: which proxy is used, how it is started and given each step's allowed hosts, how its refusals are collected, how step containers are kept from reaching each other on a network they share with it, and the host name and port at which they reach it.
- The spike ran on linux/amd64 under Docker Desktop, with short runs. Its report is [docs/spikes/injection/README.md](../spikes/injection/README.md).
