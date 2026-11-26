---
title: Sprint Change Proposal — absorbing the findings of the injection spike
project: Hephaestus
date: 2026-11-26
status: approved
approved_by: Adam
approved_on: 2026-11-26
scope: moderate
mode: batch
trigger: Story 1.1
follows: sprint-change-proposal-2026-11-25-language-and-ci.md
---

# Sprint Change Proposal — what the injection spike changes in the plan

## 1. Issue summary

**Trigger.** Story 1.1, the injection spike, closed on 2026-11-26. Its report (`docs/spikes/injection/README.md`) confirms AD-7 and AD-8 and lists fifteen findings the architecture has to absorb. The story was forbidden to touch the spine, an ADR, the threat model, or `SPEC.md`, so none is applied. The report requires them to go through this workflow before the first story that depends on one starts. The next such story is 1.2.

**Kind of change.** Technical facts discovered during implementation. No requirement is added or dropped and no approach failed.

**What the spike settled.**

| Decision | Verdict |
| --- | --- |
| AD-8, CLIs pinned and injected | Confirmed. The fallback "Hephaestus base image" is not needed. |
| AD-7, container as the boundary behind an egress proxy | Confirmed. |
| Claude Code, API key and subscription sign-in | Supported. Subscription uses a token from `claude setup-token`; the policy risk of FR-25 is unchanged. |
| Codex CLI, API key | Supported. |
| Codex CLI, subscription sign-in | Unsupported, now for two reasons instead of one. |

**Decisions the Owner made.**

| Date | Question | Decision |
| --- | --- | --- |
| 2026-11-26, closing Story 1.1 | Do a repository's instruction files and skills count as executable configuration? | No. They are prompt content. AD-7 covers hooks, MCP servers, settings, and plugins. |
| 2026-11-26, closing Story 1.1 | "Supported" rests on one-line prompts with at most one tool call. | Accepted. |
| 2026-11-26, closing Story 1.1 | No run loaded a repository plugin, so suppressing one is unproven. | Accepted as an open item. |
| 2026-11-26, this workflow | Finding 3: who supplies the CA bundle Codex needs? | Hephaestus, in the agent binaries volume. The Project image contract does not grow. |
| 2026-11-26, this workflow | Finding 12: does an Agent read the repository's `CLAUDE.md` or `AGENTS.md`? | Neither Agent does. Codex gets `project_doc_max_bytes=0`. |

**Evidence that the plan no longer matches the facts.**

- AD-7, AD-8, ADR-0008, ADR-0011, `docs/architecture.md`, `SPEC.md`, and `requirements.md` still call both decisions conditional on a spike that has now run.
- AD-8 and Story 3.1 pin "one version and checksum of each Agent CLI per platform". Claude Code needs two builds per architecture, Codex is two executables, and an archive's digest cannot verify the binary extracted from it.
- AD-8's contract "Linux, glibc or musl, `bash`" is not enough for Codex on `debian:13-slim` or `ubuntu:24.04`: with no CA bundle every request fails with `UnknownIssuer`.
- AD-7 says the runtime "sets nothing specific to a language or build tool; such proxy settings belong to the Project image". Maven and Gradle name the proxy by host and port, which an image can only carry if that address never changes. Nothing fixes it.
- AD-7 obliges every invocation to switch off repository plugins and makes a mode unsupported when that "cannot be guaranteed". Read literally, with plugin suppression unproven, no mode is supported.
- AD-10 says "the adapter selects the invocation by the Agent's authentication mode". One Claude Code invocation serves both.
- Story 1.2, AR-5, and the note on FR-63 ask whether Claude Code's `--bare` mode must name a skill explicitly. The hardened invocation is `--safe-mode`, offers no `Skill` tool at all, and reads no repository instruction file.
- Spine Open Question 3 and AR-8 call the Codex effort override unconfirmed. It is confirmed.
- Threat T5 is rated "Unverified" pending this spike. T14 does not name `--setting-sources user`, without which `--bare` and `--safe-mode` still apply the `env` block of the repository's settings.

## 2. Impact analysis

### 2.1 Checklist record

