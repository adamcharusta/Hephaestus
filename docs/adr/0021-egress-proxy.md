# ADR-0021: The egress proxy: one per step run, built into the hephaestus image

- **Status:** Accepted
- **Date:** 2026-11-27
- **Spine:** AD-7, AD-18, Deferred (egress proxy); NFR-4, NFR-15; AR-41, AR-47, AR-67

## Context

AD-7 fixes the mechanism: a step container sits on a Docker internal network whose only exit is an egress proxy, a separate unprivileged container with no credential, no volume, and no Docker socket, which resolves names itself, refuses any destination that resolves to a loopback, link-local, or private address, and applies each step run's allowed hosts by name. The spine left to this story which proxy is used, how it is started and given each step run's hosts, how its refusals are collected, how step containers stay unreachable from each other on a network they share with it, and the one host name and port at which a step container reaches it ([Story 3.4](../../_bmad-output/planning-artifacts/epics.md)).

The injection spike ran tinyproxy as a stand-in and showed that both Agent CLIs and seven package managers work through an HTTP proxy with the generic proxy variables alone ([the report](../spikes/injection/README.md)). tinyproxy applies no allowlist and does not vet the resolved address; squid can be configured to do both, but a configuration file per step run, a second image to pin and verify, and a proxy whose DNS answer is not the address it connects to (a rebinding gap) were the cost of a product the rules did not fit.

## Decision

**The proxy is Hephaestus.** `hephaestus egress-proxy` is a command of the one executable: a forward proxy for HTTP (`CONNECT` tunnels and plain requests in absolute form) of about four hundred lines in `Hephaestus.Workspaces.Egress`, free and open-source with the rest of Hephaestus (NFR-15). It opens no database, reads no secret, touches no file, and knows nothing of Docker. The runtime creates it from the same image the orchestrator runs (`HEPHAESTUS_EGRESS_PROXY_IMAGE`, which `deploy/compose.yaml` sets to the service's own image), so the proxy and the orchestrator are always one release and nothing extra is pinned or fetched.

**One network and one proxy per step run.** The runtime creates an internal bridge network `hephaestus-net-<step-run>` for each step run, and a proxy container `hephaestus-egress-<step-run>` attached to it and to `hephaestus-outbound`, a network with a route out that the runtime also creates. The step container is attached to its step run's network alone. Two step containers are therefore on two networks and cannot reach each other, the proxy of one is not reachable from the other, and no list has to be looked up per connection: the proxy of a step run holds that step run's list and nothing else. Every network and container carries the three labels and the `hephaestus-` prefix, so a crash leaves nothing unlabelled.

**The address is `hephaestus-egress:3128`.** The proxy joins the step run's network under the alias `hephaestus-egress`, and listens on 3128. The pair is part of the versioned public surface: a Project whose build tool needs proxy settings of its own (Maven, Gradle) writes this address into its image.

**The list reaches the proxy in its environment.** `HEPHAESTUS_EGRESS_ALLOWED_HOSTS` holds the step run's hosts separated by commas; absent means no list, so any public host passes. The list is the Project's `allowedEgressHosts` plus, only for a container that runs an Agent invocation, the hosts the invocation declares (`EgressPolicy.AllowedHosts`). The installation's egress default is the one constant `EgressPolicy.Default`, `open-unless-a-list-is-set` in v0.1; making the allowlist the default is a change of that constant.

**The address is vetted, not the name.** The proxy resolves the name itself, refuses the request when any address in the answer is not public (loopback, link-local, private, shared, multicast, reserved, and the documentation and transition ranges), and connects to the address it tested, never to the name again. One private address among several refuses the request, which is how a rebinding attack is made. An address written as a literal is tested the same way.

**Refusals are read from the log.** The proxy writes one line per host it refused, `refused <host> not-allowed` or `refused <host> private-address`, each host once and at most a hundred, and `listening 3128` when it is ready. The runtime waits for the ready line before it creates the step container, reads the log when the step ends, and carries the hosts on the step-exited Signal, which the loop records on the step run (AD-18). Nothing else is shared between the two.

**Plain HTTP is carried, one request per connection.** A request in absolute form is rewritten to origin form with the host the proxy checked and `Connection: close`, and a head that could make one request look like two (a folded header, a bare line feed, a body length stated twice) is refused. `dotnet restore` fetches a certificate revocation list over plain HTTP that way.

**What is set in every step container.** `HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY=localhost,127.0.0.1,::1`, and their lower-case forms; `GIT_CONFIG_COUNT=2` with two `url.https://github.com/.insteadOf` entries, for `git@github.com:` and `ssh://git@github.com/`. Nothing specific to a language or build tool.

## Alternatives considered

- **tinyproxy or squid in a second image.** Free, mature, and the spike ran tinyproxy. Neither vets the resolved address the way AD-7 asks without a configuration file and a resolver of its own, both need a second pinned image with its own verification, and squid's configuration per step run is more surface than the proxy itself.
- **One proxy for the installation on one workspace network.** Fewer containers, but every step container would see every other, the proxy would need to know which container asks for which list, and the per-step list of AD-7 would depend on reading the client's address. A network per step run gives the isolation for nothing and makes the list a start-up argument.
- **A proxy per Task.** Fewer starts, but the list is per step run (an Agent's hosts are allowed in the Agent's container only), so the proxy would have to be told when the step changes.
- **A sidecar in the orchestrator's process.** Ruled out by AD-7: the orchestrator holds the Docker socket.
- **A Compose service for the proxy.** The spine left this open; one proxy per step run cannot be a Compose service.
- **Refusals through a shared volume or an API call.** The proxy would need a volume or a credential, which AR-41 forbids; the log needs neither.

## Consequences

- Each step costs one network and one small container more; the proxy starts in about a second, and the runtime waits for its ready line before the step container is created.
- The `hephaestus` image must be on the Docker host under the name the orchestrator was given, which Compose guarantees. An image that is missing stops the step with `workspace.egress-proxy.not-ready`.
- The host must allow a container on a bridge network with a route out; a Docker host that restricts that cannot run a step.
- The proxy carries HTTP and HTTPS only: a tunnel is given to port 443 or 80 alone, so traffic that is not one of the two, and SSH to any host but `github.com`, has no way out (NFR-4 note). IPv6 destinations are vetted like IPv4 ones; whether the outbound network has IPv6 depends on the Docker host. The proxy itself listens on IPv4, which is what a Docker bridge network gives a container by default.
- Docker's default address pools allow a few dozen bridge networks at once. One Task runs at a time in v0.1, so one or two step run networks exist at once; a network that a crash left is labelled and removed by cleanup (Story 3.17), as is the `hephaestus-workspace` network of an installation that ran an earlier development build.
- The list of allowed hosts is built by `EgressPolicy.AllowedHosts` from the Project's configuration snapshot and the Agent invocation; the loop supplies it when it starts a step (Story 3.14). Until then the harness and the tests pass it.
- The threat model's open question 3 is answered, and T32 names the proxy: a compromised proxy of one step run lifts the network rule for that step run alone, and gains neither a credential nor a path to the host.
