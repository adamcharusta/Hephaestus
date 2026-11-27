---
title: 'Story 3.4: Route all Workspace traffic through an egress proxy'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '28f85ea8f8a6351eff0edda89828b8b7e326b9e1'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A step container is attached to a network that has no route out, so it has no network at all; nothing lets it reach a package registry or an Agent's vendor, nothing applies a Project's list of allowed hosts, nothing refuses a name that resolves to the Owner's local network, and nothing records what was refused (AD-7, AD-18, FR-17, NFR-4, AR-41, AR-47, AR-67).

**Approach:** Every step run gets an internal Docker network of its own, which keeps step containers apart, and an egress proxy container of its own on that network and on a network with outbound access. The proxy is a small forward proxy shipped in the `hephaestus` image (`hephaestus egress-proxy`), unprivileged, holding no credential, volume, or socket; the runtime hands it the step run's allowed hosts in its environment, reads the hosts it refused from its log at the step's end, and carries them on the step-exited Signal. The runtime sets the proxy variables and git's rewrite of `github.com` SSH addresses in every step container.

## Boundaries & Constraints

**Always:**
- A step container is attached to its step run's internal network alone; a proxy is the only thing on that network besides it, at the one fixed address `hephaestus-egress:3128`.
- The proxy resolves the name itself, refuses a destination if any address it resolves to is not public (loopback, link-local, private, and the other ranges that are not public), and connects to the address it vetted, never to the name again.
- The allowed hosts of a step run are the Project's list plus, only for a container that runs an Agent invocation, the hosts the invocation declares; with no Project list and the installation's default, any public host passes. The default is one constant in Core.
- Plain HTTP to an allowed host is passed, and so is a tunnel (`CONNECT`) to it.
- The step run's network, proxy, and step container are removed when the step ends, after the Signal; each carries the three labels, the `hephaestus-` name prefix, and a role.