| Item | Status | Finding |
| --- | --- | --- |
| 1.1 Triggering story | Done | Story 1.1, injection spike, `done`. |
| 1.2 Core problem | Done | Technical facts discovered during implementation; fifteen findings not yet in the plan. |
| 1.3 Evidence | Done | The report and its 116 result files, one harness version (`bdfdba94e1fa`); section 1 above. |
| 2.1 Current epic | Done | Epic 1 completes as planned. Story 1.2 is amended before it starts. |
| 2.2 Epic-level changes | Done | None. No epic added, removed, or redefined. |
| 2.3 Remaining epics | Done | Epic 3: eleven stories amended. Epics 7 and 9: one story each gains a starting fact. Epic 5 inherits the outcome of Story 1.2. |
| 2.4 Obsolete or new epics | N/A | None. |
| 2.5 Order and priority | Done | Unchanged. The proof-of-concept path stays at 30 stories. |
| 3.1 PRD | Action needed | Notes on FR-5 and FR-25; one superseding line in the addendum. No requirement changes. |
| 3.2 Architecture | Action needed | AD-6, AD-7, AD-8, AD-10, AD-12, AD-18; four conventions; the Stack table; Deferred; Open Questions. ADR-0008 and ADR-0011, the threat model, and `docs/architecture.md` rendered again. |
| 3.3 UX | N/A | `agent set-auth` already refuses an unsupported mode with the adapter's reason (Story 3.10). No screen, command, or text changes. |
| 3.4 Other artifacts | Action needed | `SPEC.md`, `requirements.md`, and `release-scope.md` re-derived. `README.md` unaffected: it describes nothing that changes. |
| 4.1 Direct adjustment | Viable | Effort medium, risk low. |
| 4.2 Rollback | Not viable | Nothing is built on the old text. The spike's harness stays as the reference. |
| 4.3 MVP review | Not needed | Scope is unchanged. The fallback path disappears, which shrinks Story 3.1. |
| 4.4 Selected path | Done | Direct adjustment. |
| 5.1 to 5.5 Proposal | Done | This document. |
| 6.1, 6.2 Review | Done | Every finding has a disposition in 2.2. |
| 6.3 Owner approval | Done | Approved by the Owner on 2026-11-26, with the four recommendations of section 3. |
| 6.4 `sprint-status.yaml` | N/A | No epic or story is added, removed, or renumbered. |
| 6.5 Handoff | Done | Section 5. |

### 2.2 Where each finding lands

| # | Finding | Disposition | Documents | Stories |
| --- | --- | --- | --- | --- |
| 1 | `--bare` or `--safe-mode` alone leaves the `env` block live | Rule: an invocation takes Agent settings only from the throwaway `HOME`. The flag is named in ADR-0008 and T14, not in the spine, because vendor flags live in the adapter (AD-10). | AD-7, ADR-0008, T14 | 3.8, 3.21 |
| 2 | One Claude Code invocation serves both modes | AD-10 stops requiring a choice of invocation by mode. | AD-10 | 3.8, 3.10 |
| 3 | Codex needs a CA bundle | Owner's decision: Hephaestus places a bundle in the agent binaries volume, copied at startup from the `hephaestus` image's own certificate store, so it is refreshed by every release. | AD-8, AD-10, layout convention, ADR-0011 | 3.1, 3.9 |
| 4 | Two Claude Code builds per architecture | The lock pins both. The runtime establishes the image's C library at Task start and mounts the matching executables, so adapters stay unaware of it. | AD-8 | 3.1, 3.2 |
| 5 | Codex is two pinned files | The lock pins `codex-code-mode-host`. Startup stops unless every pinned executable is present and verified. | AD-8, Stack | 3.1, 3.9 |
| 6 | Pin each executable, not only each download | The lock holds a digest per executable, and per archive where the vendor publishes one. | AD-8, T29 | 3.1 |
| 7 | Task volumes must be mounted `volume-nocopy` | Added to the Workspace rule and the layout convention. | AD-6, ADR-0008 | 3.2, 3.5 |
| 8 | The proxy's host name and port are a public contract | Rule now: they are fixed, documented, and versioned public surface. The values are chosen in Story 3.4, with the proxy. | AD-7, Public surface, Deferred | 3.4, 3.21 |
| 9 | Hosts | Adapters declare the hosts observed for the pin. Documentation says Codex contacts `chatgpt.com` with an API key, that an allow-list needs the registry's hosts, and that `dotnet restore` fetches a revocation list over plain HTTP. | AD-10 | 3.4, 3.9, 3.10, 3.21 |
| 10 | Neither CLI stops soon when the network is gone | Evidence for AD-18: the runtime's deadline is the only stop. One test case added. | AD-18 | 3.16 |
| 11 | Codex models differ by authentication mode | Recorded under Open Question 3. No story changes while Codex is API key only. | Open Questions | none |
| 12 | The CLIs treat repository instructions and skills differently | Owner's decision: neither reads the repository's instruction file. No step depends on a CLI's skill or sub-agent mechanism. Residual, stated in T2: Codex offers repository skills whatever is switched off. | AD-12, T2, §7 | 1.2, 3.9, 3.11 |
| 13 | Repository plugins are unproven | AD-7 keeps the obligation and defines "guaranteed" as shown by the planted-configuration test. A test that first gets a plugin to load is owed before any claim. | AD-7, T5, T14, §8, Open Questions | 3.21 |
| 14 | The fallback has a licence limit | Recorded in ADR-0011. The fallback is removed from AD-8 and Story 3.1. | AD-8, ADR-0011 | 3.1 |
| 15 | Rate-limit events and cost reporting | Starting facts for the Usage Limit spike and the spend limit. | Deferred | 7.1, 9.4 |

Also absorbed, from the report's "did not cover" list: the spike ran on linux/amd64 under Docker Desktop only, so Story 3.1 gains a start check on linux/arm64, and the threat model states the limit.

### 2.3 Story impact

