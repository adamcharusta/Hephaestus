---
title: 'Story 3.10: Choose how each Agent signs in and check that it is ready'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '159b84e3c94c012b355df6139ff519240326a750'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** An adapter names the secret it needs, but the Owner cannot say how an Agent signs in, cannot supply its credential, cannot learn that the credential is broken before a Task does, and the runtime does not resolve a secret reference when it starts a container (FR-18, FR-25, FR-26, AD-16, AD-20, AR-58).

**Approach:** Core gains the Agent's chosen authentication mode, a capacity record per Agent credential, and a service that sets the mode and takes the credential, and checks an Agent with the adapter's probe. The runtime runs the probe in a fixed image with no Task volume and resolves a step container's secret references through `ISecretStore` at the moment it starts. `agent set-auth`, `agent check`, `agent list`, and the installation documentation follow the command tree of `EXPERIENCE.md`.

## Boundaries & Constraints

**Always:**
- The mode is accepted only when the Agent's adapter supports it; the refusal carries the adapter's own reason. The credential comes from standard input, becomes a secret under the name the adapter's reference gives, and no sign-in flow of Hephaestus exists. An empty standard input keeps the secret already stored; with none stored it is refused.
- A probe is `DescribeProbe` run by the runtime in the fixed probe image `debian:13-slim` pinned by digest, which belongs to no Project and meets the Project image contract, with the Agent mounts, the egress proxy limited to the adapter's hosts, and no Task, context, or Methodology volume. Its output is masked, read with `Parse` and `ClassifyExit`, and kept nowhere.
- A probe's finding is stored on the capacity record of that Agent credential (Agent and mode) as a status, a reason as a message id with numeric arguments, and the time. Text the CLI printed is never stored. An unavailable Agent has no capacity. No step run is written or failed.
- A step container resolves exactly the references of its own invocation, through `ISecretStore`, at the start; a value is written only into the container's environment, never into a label, a name, the journal, or the database. A reference to a file is refused by the runtime.
- Every sentence for a person is a message id in `en` and `pl`; commands, modes, and states are not translated.

