# ADR-0015: One host process; the CLI and the Dashboard are API clients

- **Status:** Accepted; amended 2026-11-25 (the Dashboard moved into v0.1)
- **Date:** 2026-11-25
- **Spine:** AD-17

## Context

The Owner configures Hephaestus, starts work, and asks for status from the command line. Later releases add schedules, which need a process that is always running. The single-writer rule (ADR-0002) must hold whatever interface is used. The brief asks for a logical separation between orchestration and execution without premature distribution.

The first version of this record kept the API on a Unix socket only and left the dashboard for after v1. The owner then asked for a dashboard from v0.1: a read-only seed that grows with each release, built with Angular. A browser cannot reach a Unix socket inside a container, so the process needs a network listener. That process holds the Docker socket and is equivalent to root on the host, which is what this amendment has to be careful about.

## Decision

**Control plane.** One long-running container, `hephaestus`, hosts the orchestrator loop and an HTTP API with two listeners.

**Control listener.** A Unix domain socket inside the container. It carries the whole API and is the only place an endpoint that appends a Signal, changes configuration, issues or revokes a Dashboard Token, or touches a secret is mapped.

**Command line.** The CLI is the same executable in client mode, run with `docker exec`. It does nothing except call the control listener. A request that affects a Task or a Project's work becomes a Signal: the endpoint answers 202 with the Signal's id, and the Signal's status later reports pending, applied, or rejected. Installation settings are written directly by the API in their own transaction.

**Dashboard listener.** A TCP listener that serves the Dashboard's static files and read endpoints only. Four rules bound it:

- *Read-only by construction.* No endpoint that is not a read is mapped on it, and a test fails when one is. Reads go through a read-only store on a read-only database connection, never through the loop's unit of work.
- *Local.* It is published on the host's loopback address only. The `hephaestus` container shares no Docker network with a step container or with the egress proxy. The listener refuses a request whose host name is not one of its own loopback names.
- *Token.* Every read requires a Dashboard Token in a request header. There is no cookie, and a token never appears in an address. Tokens are issued and revoked from the command line and stored only as a hash; revoking one closes the streams opened with it.
- *No foreign content.* The Dashboard is built when the image is built and served as static files. It loads nothing from another origin, the listener sends a content security policy to that effect, and text from GitHub and from Agents is rendered as text.

**Change stream.** The Dashboard stays current through one read endpoint that answers with Server-Sent Events. After each committed transaction the loop, and a control API handler after its own, announces what changed through a port the host implements. An event names a scope and an id, never content; the Dashboard then reads through the ordinary read endpoints, and reads everything on screen again when the stream reconnects. An announcement is not an effect and is not journaled. Open streams are bounded.

**Execution plane.** Steps run in short-lived containers the control plane creates (ADR-0008). The seam between the two is one port, the Workspace runtime, with the contract "start a step; its exit arrives as a Signal".

## Alternatives considered

- **The CLI opens the SQLite file directly.** A second writer that bypasses the loop and the journal. Rejected.
- **No daemon; the CLI does the work in the foreground.** Cannot serve schedules in v0.2. Rejected.
- **No network listener at all.** The original decision. It was rejected for a TCP listener on localhost because such a listener can be reachable from other containers, depending on network setup. The amendment answers that objection with the network rule, the token, and the absence of any changing endpoint, and accepts what remains.
- **A separate, unprivileged dashboard container** that serves the application and forwards reads. It keeps browser traffic out of the process that holds the Docker socket, at the price of another component to build, run, and secure. The owner chose the single process. It remains possible later without changing the read API.
- **Access from the local network, with sign-in and encryption.** Pulls authentication and remote access into v0.1. Deferred to Server Mode.
- **Actions in the Dashboard.** Would need protection against cross-site requests and a decision on entering secrets in a browser, which ADR-0013 forbids. Read-only for v0.1 to v0.3.
- **Asking again on a timer instead of a stream.** Simpler, but a view could be stale without saying so. Rejected by the owner.
- **SignalR.** Its browser transports carry the token in the address, its negotiation is a write request, and its channel is two-way. Each can be worked around, but that is three exceptions to the read-only rule for a one-way need. It becomes relevant if the Dashboard ever gains actions.

## Consequences

- Whoever can run `docker exec` on the host controls Hephaestus; in v1 that is the Owner.
- Whoever holds a Dashboard Token and can reach the host's loopback address reads Task history, issue text, and masked Agent output. They cannot change anything.
- The root-equivalent process now answers HTTP requests. A flaw in the dashboard listener is a flaw in the host. The threat model lists this as a known limitation (T34 to T38).
- The build needs Node.js as well as the .NET SDK. The runtime image does not.
- The Dashboard and the HTTP API are not part of the versioned public surface in v1: they ship in the same image and always match.
- Output of a step is not shown while it runs, because the stream is parsed after the container exits (ADR-0002).
- A remote implementation of the Workspace runtime port would give distributed execution without changing the loop.