| Story | Change |
| --- | --- |
| 1.2 BMAD spike | Runs under the hardened invocations exactly. The `--bare` question is replaced by how a workflow starts with no `Skill` tool and no repository instruction file. Two criteria added. |
| 3.1 Pin, fetch, verify | The lock's content, the CA bundle, the completeness check, signature verification when a pin changes, an arm64 start check. The fallback criterion is replaced. |
| 3.2 Hardened container | `volume-nocopy`, C library selection, `HOME` as a tmpfs. |
| 3.4 Egress proxy | A fixed, documented proxy address; plain HTTP through the proxy. |
| 3.5 Fresh checkout | The checkout leaves `/workspace` writable by the step user. |
| 3.8 Claude Code adapter | Settings from the throwaway `HOME` only; one argument vector for both modes; a closed tool list; a credential-free test of start-time configuration. |
| 3.9 Codex adapter | The CA bundle variable, `project_doc_max_bytes=0`, the hosts, the effort mechanism, the reasons subscription is unsupported, cost reported as null. |
| 3.10 Agent sign-in | The Claude Code subscription credential is a `setup-token` token. Two documentation statements. |
| 3.11 Same instructions | Templates rely on no CLI skill, sub-agent, slash command, or repository instruction file. |
| 3.16 Failure classes | One test case: a CLI that keeps waiting for a lost network. |
| 3.21 Security statement | No claim about plugins without a test; the residual on Codex skills; each documented proxy setting was run; registry hosts. |
| 7.1 Usage Limit spike | Starts from the `rate_limit_event` seen on every Claude Code subscription run. |
| 9.4 Spend limit | Codex reports tokens and no cost. |

Totals: 93 stories stay 93. Thirteen are amended; none is added, removed, or moved. The proof-of-concept path is unchanged.

### 2.4 Technical impact

- **Nothing is rolled back.** No application code exists. The spike's harness under `docs/spikes/injection/scripts/` stays, and its `agents.lock` and hardened invocations are the starting point for Stories 3.1, 3.8, and 3.9.
- **A pin change needs the Owner.** The planted-configuration run needs real credentials. Changing a pin in `deploy/agents.lock` therefore cannot be closed by a dev agent alone, like Stories 1.1 and 7.1.
- **Purity of adapters holds.** The C library is the runtime's concern (it mounts the matching executables); the CA bundle is a path under `/hephaestus/agents` that an invocation names. No adapter learns about Docker and the runtime learns nothing about Agents.
- **BMAD support carries more risk than before.** BMAD workflows are installed as skills. Hardened Claude Code has no `Skill` tool, so Story 1.2 must show the workflows complete when the Agent is pointed at their files. If they do not, the consequence for FR-63 is raised there, as that story already requires.

## 3. Recommended approach

**Direct adjustment.** Every finding fits an existing decision or story. The two decisions the spike conditioned are confirmed, so the cost is in precision, not in direction.

Four points below go beyond what the report states. They were recommendations; the Owner approved them with this proposal on 2026-11-26.

1. **Verify vendor signatures when a pin changes, not at startup.** The report says the startup fetch should verify Anthropic's signed manifest and the Codex release's sigstore bundle. Once the lock pins a SHA-256 per executable, that digest is the authority at startup and a signature adds nothing there, while it would put GPG and sigstore tooling into the image that holds the Docker socket. The signature matters at the moment a digest enters the lock. A check in Hephaestus's own repository on any pull request that changes `deploy/agents.lock` covers that moment.
2. **Fix the rule about the proxy address now, the values in Story 3.4.** Which proxy is used is still deferred to that story, and the name belongs with that choice. What cannot wait is that the address is one value, documented, and versioned.
3. **Let the runtime select the Claude Code build.** The alternative, the adapter choosing a path by C library, would make an invocation depend on a property of the image. Mounting the matching set at one path keeps AD-10 intact.
4. **Start each pinned executable once on linux/arm64.** Hephaestus publishes arm64 images, and the spike ran none. A `--version` run in a hardened container on an arm64 runner needs no credential.

**Alternatives considered.** Adding "CA certificates present" to the Project image contract was rejected by the Owner: stock `debian:13-slim` and `ubuntu:24.04` would stop working with Codex. Leaving `AGENTS.md` readable by Codex was rejected by the Owner: the two Agents would work from different instructions (FR-62), and a Task branch could rewrite them. Delivering the Base Branch's instruction file to both Agents through the context files would be a new requirement and is not proposed.

**Cost.** Effort medium: three planning documents, the rendered `docs/`, thirteen stories. Risk low. No timeline effect: the proof-of-concept path is not reordered, and Story 1.3, the next story on it, is untouched.

## 4. Detailed change proposals

### 4.1 PRD (`prd.md`, `addendum.md`) — through `bmad-prd`

No requirement, consequence, or scope statement changes.

**FR-5 — add a note**

> **Notes:** The execution environment needs only Linux with glibc or musl and `bash`; Hephaestus supplies the Agent CLIs and the certificates they need. A build tool that does not honor the standard proxy variables (Maven, Gradle) needs its proxy settings supplied by the Project. Installation documentation lists them.

**FR-25 — replace the note**

OLD:

> **Notes:** The architecture supports Codex CLI with an API key only until its subscription credential can be refreshed without taking a file back from a Workspace. See the architecture spine, Open Question 3.

