# ADR-0011: Agent CLIs pinned and injected by Hephaestus

- **Status:** Conditional on the injection spike (the owner accepted injection; fetching instead of shipping the binaries was added by review)
- **Date:** 2026-11-25
- **Spine:** AD-8

## Context

A Project chooses its own container image, but the agent CLI must run in that same container so that it can build and test. Both CLIs change their output formats often, and an adapter is only trustworthy against the version it was tested with.

Verified facts: Claude Code is a self-contained native binary with glibc and musl builds, needs no Node.js at runtime, publishes a signed manifest of checksums, and can have updates disabled. Codex publishes musl builds on GitHub Releases. Claude Code is proprietary software; no input established that its binary may be redistributed.

## Decision

Each Hephaestus release pins one version and checksum of each agent CLI per platform in `deploy/agents.lock`. Hephaestus images contain no agent binary. At startup the orchestrator downloads the pinned binaries from the vendors' official channels, verifies them, and fills a volume that is mounted read-only into whatever image the Project names.

The Project image contract is: Linux, glibc or musl, `bash` present. The image digest resolved at Task start is recorded and used for every step of that Task. CLI self-update is disabled in step containers. An adapter runs only against the CLI version it was released with.

## Alternatives considered

- **Project images derive from a Hephaestus base image containing both CLIs.** Deterministic and simplest to build, but every Project needs a Dockerfile, and upgrading Hephaestus means rebuilding every Project image before the adapters match again. Kept as the fallback if the spike fails.
- **Ship the binaries inside Hephaestus's images.** Removes the startup download, but redistributes vendor software in a public MIT-licensed image without an established right to. Rejected in review.
- **Separate agent and toolchain containers.** See ADR-0008. Rejected.

## Consequences

- A Project is added with one line, such as `image: mcr.microsoft.com/dotnet/sdk:10.0`.
- The adapter and the CLI are always the tested pair.
- First startup needs network access to the vendors' download hosts.

## What the spike must show

- Both CLIs run from a read-only mount as a non-root user with no passwd entry and a throwaway home directory, with all capabilities dropped, on Debian-, Ubuntu-, and Alpine-based images.
- The right Claude Code build is selected for the image's C library.
- Each CLI can be made to disable account-linked tools and repository-supplied configuration in each authentication mode.
- The vendors' terms allow fetching the binaries this way.