**Never:**
- Wire the probe into dispatch, the loop, or a Task start (Stories 3.14, 3.15, 7.x); add the Codex adapter (Story 3.9) or a capacity limit; present a sign-in flow; accept a credential as an argument; edit the epics, the architecture, or the workflow.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Set | `claude-code`, `subscription`, token on stdin | Mode stored; secret `claude-code-oauth-token`; old probe of that credential cleared | N/A |
| Keep credential | Same, empty stdin, secret exists | Mode stored; secret unchanged | Secret absent: refused, nothing stored |
| Unsupported | Mode the adapter declares unsupported | Refused with the adapter's reason | Nothing stored |
| Unknown | Unknown Agent or mode; Agent with no adapter | Refused with the known tags, or "no adapter in this release" | Nothing stored |
| Check ready | Configured, probe ends well | `ready`, time of probe stored | N/A |
| Check unavailable | Credential missing, rejected, probe failed or timed out | `unavailable` with reason id, stored with time; no capacity | No step run touched |
| Not configured | No mode chosen | Refused: choose a mode first | Nothing stored |
| Probe cannot run | Docker or image failure | Problem with the runtime's message | Nothing stored |
| Step start | Invocation with references | Value resolved at the start, in the environment only | A missing secret refuses the start |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Readiness/` (new) -- `AgentSetting`, `AgentCapacity`, `AgentProbeFinding`, `AgentSignInService`, `ProbeImage`; the probe request and result under `Core/Workspaces/`.
- `src/Hephaestus.Core/Ports/` -- `IInstallationConfiguration` (mode, probe record), `IReadStore` (settings, capacities), `IWorkspaceRuntime.RunProbeAsync`.
- `src/Hephaestus.Workspaces/DockerWorkspaceRuntime.Probe.cs` (new), `.Container.cs`, `.Egress.cs`, `.cs` -- the probe container; secret resolution in `StartStepAsync`; `WorkspaceRuntimeOptions.ProbeImage`.
- `src/Hephaestus.Persistence/` -- two tables, migration, read and write methods.
- `src/Hephaestus.Host/Api/AgentApi.cs`, `Cli/CommandTree.cs`, `CliApplication.Agents.cs`, `ApiJson.cs`, `Documents.cs`, `ControlHost.cs` -- endpoints on the control socket and the commands.
- `src/Hephaestus.Core/Localization/*` -- message ids; `docs/agent-sign-in.md`, `docs/agent-clis.md`, `docs/step-containers.md`, `docs/adr/0026-*.md`, `README.md`.

## Tasks & Acceptance

**Execution:**
- [x] Core: records, service, ports, messages; tests.
- [x] Persistence: tables, migration, methods; tests.
- [x] Workspaces: secret resolution, probe run; tests on the Engine double and, in CI, a real Engine.
- [x] Host: API, commands, wiring; tests.
- [x] Documentation, README, story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| Mode accepted or refused with the adapter's reason | `AgentSignInServiceTests`, `AgentApiTests`, CLI tests |
| Credential from standard input stored as a secret | `AgentSignInServiceTests`, CLI tests |
| Probe in a fixed image, no Task volume | `ProbeContainerTests` |
| Finding stored on the capacity record; no capacity; no step run failed | `AgentSignInServiceTests`, persistence tests |
| References resolved at the start, value in no label, name, row, or journal | `StepContainerTests` |
| Documentation of both modes and the probe image | documentation tests |

## Implementation Notes

- Unattended run: no human gate, and no open question was left; the choices below were made and are recorded here and in ADR-0026. Implemented directly from this spec rather than by a subagent, because the investigation was already in this session.
- Codex CLI has no adapter yet (Story 3.9), so `AgentSignInService` takes `IEnumerable<IAgentAdapter>` and an Agent without one is refused with `agent.no-adapter`; the host registers `ClaudeCodeAgentAdapter` alone. The service names the credential by the reference of the adapter's probe for the mode, so no list of secret names exists in Core.
- A probe is `IWorkspaceRuntime.RunProbeAsync`, built from the step container's definition with the Task left out (`Guid.Empty` names none in `StepContainerRequest`, which the label code reads as no Task). The fixed image is `docker.io/library/debian:13-slim@sha256:8aa5...`, the digest of the multi-platform index read from Docker Hub on 2026-11-27, and `ProbeEngineTests` shows in CI that it is glibc with no declared volume and that a probe runs in it; that test could not be run here, where no Docker answers.
- The credential reaches a container as an environment variable only. A reference to a file is refused; no adapter needs one. `DockerWorkspaceRuntime` now takes the secret store and the masker.
- `MessageCatalog.Resolve` resolves an argument that is itself a `Message`, so that a sentence can carry the reason another gives (the runtime's own message inside `agent.probe.not-run`).
- The migration was generated with a temporary design-time factory and the Design package (neither is kept) and renamed to sort after the earlier ones; its snapshot was merged by hand, because the generated one dropped the filtered index of the effect journal that the earlier snapshot carries.
- `config show` is unchanged: `agent list` is where how each Agent signs in is read until the Dashboard's Installation view (Story 3.22).
- `README.md` changed: a section on Agent sign-in and the three commands.

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| `agent check` is cut off by the CLI's 30 s limit though a probe may take two minutes and a first pull longer | medium | patch | `ControlApiClient` takes a limit per call; `agent check` waits 15 minutes; tests in `ControlApiClientTimeoutTests`. |
| Docker faults (daemon down, refused create) escape `RunProbeAsync` as a 500, not as "probe could not run" | medium | patch | The runtime wraps them in `WorkspaceRuntimeException` (`workspace.probe.docker`); three tests in `ProbeContainerTests`. |
| A probe that outlasts a change of the mode or credential records a finding for the wrong credential | medium | patch | The service re-reads the setting after the probe and records nothing when it changed (`agent.probe.superseded`); test. |
| Two checks run two probes and race to write one record | low | patch | One probe at a time through a semaphore; two tests, one for a cancelled waiter. |
| A stored mode that the adapter no longer supports makes `DescribeProbe` throw (500) | low | patch | `CheckAsync` refuses it with the adapter's reason; test. |
| Choosing the same mode with a kept credential wipes `ExhaustedUntil` and the finding | medium | patch | The forgetting happens only when a credential was stored (`credentialStored`); persistence and service tests. |
| Empty standard input is reported like a stored credential | low | patch | The command says the credential "was already stored and was kept" (`cli.agent.set-auth.kept`); two tests. |
| The guide says "no log line" holds the credential, and that storing a credential forgets the finding, which `secret set` does not do | medium | patch | Reworded to what is tested; the guide says to check again after `secret set` or `secret remove`. |
| `ProbeRequest.ProbeId` doc says a crash-left probe is found by name | low | patch | Reworded: the id is new each time and recovery finds a leftover by its labels. |
| A probe a crash left is found by nothing and keeps the credential in its environment | medium | defer | Needs the recovery by labels of Story 3.17; deferred with its evidence, and stated in the guide and the ADR. |
| The log is read whole before it is cut to 256 KiB | low | defer | `ContainerLogs.ReadAsync` is shared with the helpers; deferred. |
| Usage limit and refused credential read alike in the reason | low | defer | The adapter classifies no usage limit and Story 7.1 owns it; deferred. |
| `ready` is not guarded against an empty or garbled result | false | none | `ClassifyExit` returns none only for exit 0 with a result that is not an error (`IAgentAdapter`, and the adapter's own tests); the service takes the adapter's reading, as AD-10 says. |
| Secret store write and setting write are not atomic | low | none | Everything acceptable is checked first; a failure of the second write leaves a secret the Owner stores again by running the command; adding a compensation is more than the defect. |
| Whitespace-only input keeps the stored credential | low | none | It is not an acceptable value either way, and the command now says it was kept. |
| `CheckoutPollInterval` is reused for the probe, and a surrogate pair or one line longer than the cut is lost | low | none | The same knob serves every helper's wait; a probe prints one short result line; cosmetic. |
| Reason arguments are not all numeric | low | none | The stored parameters are tags, names, and exit codes, never text of a CLI; the spec's word was loose, the code and ADR say "parameters". |
| The digest of the probe image is unchecked against a registry and has no refresh policy | low | none | The Engine test resolves it in CI; a change of it is a change of the release (ADR-0026). |
| Weak tests: language test asserts too little, a test without `await`, a double call, a re-serialised document, a name that over-claims | low | patch | Each reworded or fixed; the Polish reason is shown by two CLI tests. |
| Docs: story numbers in the guide, a later story unnamed, the wait not described | low | patch | The guide names Story 9.4, describes the wait and the one-probe rule; story numbers stay, as in `docs/agent-clis.md`. |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass; the real-Engine tests run in CI only
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