NEW:

> **Notes:** Claude Code is supported with an API key and with a subscription sign-in, the latter through a token from the vendor's `claude setup-token`. Codex CLI is supported with an API key only: its subscription credential is a file holding a full account credential that must be kept and refreshed between runs, and the tools the account brings cannot be listed from the CLI. See the architecture spine, Open Question 3, and the injection spike report.

**Addendum — add one line under the "Security finding" entry**

> Superseded by the injection spike (2026-11-26): `--bare` and `--safe-mode` both still apply the `env` block of the repository's settings; `--safe-mode --setting-sources user` leaves nothing and works in both authentication modes. See `docs/spikes/injection/README.md`.

### 4.2 Architecture spine (`ARCHITECTURE-SPINE.md`) — through `bmad-architecture`

The texts below fix content. `bmad-architecture` may tighten wording.

**AD-6 — add after "Workspaces live only in named volumes, never in host bind mounts."**

> A Task volume is mounted with Docker's copy-from-image switched off (`volume-nocopy`) in every container that mounts it, so that what an image holds at `/workspace` can neither enter the volume nor change the owner of its root.

Rationale: finding 7. Without it the step user cannot write to its own workspace.

**AD-7 — four changes**

1. Proxy bullet. Remove "Conditional on the injection spike (Open Questions 1)."
2. Proxy-variables bullet. Add:

   > The proxy is reached from a step container at one fixed host name and port. They are documented and part of the versioned public surface, because a Project that builds with a tool needing its own proxy settings writes them down.

3. Last bullet, OLD:

   > Every invocation disables tools that execute outside the step container under the vendor account's authority (account-linked connectors, remote MCP servers, cloud task delegation) and switches off repository-supplied executable Agent configuration (hooks, MCP servers, settings, plugins). An adapter that cannot guarantee this for an authentication mode declares that mode unsupported.

   NEW:

   > Every invocation disables tools that execute outside the step container under the vendor account's authority (account-linked connectors, remote MCP servers, cloud task delegation) and switches off repository-supplied executable Agent configuration (hooks, MCP servers, settings, plugins). An invocation takes Agent settings only from the throwaway `HOME`, which Hephaestus controls, and from nothing under `/workspace`. The tools an invocation offers the Agent are a closed list fixed by the adapter. "Guaranteed" means shown by the planted-configuration test for the pinned CLI version and the authentication mode; an adapter that cannot show it declares that mode unsupported, and a change of pin, of a switch, or of the tool list repeats the test. For repository plugins the test is owed: no pinned version loaded one from a repository's settings, so nothing shows a switch stops one, and no claim is made about them until it does. Instruction files and skills in a repository are prompt content, not executable configuration (T2).

4. Add a closing line: "Confirmed by the injection spike (Story 1.1, 2026-11-26)."

Rationale: findings 1, 8, 12, 13. The third change also removes the contradiction between an unproven plugin switch and "a mode that cannot be guaranteed is unsupported".

**AD-8 — replace the rule**

OLD:

> Each Hephaestus release pins one version and checksum of each Agent CLI per platform in `deploy/agents.lock`. Hephaestus images contain no Agent binary: at startup the orchestrator downloads the pinned binaries from the vendor's official channel, verifies them, and fills the agent binaries volume, which is mounted read-only into the image the Project Configuration names. The Project image contract is: Linux, glibc or musl, `bash` present. The configuration snapshot records the image digest resolved at Task start and every step of the Task runs that digest. CLI self-update is disabled in step containers. An adapter runs only against the CLI version it was released with. Conditional on the injection spike (Open Questions 1); the fallback keeps the same rule with a Hephaestus base image that Project images derive from.

NEW:

> Each Hephaestus release pins, in `deploy/agents.lock`, one version of each Agent CLI and the SHA-256 of every executable it consists of, per architecture and, where the vendor builds per C library, per C library. Claude Code is one executable in a glibc and a musl build. Codex CLI is two statically linked executables, `codex` and `codex-code-mode-host`. Where the vendor publishes an archive, the lock pins the archive and each executable extracted from it. Hephaestus images contain no Agent binary: at startup the orchestrator downloads the pinned files from the vendor's official channel, verifies each against the lock, and fills the agent binaries volume; startup stops unless every pinned executable is present and verified. The vendor's signature over a release is verified when a pin changes, by a check in Hephaestus's own repository, not at startup. The orchestrator also places in that volume a CA certificate bundle copied from the `hephaestus` image's own certificate store, for a CLI that needs one. The volume is mounted read-only into the image the Project Configuration names. The Project image contract is: Linux, glibc or musl, `bash` present; nothing else is required of it. The runtime establishes the image's C library when it resolves the image digest at Task start, records both in the configuration snapshot, and mounts the matching executables at `/hephaestus/agents`, so an invocation's paths do not depend on the C library (AD-10). Every step of the Task runs that digest. CLI self-update is disabled in step containers. An adapter runs only against the CLI version it was released with. Confirmed by the injection spike (Story 1.1, 2026-11-26). No Hephaestus base image exists: one that contained Claude Code could not be published (ADR-0011).

