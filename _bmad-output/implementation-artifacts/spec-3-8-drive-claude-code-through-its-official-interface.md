---
title: 'Story 3.8: Drive Claude Code through its official interface'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: 'f4365c19e1f9ee7508583ccef2c048bbe0bb91f2'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Nothing describes how to start Claude Code with no one watching, or reads what it printed. `IAgentAdapter` knows only a thin invocation; there is no probe, no parser, no failure reading, and no test shows that a repository's own hooks, MCP servers, and `env` block stay inert (AD-7, AD-10, AD-11, AD-12, AR-19, AR-56, FR-24).

**Approach:** `Hephaestus.Core` gains the shared contract: the invocation with secret references and a result location, the authentication mode, support declarations, the normalized events and result, and `FailureClass`. `Hephaestus.Agents` gets `ClaudeCodeAgentAdapter`, pure, built on the invocation Story 1.1 proved. A credential-free test starts the pinned CLI against a planted repository and shows nothing of it takes effect.

## Boundaries & Constraints

**Always:**
- One argument vector for both modes: `claude -p <fixed sentence> --output-format stream-json --verbose --dangerously-skip-permissions --no-session-persistence --model <model> [--effort <level>] [--json-schema <schema>] --safe-mode --setting-sources user --strict-mcp-config --settings <json> --tools Bash,Read,Edit,Write`. Only the secret reference differs: `anthropic-api-key` to `ANTHROPIC_API_KEY`, or `claude-code-oauth-token` to `CLAUDE_CODE_OAUTH_TOKEN`. Egress host: `api.anthropic.com` in both modes at 2.1.332.
- The environment holds the switches Story 1.1 proved (self-update, connectors, nonessential traffic, and the others), never a secret value. The prompt is one fixed sentence in Core, shared with Story 3.9, that points at `/hephaestus/context/instruction.md`.
- All five Core Efforts map to `--effort`; an Effort or mode the adapter cannot map is declared unsupported, with a message id, and `DescribeInvocation` refuses it; it never substitutes.
- The result is found in the output stream: `structured_output` of the final `result` event when a schema was given, its `result` text otherwise. Usage is null where the stream did not report it, never zero.
- A failure class is returned only when the stream names it (an `authentication_failed` error, or a result subtype that starts with `error_`: `agent-error`); anything else that failed is `unknown`; a run the stream shows as finished well returns none. A usage limit is not classified here: its signals are Story 7.1's.
- The adapter is pure: no process, nothing of Docker. It runs only against the pinned version (`deploy/agents.lock`); a test fails when they differ.

