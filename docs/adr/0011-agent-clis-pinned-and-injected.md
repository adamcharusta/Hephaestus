# ADR-0011: Agent CLIs pinned and injected by Hephaestus

- **Status:** Accepted; confirmed by the injection spike on 2026-11-26 (the owner accepted injection; fetching instead of shipping the binaries was added by review)
- **Date:** 2026-11-25
- **Spine:** AD-8

## Context

A Project chooses its own container image, but the agent CLI must run in that same container so that it can build and test. Both CLIs change their output formats often, and an adapter is only trustworthy against the version it was tested with.

Verified facts: Claude Code is a self-contained native binary with glibc and musl builds, needs no Node.js at runtime, publishes a signed manifest of checksums, and can have updates disabled. Codex publishes statically linked builds on GitHub Releases, as two executables: `codex` and `codex-code-mode-host`. Claude Code is proprietary software and the vendor grants no licence to redistribute it; Codex is Apache-2.0.

## Decision

Each Hephaestus release pins, in `deploy/agents.lock`, one version of each agent CLI and the SHA-256 of every executable it consists of, per architecture and, where the vendor builds per C library, per C library. Claude Code is one executable in a glibc and a musl build. Codex CLI is two executables. Where the vendor publishes an archive, the lock pins the archive and each executable extracted from it, because an archive's digest cannot verify a file taken out of it.

Hephaestus images contain no agent binary. At startup the orchestrator downloads the pinned files from the vendors' official channels, verifies each against the lock, and fills a volume that is mounted read-only into whatever image the Project names. Startup stops unless every pinned executable is present and verified. The orchestrator also places in that volume a CA certificate bundle copied from the `hephaestus` image's own certificate store, for a CLI that needs one.

The vendor's signature over a release is verified when a pin changes, by a check in Hephaestus's own repository, not at startup. At startup the digest in the lock is the authority.

The Project image contract is: Linux, glibc or musl, `bash` present. Nothing else is required of it. When the runtime resolves the image digest at Task start it also establishes the image's C library, records both, and mounts the matching executables at one path, so an adapter's invocation does not depend on the C library. The digest is used for every step of that Task. CLI self-update is disabled in step containers. An adapter runs only against the CLI version it was released with.

No Hephaestus base image exists.

## Alternatives considered

- **Project images derive from a Hephaestus base image containing both CLIs.** Deterministic and simplest to build, but every Project needs a Dockerfile, and upgrading Hephaestus means rebuilding every Project image before the adapters match again. Not needed. A base image containing Claude Code could not be published by this project: the vendor grants no licence to redistribute.
- **Ship the binaries inside Hephaestus's images.** Removes the startup download, but redistributes vendor software in a public MIT-licensed image without a right to. Rejected in review.
- **Separate agent and toolchain containers.** See ADR-0008. Rejected.
- **Require CA certificates in the Project image.** Stock `debian:13-slim` and `ubuntu:24.04` ship none and would stop working with Codex. Rejected by the owner; Hephaestus supplies the bundle.
- **Verify vendor signatures at every start.** Once the lock pins a digest per executable, a signature adds nothing at startup, while it would put GPG and sigstore tooling into the image that holds the Docker socket. The signature matters at the moment a digest enters the lock. Rejected.
- **Let the adapter choose the Claude Code build by C library.** An invocation would then depend on a property of the image. Rejected; the runtime mounts the matching set at one path.

## Consequences

- A Project is added with one line, such as `image: mcr.microsoft.com/dotnet/sdk:10.0`.
- The adapter and the CLI are always the tested pair.
- First startup needs network access to the vendors' download hosts.
- The CA bundle is as fresh as the Hephaestus release: it is replaced at every start from the image's own store.
- A change of pin repeats the credentialed matrix and planted-configuration runs of the injection spike. It needs the Owner's credentials and is not closed by a dev agent alone.
- An image whose C library cannot be established cannot start a Task.
- Codex contacts `chatgpt.com` even with an API key; installation documentation says so.

## What the spike showed

Full report: [docs/spikes/injection/README.md](../spikes/injection/README.md).

- Both CLIs run from a read-only mount as a non-root user with no passwd entry and a throwaway home directory, with all capabilities dropped, on `debian:13-slim`, `ubuntu:24.04`, and `alpine:3.23` with `bash` added.
- The Claude Code build must match the image's C library; the other build does not start.
- Codex fails every request on an image with no CA bundle and works once `CODEX_CA_CERTIFICATE` names one. Without `codex-code-mode-host` beside it, a model can be unable to run any command and still exit 0.
- Each CLI can be made to disable account-linked tools and repository-supplied hooks, MCP servers, and settings in each supported authentication mode (ADR-0008). Whether a repository plugin is stopped was not shown.
- The vendors' terms allow fetching the binaries this way. Redistributing Claude Code is not permitted.
- Not run: linux/arm64, a native Linux Docker Engine, and rootless Docker.