Rationale: findings 3, 4, 5, 6, 14, and recommendations 1 and 3.

**AD-10 — two changes**

1. OLD: "The adapter selects the invocation by the Agent's authentication mode."

   NEW: "The adapter describes the invocation for the Agent's authentication mode. Where the CLI allows it, the argument vector is the same in every mode and only the secret reference and the declared hosts differ."

2. Add:

   > A file an invocation needs from Hephaestus, such as the CA bundle, is named by its path under `/hephaestus/agents`. The egress hosts an adapter declares are those observed for the pinned version; they are a minimum, and a missing one shows up as a host the proxy refused (AD-18).

Rationale: findings 2, 3, 9.

**AD-12 — add**

> A repository's own Agent instruction file (`CLAUDE.md`, `AGENTS.md`) is not given to an Agent: each adapter switches its CLI's reading of it off. A Project instructs Agents through its Methodology (AD-14). No step depends on a CLI's own skill, sub-agent, or slash-command mechanism; a template instructs through files the Agent reads with the tools the invocation offers.

Rationale: finding 12, Owner's decision of 2026-11-26. It keeps FR-62 true and keeps a Task branch from rewriting its own instructions.

**AD-18 — add**

> No behavior depends on a CLI ending by itself: with the network gone, neither pinned CLI stopped within a minute.

Rationale: finding 10.

**Consistency Conventions**

| Row | Change |
| --- | --- |
| Step container layout | Add: the CA bundle at `/hephaestus/agents/certs/ca-certificates.crt`; `HOME` is a tmpfs; the Task volume is mounted `volume-nocopy`. |
| Public surface | Add to the versioned list: the egress proxy's host name and port as seen from a step container. |
| Tests | Add: the matrix and planted-configuration scripts of `docs/spikes/injection` are repeated, with the Owner's credentials, whenever a pin in `deploy/agents.lock` changes; they fail by themselves when a claim no longer holds. What executes when a CLI starts is also covered by a test that needs no credential. |

**Stack.** The Codex CLI row becomes "0.172.0 at authoring, with its `codex-code-mode-host`; `deploy/agents.lock` is the authority".

**Structural Seed.** Source tree: `agents.lock` is described as "pinned Agent CLI versions and a checksum per executable". Deployment, Startup: "fetch and verify the pinned Agent executables and place the CA bundle". Deployment, Egress proxy: add "reached at one fixed, documented host name and port".

**Deferred**

| Row | Change |
| --- | --- |
| Which egress proxy is used… | Add to the decision: "and the fixed host name and port at which step containers reach it (public surface, AD-7)". |
| Usage Limit detection signals… | Add: "Starting fact from the injection spike: every Claude Code run under subscription sign-in emits a `rate_limit_event` with `status`, `resetsAt`, and `rateLimitType`; runs with an API key emit none." |
| Spend limit accounting… | Add: "Starting fact: Claude Code reports a cost per run; Codex CLI reports token counts and no cost." |

**Open Questions**

- **1, replace.** "Injection spike: answered. AD-7 and AD-8 are confirmed (`docs/spikes/injection/README.md`, 2026-11-26). Left open by it: whether the hardened invocations stop a repository plugin, which needs a test that first gets one to load (Story 3.21); arm64, a native Linux Docker Engine, and rootless Docker, none of which was run; runs longer than one tool call; what `ab.chatgpt.com` is for and whether Codex works when it is refused."
- **3, replace.** "Codex subscription sign-in. Unsupported. The credential file is a full account credential that must be kept and refreshed between runs, which AD-16 forbids taking from a step container, and the tools the account brings cannot be listed from the CLI. Should it ever be supported, the Models Codex accepts differ by authentication mode, so an Alias could not resolve to one Model for both. The per-run reasoning-effort override is confirmed: `model_reasoning_effort`."

### 4.3 Rendered documents (`docs/`) — rendered again by `bmad-architecture`

**ADR-0011**

- Status becomes "Accepted; confirmed by the injection spike on 2026-11-26".
- Decision follows the new AD-8.
- "Kept as the fallback if the spike fails" becomes "Not needed. A base image containing Claude Code could not be published by this project: the vendor grants no licence to redistribute."
- Consequences gain: a pin change repeats the credentialed run and needs the Owner; Codex contacts `chatgpt.com` even with an API key.
- "What the spike must show" becomes "What the spike showed", with a link to the report.

**ADR-0008**

- Status drops "conditional on the injection spike".
- "Outside the container" names the Claude Code switches that held: `--safe-mode` with `--setting-sources user`, `--strict-mcp-config`, and the settings that disable hooks and account connectors; and states that `--bare` or `--safe-mode` alone still applies the `env` block of the repository's settings. It names Codex's untrusted project, ignored user configuration, and disabled features.
- Decision gains `volume-nocopy` and the fixed proxy address.
- Consequences: "which the injection spike must show" becomes what it showed, per package manager: npm, `dotnet restore`, pip, cargo, and go work with the standard variables; Gradle needs the proxy as JVM system properties; Maven needs a `<proxy>` element in its settings.