**Never:**
- Wire the adapter into the loop, the runtime, or the host; resolve a secret; add the CLI's sign-in flow; send `--bare`, `--resume`, or `--add-dir`; or edit the epics, the architecture, or the workflow.
- Write a claim about plugins: the spike did not load one (AD-7).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Either mode | Model, mode | Same vector; one secret reference; `api.anthropic.com` | N/A |
| Effort | `low` to `max` | `--effort <tag>` | Undefined value: unsupported, message id |
| Schema | Generated JSON schema | `--json-schema` compact; result in `structured_output` | Not JSON: `ArgumentException` |
| Recorded stream | Tool run, subscription, schema | Events, result, usage; rate limit event kept | Unreadable lines counted, never thrown |
| No credential | Planted hook, MCP server, `env` block | None executes; `authentication_failed`, `agent-error` | N/A |
| Other failure | Non-zero exit, no result | `unknown` | N/A |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Invocations/AgentInvocation.cs` (changed), `AuthenticationMode.cs`, `SecretReference.cs`, `Support.cs`, `ResultLocation.cs`, `AgentPrompt.cs`, `AgentOutput.cs` (new) -- the contract; `Core/Failures/FailureClass.cs` (new); `Core/Ports/IAgentAdapter.cs` (changed).
- `src/Hephaestus.Agents/ClaudeCodeAgentAdapter.cs`, `ClaudeCodeStream.cs` (new) -- the adapter and its parser.
- `src/Hephaestus.Core/Localization/*` -- two message ids for unsupported modes and Efforts.
- `tests/Hephaestus.EndToEnd.Tests/Harness/ScriptedAgentAdapter.cs`, `HarnessTests.cs` -- follow the changed contract.
- `tests/Hephaestus.Agents.Tests/` (Transcripts from `docs/spikes/injection/results`, adapter, parser, planted-configuration tests); `tests/Hephaestus.Host.Tests/Architecture/AgentAdapterRulesTests.cs`.
- `docs/agent-clis.md`, `docs/security/threat-model.md`, `docs/adr/0025-*.md`, `sprint-status.yaml`.

## Tasks & Acceptance

**Execution:**
- [x] Core: contract types, `FailureClass`, port, messages; tests.
- [x] Agents: adapter, parser, classification, probe; tests on recorded transcripts tagged 2.1.332.
- [x] Planted-configuration test with the pinned CLI and a local listener; the architecture test.
- [x] Documentation; story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| Fixed record, one vector for both modes, fixed prompt | `ClaudeCodeAgentAdapterTests` |
| Hardening flags, closed tool list, user settings only, no `CLAUDE.md`, nonessential traffic off | `ClaudeCodeAgentAdapterTests`, `ClaudeCodePlantedConfigurationTests` |
| Model and Effort mapped or declared unsupported | `ClaudeCodeAgentAdapterTests` |
| Stream to events, result, usage | `ClaudeCodeStreamTests` |
| Exit classified only when named | `ClaudeCodeStreamTests` |
| Probe | `ClaudeCodeAgentAdapterTests` |
| Pure adapter | `AgentAdapterRulesTests` |
| Hosts and version of the pin | `ClaudeCodeAgentAdapterTests` |

## Implementation Notes

- Unattended run: no human gate, and the implementation was written directly from the spec rather than by a subagent, because the investigation was already in this session. `README.md` is not changed: nothing here alters how to install, run, or configure Hephaestus, because the adapter is not wired into the loop, the runtime, or the host yet (Stories 3.10, 3.11, 3.14).
- The contract changed where Story 3.3 had left it thin: `AgentInvocation` gained `SecretReferences` and `Result` (a `ResultLocation`, in place of `ResultPath`), `InvocationRequest` gained the mode, the Model, the Effort, and the result schema, and `IAgentAdapter` gained the pin, the two `Support` questions, the probe, `Parse`, and `ClassifyExit`. The scripted adapter of the end-to-end harness follows it.
- `Hephaestus.Agents` is a namespace now, so `Agents.Tags` in `AliasApi` (the Core class of that name) is written `Core.Aliases.Agents.Tags`; no other use in the host had the clash.
- The planted-configuration test proves the `env` block without a credential by pointing the planted `ANTHROPIC_BASE_URL` at a listener on the machine; the control run shows it live. It mutated to red when `--setting-sources user` was removed. The pinned executable is `HEPHAESTUS_CLAUDE_CODE` or the vendor's download checked against `deploy/agents.lock`.
- The recorded transcripts are the stdout of four runs of the injection spike (harness `bdfdba94e1fa`) and one run with no credential recorded from the pinned CLI on 2026-11-27, with the repository's path replaced by `/workspace`.
- A usage limit is not classified: no recorded stream shows one (Story 7.1). Secret names `anthropic-api-key` and `claude-code-oauth-token` are the adapter's defaults; Story 3.10 owns how the Owner chooses.

## Spec Change Log

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| Sprint status still `backlog`, tasks unchecked at review time | false | none | Set to `done` as the last step of the run; tasks are ticked. |
| "No connection" and "no vendor call" claimed more broadly than the test shows | medium | patch | Reworded in the threat model, the guide, and the ADR to "no connection to the listener"; the ADR says what is not claimed. |
| `InstructionPath` ignored; the prompt could point at a missing file | medium | patch | The adapter refuses a path other than `instruction.md`; the file itself is placed by Core's context set (Story 3.11), not by an invocation. |
| The shared environment dictionary is mutable | medium | patch | Now a `FrozenDictionary`; a test shows a write throws. |
| A stale assistant error is blamed for a later result | medium | patch | The last assistant message decides (`lastError` is assigned, not coalesced); tests for a stale error and for errors that are not named. |
| A lone surrogate in a stored line throws `ArgumentException`; a cut can end on half a pair | medium | patch | Both caught or avoided; two tests. |
| Failure classification untested for assistant errors other than `authentication_failed` | medium | patch | Theory over `rate_limit`, `billing_error`, `server_error`, `invalid_request`: kept in `Error`, class `unknown`. |
| `ResultLocation.InFile` accepts a relative path | low | patch | Refuses a path that is not absolute. |
| `ClassifyExit` doc says "ended well" for null | low | patch | Doc names exit 0 with a result that is not an error; a non-zero exit after one is `unknown`. |
| Planted test: unsynchronised counter, no settle time after exit, a missing named binary skips, `/lib` may not exist | medium | patch | `Volatile.Read`, a one-second settle, a named file that is wrong fails, `/lib` checked. |
| Docs say the tests show "settings"; one rejected alternative is garbled | low | patch | Reworded. |
| The probe omits `--model` and `--effort` | low | none | A probe asks about the credential's capacity (AD-20), which is not a property of a Model; stated in the ADR. |
| `default(Support)` has no reason; `SupportsEffort` rejects only undefined values | low | none | Only `Support.Yes` and `Support.No` are made; Claude Code has a value for every Effort of Core, so a value outside the set is all there is to refuse. |
| `TryParse` of two enums differ in trimming; unused Core members | low | none | They follow the existing `Effort` and `StepExitCause`; the callers are Stories 3.9 and 3.10. |
| `SecretReference` accepts reserved variable names and odd paths | low | none | Only adapters make references, from constants, and a test of the adapter shows which. |
| Transcripts hold session ids and are not scrubbed | low | none | The spike's results, from which four come, are committed already; the ids are the CLI's own and name no account. |
| Parsing is unbounded for a long run | low | none | The stored stream is bounded by the output limit (Story 3.3); each event's text is cut. |
| The test of the planted configuration is skipped where the binary is not reachable | low | none | It fails in GitHub Actions and the control run shows it is live where it runs; stated in the guide. |
| Block comments hide from the architecture test; `AliasApi` change unexplained | low | none | The sources use `//` and `///` only; the `AliasApi` change is in the Implementation Notes. |
| The pinned version of the CLI mounted in a container is not checked | low | none | The runtime's check is the lock's digest at start (Story 3.1); this adapter's test ties its own version to the lock. |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