**Never:**
- Put the proxy in the `hephaestus` process, give it a volume, a credential, or the Docker socket, or attach the `hephaestus` container, or any step container, to a network that has a route out.
- Set anything specific to a language or build tool in a step container; carry traffic that is not HTTP or HTTPS; intercept traffic.
- Write step-run state from the runtime (3.14 records the refused hosts from the Signal); start a container for checkout, checkpoint, or Validation (3.5, 3.6, 3.12).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Name outside the list | List set; `CONNECT` or `GET http://` to a host not in it | 403; `refused <host> not-allowed` in the proxy's log; the host on the Signal | N/A |
| Host on the list | Same list; the listed host | Passed | N/A |
| Private name | Any mode; name resolves to loopback, link-local, or private | 403; `refused <host> private-address` | One non-public address among several refuses the request |
| No list | Empty Project list | Any public host passes | N/A |
| Validation container | List set; the Agent's host not on it | Refused | N/A |
| Plain HTTP | `GET http://allowed/x` | Rewritten to origin form, `Connection: close`, passed | Ambiguous framing or a malformed head is a 400 |
| Bypass | A connection that skips the proxy | Fails: the network has no route out | N/A |
| Crash between network, proxy, and container | Restart finds some of them | Found by name and labels; none is made twice | A name taken by something else is an error |
| Proxy never listens | Container exits or no ready line in time | The step is not started; the Owner is told | Message id in `en` and `pl` |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Workspaces/EgressProxyAddress.cs`, `EgressPolicy.cs`, `EgressEnvironment.cs` -- the fixed host and port, the default and the combination of a Project's list with an invocation's hosts, and the variables set in every step container.
- `src/Hephaestus.Core/Workspaces/StepContainerRequest.cs`, `Signals/Signal.cs` -- `AllowedEgressHosts`; `RefusedHosts` on the step-exited Signal.
- `src/Hephaestus.Workspaces/Egress/` -- `EgressAllowlist`, `AddressPolicy`, `EgressProxyServer` and its connection handling, `EgressRefusals` (the log lines the runtime reads), `EgressProxyOptions`.
- `src/Hephaestus.Workspaces/DockerWorkspaceRuntime*.cs`, `WorkspaceLabels.cs`, `WorkspaceRuntimeOptions.cs`, `StepWatcher.cs` -- per-step network and proxy container, readiness, environment, removal, refused hosts.
- `src/Hephaestus.Persistence/` -- `RefusedHosts` column on `Signals`, a migration.
- `src/Hephaestus.Host/` -- `egress-proxy` mode of the executable, the image setting, composition.
- `deploy/compose.yaml`, `docs/adr/0021-egress-proxy.md`, `docs/step-containers.md`, `docs/architecture*.md`, `docs/project-configuration.md`, `README.md`.
- Tests beside each; `tests/Hephaestus.Workspaces.Tests/EgressProxyEngineTests.cs` runs the real proxy in CI.

## Tasks & Acceptance

**Execution:**
- [x] Core: address, policy, environment, request field, Signal field; tests.
- [x] Workspaces: the proxy and its tests on real loopback sockets; the runtime and watcher changes with tests on the Docker double.
- [x] Persistence: column and migration; tests.
- [x] Host: `egress-proxy` mode, image setting, compose; tests.
- [x] CI: build the image before the tests and run the proxy against a real Engine.
- [x] Documentation and decision record; story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| Step container on the internal network alone; a connection that skips the proxy fails | `StepContainerTests`, `EgressProxyEngineTests` |
| Proxy separate, unprivileged, no credential, volume, or socket | `StepContainerTests` (the proxy definition), `EgressProxyEngineTests` |
| Private, loopback, and link-local names refused; host and other step containers unreachable | `EgressProxyServerTests`, `AddressPolicyTests`, `EgressProxyEngineTests` |
| List refuses and records the host; a listed host is reached; no list passes public hosts | `EgressProxyServerTests`, `StepWatcherTests`, `EgressPolicyTests` |
| Allowed hosts are the list plus the invocation's, for an Agent container only | `EgressPolicyTests` |
| Proxy variables, loopback excluded, git rewrite, nothing ecosystem-specific | `EgressEnvironmentTests`, `StepContainerTests` |
| Decision record, address documented and in the public surface; default is one setting | `EgressProxyDocumentationTests`, `EgressPolicyTests` |
| Plain HTTP to an allowed host is passed | `EgressProxyServerTests` |

## Implementation Notes

- Unattended run: no human gate. No Docker daemon answers where this was built, so the real-Engine tests (`EgressProxyEngineTests`, the harness) are written for CI, where `build-and-test` now builds the `hephaestus` image first and names it in `HEPHAESTUS_TEST_PROXY_IMAGE`; without that variable they skip, and on GitHub Actions they fail instead. Every row of the matrix has a test that runs here: `EgressProxyServerTests` on real loopback sockets with name resolution and the connection replaced, `ProxyRequestTests`, `AddressPolicyTests`, `EgressAllowlistTests`, `EgressRefusalsTests`, `StepContainerTests`, `StepWatcherTests`, `EgressProxyCommandTests`.
- The proxy is `hephaestus egress-proxy`, a command of the one executable (`EgressProxyCommand`), rather than tinyproxy or squid: the rules of AD-7 (vet every resolved address, connect to the vetted address, per-step-run list, refusals read back) fit a small purpose-built proxy better than a configuration of a general one, and nothing extra is pinned (ADR-0021). It carries `CONNECT` to ports 443 and 80 only, and plain HTTP one request per connection.
- One internal network and one proxy per step run (`hephaestus-net-<step-run>`, `hephaestus-egress-<step-run>`) keep step containers apart without any rule in the proxy; `hephaestus-outbound` is the one network with a route out. The runtime refuses a step network that is not internal and an outbound network that is (`workspace.network.not-internal`, `workspace.network.not-outbound`).
- The matrix row on a crash: the step container is found by its labels and never made twice; the proxy holds no state and is replaced by name when the step container does not exist yet, or is missing when a found container has not started; a found container that runs keeps the proxy it has. The step run's network is found by name and reused.
- `HostSettings.EgressProxyImage` (`HEPHAESTUS_EGRESS_PROXY_IMAGE`) names the image the proxies are created from; Compose sets it to the service's own image. `EgressPolicy.AllowedHosts` builds a step run's list; the loop calls it when it starts steps (3.14), which is also when refused hosts are written to the step run from the Signal.
- The Signal gained `RefusedHosts`, the database the `RefusedHosts` column (migration `AddRefusedHosts`), `StepContainerRequest` the required `AllowedEgressHosts`.

## Spec Change Log

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| Accepted socket leaks on the 503 path | medium | patch | The refusal runs inside `using (client)`; `One_connection_more_than_the_limit_is_told_to_try_again` shows the slot is released afterwards. |
| CI probe runs `hephaestus serve egress-proxy` because of the image's entry point | high | patch | `--entrypoint hephaestus` added; the probe polls for the ready line and cleans up with a trap. |
| Busybox `wget` cannot tunnel HTTPS through the proxy | high | patch | HTTPS in `EgressProxyEngineTests` is asked for as a raw `CONNECT` through `nc`; plain HTTP stays on `wget`. |
| Names the proxy cannot resolve answer 502, not 403 | medium | patch | The isolation test accepts anything but 200 for name-based targets and demands 403 for addresses. |
| Fragment or control character in a plain request's path | medium | patch | The fragment is dropped, a control character is a 400 (`ProxyRequestTests`). |
| `CONNECT` to any port carries SSH or mail | medium | patch | Tunnels go to 443 or 80 only, refused with `port-not-allowed` and recorded (`A_tunnel_to_a_port_that_is_neither_https_nor_http_is_refused_and_recorded`). |
| ORCHID and benchmarking IPv6 ranges pass as public | low | patch | Added to `AddressPolicy` with tests. |
| `AcceptAsync` failure ends the accept loop | medium | patch | A `SocketException` while not stopping is logged and the loop goes on. |
| Semaphore released after disposal | low | patch | The slot is released in `HandleAsync`'s `finally`, which `DisposeAsync` awaits. |
| Ready line read from a proxy that has already ended | medium | patch | The state is read first; ready means listening and running (`A_proxy_that_said_it_was_listening_and_then_ended_stops_the_step`). |
| A found, not yet started step container is started with no proxy | medium | patch | The proxy is checked and replaced on that path (`A_found_container_that_has_not_started_gets_its_proxy_back...`). |
| An internal `hephaestus-outbound` gives every proxy no route out and nothing names the cause | medium | patch | `workspace.network.not-outbound` in `en` and `pl`; `An_outbound_network_that_is_internal_is_never_used...`. |
| `CreateOptions` not shown to carry `EgressProxyImage` | medium | patch | `The_options_carry_the_egress_proxy_image_the_settings_name`. |
| Harness fails instead of skipping with Docker and no proxy image | low | patch | `Installation.StartStepAsync` skips (fails in CI) like the Engine tests. |
| Removal message does not name a network | low | patch | "A container, network, or volume". |
| Stale diagrams and table rows name one workspace network | medium | patch | `docs/step-containers.md`, `docs/architecture-c4.md`, `docs/architecture.md`, `IWorkspaceRuntime` updated. |
| Threat model rows T7, T12, T32 not updated | medium | patch | Rows name the proxy and the tests behind them. |
| Spec cites test classes that do not exist; tasks unchecked | low | patch | Names corrected; tasks and notes written. |
| README says the list is applied ahead of anything producing it | low | patch | Worded as what the runtime hands the proxy; the ADR names 3.14 as the caller of `EgressPolicy.AllowedHosts`. |
| Proxy listens on IPv4 only | low | none | A Docker bridge network gives a container IPv4 by default; stated in the ADR's consequences. |
| Proxy and network left when the step container cannot be created after the proxy listens | low | none | The retry replaces the proxy and reuses the network; leftovers of an abandoned run are cleanup (3.17), as for the volumes of 3.3. |
| Two overlapping `StartStepAsync` calls for one step run remove each other's proxy | false | none | Only the loop starts steps, one at a time (AD-1); the Docker-side race the step container guards against is a duplicate container, not a second caller. |
| Docker's address pool allows about thirty networks | low | none | One Task at a time in v0.1; noted in the ADR. |
| The former `hephaestus-workspace` network is orphaned on upgrade | low | none | No release has shipped it; it carries the installation label, which cleanup (3.17) selects on. |
| Proxy of another installation with the same name removed without a label check | low | none | The name holds the step run's id; ADR-0021 records the replace-by-name choice. |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass; real-Engine tests skipped without Docker
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