**Threat model**

| Place | Change |
| --- | --- |
| Status line | Add: "except what the injection spike recorded on 2026-11-25 for the pinned CLI versions". |
| T2 | Mitigation gains: neither Agent is given the repository's instruction file (AD-12). Residual gains: Codex CLI offers a repository's skills to the model whatever is switched off; whether the model acts on one depends on the model. |
| T5 | Residual, replacing "Unverified": shown for the pinned versions in Claude Code's two modes and Codex's API key mode, where no planted hook, MCP server, or setting executed and no account connector was listed. Not shown: a repository plugin; an account known to have connectors attached; Codex's tools, which rest on the model's own report. Codex subscription sign-in is unsupported. |
| T14 | Mitigation names the flag: for Claude Code, `--setting-sources user` in addition to `--safe-mode`, because either `--bare` or `--safe-mode` alone still applies an `env` block that could redirect where requests, and the credential with them, are sent. |
| T15 | Add the second reason Codex subscription sign-in is unsupported: the file is a full account credential. |
| T29 | "a version and checksum per platform" becomes "a checksum per executable, architecture, and C library; the vendor's signature is verified when a pin changes". |
| §5 | Last paragraph becomes: Claude Code supports both modes, the subscription mode with a `setup-token` token the vendor documents as limited to model requests; Codex is supported with an API key only (T5, T15). |
| §7 | Add two limitations: repository instructions and skills are prompt content and are contained, not prevented; the injection result was shown on linux/amd64 under Docker Desktop with short runs. |
| §8 | Last row becomes: shown once by the injection spike for the pinned versions; repeated against each new pin and authentication mode; owed, a case in which a repository plugin loads. |
| §9 | Question 1 is answered. Question 4 gains the second reason. New question: can a repository get a plugin loaded, and do the invocations stop it? |

**`docs/architecture.md`**

- Section 7, "Agent programs are supplied by Hephaestus": remove "This depends on a spike that has not been run yet."; add the two Claude Code builds, the two Codex executables, and the CA bundle in one sentence.
- Section 7, "One way out of the network": say that the proxy's address is fixed and documented.
- Section 12: "Spikes that gate v0.1" names the BMAD spike only. "Codex with a subscription sign-in" gains the second reason.

### 4.4 Spec (`_bmad-output/specs/spec-Hephaestus/`) — re-derived by `bmad-spec`

| File | Change |
| --- | --- |
| `requirements.md` | FR-5 and FR-25 notes follow 4.1. FR-63 note about `--bare`: replaced by "hardened Claude Code offers no `Skill` tool and neither Agent reads a repository instruction file, so a BMAD workflow is started from its files; to verify in the BMAD spike". NFR-4 note: drop "conditional on the injection spike"; add that the proxy address is fixed and public. |
| `SPEC.md` | Open Questions: remove the injection spike; rewrite "Codex subscription sign-in" as a statement that it is unsupported, with the effort override confirmed. |
| `release-scope.md` | Ordering: the injection spike is done and confirmed AD-7 and AD-8. |

### 4.5 UX

No change.

### 4.6 Epics and stories (`epics.md`) — through `bmad-create-epics-and-stories`

**Overview.** Add a note naming this proposal as the third approved change: no story added, thirteen amended.

**Requirements inventory**

| Item | Change |
| --- | --- |
| AR-4 | Append: "Done, 2026-11-26: both decisions confirmed; the fallback is not needed." |
| AR-5 | Replace "and whether Claude Code's `--bare` mode needs the skill named explicitly" with "and how a workflow is started when the Agent has no `Skill` tool and is given no repository instruction file". |
| AR-8 | Replace with Open Question 3 as rewritten in 4.2. |
| AR-15, AR-16, AR-17, AR-19, AR-21, AR-27 | Follow AD-6, AD-7, AD-8, AD-10, AD-12, and AD-18 as changed in 4.2. |
| AR-34 | Follow the layout convention. |
| AR-40 | "fetch and verify the pinned Agent executables and place the CA bundle". |
| AR-56 | "Shown by the injection spike for the pinned versions; repeated with the Owner's credentials whenever a pin changes; a repository plugin that loads is still owed." |
| AR-60 | Add: Gradle's JVM properties and Maven's `<proxy>` element, with the proxy's fixed address. |
| AR-67 | Add: the proxy's fixed host name and port. |
| AR-70 | Add the two starting facts of finding 15. |

**Story 1.2 — amend**

OLD, in the first criterion: "in the container setup proven by Story 1.1".

NEW: "in the container setup and with the hardened invocations Story 1.1 confirmed, Codex with `project_doc_max_bytes=0` added".

OLD: "**And** it states whether a Claude Code run in API-key mode must name the skill explicitly and how the skills directory is made visible to it"

NEW: "**And** it states how a workflow is started when the Agent has no `Skill` and no `Task` tool and is given no repository instruction file, and whether any workflow needs a tool beyond `Bash`, `Read`, `Edit`, and `Write`"

Add:

> **Given** a workflow that completes only with a tool or a setting the hardened invocation switches off **When** the spike closes **Then** the report names it, and it is raised with the Owner as a change to the invocation, which repeats the planted-configuration run of Story 1.1 before it is adopted (AD-7)

> **Given** Codex CLI, which offers a repository's skills to the model whatever is switched off **When** a workflow whose files exist both at `/hephaestus/methodology` and in `/workspace` is run **Then** the report states which copy each Agent followed (AD-14)

**Story 3.1 — amend**

OLD: "**Then** it pins one version and one checksum of the Claude Code CLI and of the Codex CLI for each supported platform and C library (AD-8)"

NEW: "**Then** it pins one version of each CLI and the SHA-256 of every executable: Claude Code's glibc and musl builds per architecture, and Codex CLI's `codex` and `codex-code-mode-host` per architecture; where the vendor publishes an archive, the archive's digest and each extracted executable's (AD-8)"

OLD, the last criterion: "**Then** it follows the path that story confirmed: injection through the volume, or the fallback of a Hephaestus base image that Project images derive from"

NEW: "**Then** it follows injection through the volume, which that story confirmed, starting from `docs/spikes/injection/scripts/agents.lock`; no Hephaestus base image is built"

Add:

> **Given** a start **When** the agent binaries volume has been filled **Then** startup stops, naming the file, unless every executable the lock names is present and matches its digest

> **Given** a start **When** the volume is prepared **Then** it holds a CA certificate bundle at `certs/ca-certificates.crt`, copied from the `hephaestus` image's own certificate store and replaced at every start

> **Given** a pull request that changes `deploy/agents.lock` **When** GitHub Actions runs **Then** the build fails unless each new digest matches what the vendor publishes and the vendor's signature over that release verifies

> **Given** the pinned executables for linux/arm64 **When** each is started in a hardened container on an arm64 runner **Then** it prints its version; Story 1.1 ran linux/amd64 only

> **Given** the documentation for contributors **When** it describes changing a pin **Then** it says that the matrix and planted-configuration runs of `docs/spikes/injection` are repeated with the Owner's credentials, and that such a change is not closed by a dev agent alone

**Story 3.2 — amend**

OLD: "and a temporary `HOME` at `/hephaestus/home` (AD-7)"

NEW: "and a temporary `HOME`, a tmpfs, at `/hephaestus/home` (AD-7)"

Add:

> **Given** a Task volume **When** any container mounts it **Then** it is mounted with `volume-nocopy`, and the step user can create a file in `/workspace` on a glibc image and on a musl image (AD-6)

> **Given** a Task that starts **When** its image digest is resolved **Then** the runtime establishes whether the image uses glibc or musl, records it in the configuration snapshot, and mounts the matching executables at `/hephaestus/agents` in every step container of the Task (AD-8)

> **Given** an image whose C library cannot be established **When** the Task would start **Then** it does not start and the Owner is told why

**Story 3.4 — amend**

OLD: "**Then** a decision record names the proxy chosen, how it is started, how each step run's hosts reach it, and how step containers are kept apart; the proxy is free and open-source software (AR-67, NFR-15)"

NEW: "**Then** a decision record names the proxy chosen, how it is started, how each step run's hosts reach it, how step containers are kept apart, and the one host name and port at which step containers reach it; the proxy is free and open-source software (AR-67, NFR-15)

**And** that host name and port are documented and listed in the versioned public surface (AD-7)"

Add:

> **Given** a request over plain HTTP to an allowed host **When** it goes through the proxy **Then** it is passed; `dotnet restore` fetches a certificate revocation list that way

**Story 3.5 — add**

> **Given** a checkout that has run **When** the step container starts as user `10001:10001` **Then** it can write to every path the checkout created; the checkout container mounts the Task volume with `volume-nocopy` like every other (AD-6)

**Story 3.8 — amend**

OLD: "**Given** a step and an authentication mode **When** the Claude Code adapter describes the invocation"

NEW, appended to that criterion: "**And** the argument vector is the same for an API key and for a subscription sign-in; only the secret reference differs (AD-10)"

Add:

> **Given** any invocation **When** it is inspected **Then** the CLI takes settings only from the throwaway `HOME`, is offered a closed list of tools that holds no `Skill` and no `Task` tool, does not read the repository's `CLAUDE.md`, and has traffic the vendor calls nonessential switched off (AD-7, AD-12)

> **Given** a repository that plants a session-start hook, an MCP server, and an `env` block in its Claude Code settings **When** the CLI is started with the adapter's invocation and no credential **Then** none of them takes effect; this test needs no vendor call, and what executes only around a tool call stays with the credentialed run of AR-56

> **Given** the adapter **When** it declares its egress hosts **Then** they are the hosts recorded for the pinned version, `api.anthropic.com` for both modes at 2.1.332

**Story 3.9 — amend**

OLD: "**Then** it supports an API key and declares subscription sign-in unsupported, with the reason (AR-8)"

NEW: "**Then** it supports an API key and declares subscription sign-in unsupported, with the reasons: the credential file is a full account credential that must be kept and refreshed between runs, and the tools the account brings cannot be listed (AR-8)"

OLD: "**Then** it uses the per-run mechanism Story 1.1 confirmed, or declares Effort unsupported for this Agent, which is reported and not imitated (FR-24)"

NEW: "**Then** it sets `model_reasoning_effort` for the run, the mechanism Story 1.1 confirmed; a Core Effort with no Codex value is declared unsupported, reported, and not imitated (FR-24)"

Add:

> **Given** any invocation **When** it is inspected **Then** it points `CODEX_CA_CERTIFICATE` at the bundle under `/hephaestus/agents`, keeps the repository's `AGENTS.md` from the Agent, and declares the hosts recorded for the pinned version: `api.openai.com`, `chatgpt.com`, and `ab.chatgpt.com` at 0.172.0 (AD-8, AD-10, AD-12)

> **Given** a stored output stream **When** usage is normalized **Then** token counts are taken from the stream and cost is null, because Codex CLI reports none

> **Given** a repository that plants a hook and an MCP server in its Codex configuration **When** the CLI is started with the adapter's invocation and no credential **Then** neither takes effect

**Story 3.10 — amend**

OLD, in the documentation criterion: "and an API key with a spend cap at the vendor is named as the conservative choice (AR-58)"

NEW: "and an API key with a spend cap at the vendor is named as the conservative choice (AR-58)

**And** it says that Claude Code's subscription credential is the token `claude setup-token` prints, that Codex CLI is supported with an API key only and why, and that Codex CLI contacts `chatgpt.com` even with an API key"

**Story 3.11 — add**

> **Given** the `default` templates **When** they are read **Then** none relies on a CLI's skill, sub-agent, or slash command, or on an instruction file in the repository, because both Agents run without them (AD-12)

**Story 3.16 — amend**

OLD: "**Then** it covers the deadline on an Agent run, the deadline on a Validation command, the step retry bound, and the Task duration limit (AR-55)"

NEW: "**Then** it covers the deadline on an Agent run, including a CLI that keeps waiting for a network that is gone, the deadline on a Validation command, the step retry bound, and the Task duration limit (AR-55)"

**Story 3.21 — amend**

OLD: "**And** it lists the proxy settings the common ecosystems need in a Project image, as Story 1.1 found them, and says that the Marker Label, not a state label, is the control (AR-60)"

NEW: "**And** it lists the proxy settings the common ecosystems need, as Story 1.1 found them, with the proxy's fixed host name and port, and each setting listed was run behind the proxy in the place the documentation names; Story 1.1 ran Maven's from `HOME`, which a step container replaces

**And** it says which registry hosts a Project that sets an allow-list must include, and that the Marker Label, not a state label, is the control (AR-60)"

Add:

> **Given** repository plugins, which no run of Story 1.1 got to load **When** the security statement is written **Then** it makes no claim about them unless a test first gets a repository plugin to load with nothing switched off and then shows the adapters' invocations stop it; without that test it says plainly that this is not shown (T5, T14)

> **Given** the security documentation **When** it describes what a repository can do to an Agent **Then** it says that instruction files and skills are prompt content, that neither Agent is given the repository's instruction file, and that Codex CLI still offers a repository's skills to the model (T2)

**Story 7.1 — add**

> **Given** the `rate_limit_event` that Story 1.1 saw on every Claude Code run under subscription sign-in, with `status`, `resetsAt`, and `rateLimitType` **When** the spike examines it **Then** the report states how its fields behave before and at a real limit, and whether it tells remaining capacity without a further model call

**Story 9.4 — add**

> **Given** Codex CLI, which reports token counts and no cost **When** a spend limit is set for it **Then** the story fixes how its spend is counted, and a price is never assumed silently; where no count is possible, Hephaestus says the limit cannot be enforced

**Proof-of-concept path.** No change.

## 5. Implementation handoff

**Scope: moderate.** No new requirement, decision, or story. Six decisions refined, thirteen stories amended.

| Order | Skill | Responsibility |
| --- | --- | --- |
| 1 | `bmad-prd` (update) | Section 4.1 |
| 2 | `bmad-architecture` (update) | Sections 4.2 and 4.3; render `docs/` again |
| 3 | `bmad-spec` (update) | Section 4.4 |
| 4 | `bmad-create-epics-and-stories` | Section 4.6 |

`bmad-ux` and `bmad-sprint-planning` have nothing to do: no UX text changes and `sprint-status.yaml` keeps its 93 keys.

**Order of work after that.** Story 1.2 may start once step 4 is done. Story 1.3 does not depend on this proposal and may start at once.

**Owner actions outside the documents.** Delete `docs/spikes/injection/scripts/.env`, and revoke the keys and the `setup-token` token if they were created for the spike.

**Success criteria.**

- No planning document, ADR, or threat model row calls AD-7 or AD-8 conditional.
- A reader of the spine alone can write `deploy/agents.lock`: which files, which digests, which C libraries.
- No story still refers to `--bare`, to the base image fallback, or to an unconfirmed effort override.
- The threat model makes no claim about repository plugins and says which test is owed.
- Story 1.2 tests BMAD workflows under the invocations that held, not under a setup that offers the `Skill` tool.
