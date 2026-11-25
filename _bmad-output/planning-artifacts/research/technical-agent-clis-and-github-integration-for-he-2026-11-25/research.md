---
title: 'Technical research: agent CLIs and GitHub integration for Hephaestus'
type: 'technical'
topic: 'Agent CLIs and GitHub integration for Hephaestus'
decision: 'How Hephaestus should drive Claude Code and Codex CLI unattended in containers and integrate with GitHub'
source: 'native run'
status: complete
preset: 'standard'
validation: 'normal'
created: '2026-11-25'
updated: '2026-11-25'
claims_verified: 4
claims_unverified: 13
---

# Technical research: agent CLIs and GitHub integration for Hephaestus

**Decision this research serves:** How Hephaestus should drive Claude Code and Codex CLI unattended in containers and integrate with GitHub.

**How to read confidence.** *Verified* means a second, independent publisher agreed. *Unverified* means a single source, even when that source is the vendor's own documentation. Several sources were read through search summaries rather than fetched in full; those are marked in the source appendix.

## Executive summary

Both agents can be driven the way the PRD needs: as child processes with machine-readable event streams, resumable sessions, and per-run model selection. Claude Code offers `claude -p --output-format stream-json` [1]; Codex offers `codex exec --json` [7]. Hephaestus should integrate through these command-line interfaces behind an internal adapter per agent, and should not adopt the Agent Client Protocol for now.

Three findings drive the design:

1. **Subscription authentication is technically supported for unattended use, but sits in a policy grey zone for Claude.** Anthropic documents a one-year token for "CI pipelines, scripts" (`claude setup-token`) [2], and OpenAI documents ChatGPT-account auth in CI as an advanced path [7]. Anthropic's terms, however, reserve subscription sign-in for "ordinary use", forbid developers from routing requests through plan credentials on behalf of their users, and state that plan limits "assume ordinary, individual usage" [3]. A self-hosted tool where the owner signs in to the unmodified binary with their own account fits the stated carve-out, but heavy automation on a subscription is exposed to enforcement "without prior notice" [3].
2. **Usage-limit detection, the basis of handoff and wait-and-resume, rests on undocumented behaviour.** Claude Code emits a `rate_limit_event` with a reset time that appears in no official documentation [5][6], and one bug report shows a run ending at the weekly limit with no event at all [5]. Codex's `usage_limit_reached` error with a reset time is known from its source and issue tracker, not from its documented event stream [10]. Neither tool resumes on its own after a limit resets [10].
3. **A non-interactive Claude Code run executes hooks and connects MCP servers defined in the repository, with no trust prompt, unless `--bare` is used — and `--bare` does not work with subscription credentials** [1][2]. In subscription mode the repository's own content is therefore executable configuration. The container is the security boundary, not the agent.

The biggest caveat: findings 2 and the Codex details were not confirmed against primary documentation or a real limit. A short technical spike against both tools is needed before the limit-handling design is fixed.

## 1. Claude Code — unattended execution

**Invocation and output.** `claude -p "<prompt>"` runs non-interactively and is the command-line face of the Agent SDK, which otherwise ships only as Python and TypeScript packages [1]. `--output-format json` returns a single object with `result`, `session_id`, usage metadata, and `total_cost_usd` (a client-side estimate); `stream-json` with `--verbose` returns newline-delimited events, starting with `system/init` [1]. `--json-schema` yields schema-conforming output in a `structured_output` field, which is absent if the run ends without valid output [1]. Confidence: high (verified).

**Exit behaviour.** Exit code is 0 on success and non-zero on failure; a failure inside the run is printed as the result on stdout [1]. SIGTERM exits with code 143 and leaves the turn unfinished; SIGINT ends the turn cleanly [1]. `--max-turns` and `--max-budget-usd` cap a run [1][4].

**Sessions.** `--resume <session_id>` continues a specific conversation; the id comes from the JSON output. Sessions are stored on the machine and found by id [1]. Resuming inside a fresh container therefore requires the configuration directory (`CLAUDE_CONFIG_DIR`, default `~/.claude`) to persist [2].

**Model and effort.** `--model` takes an alias or full name; `--effort` takes `low`, `medium`, `high`, `xhigh`, `max`, with availability varying by model; `--fallback-model` covers an overloaded or unavailable model for one turn [4]. Confidence: medium (official page read through excerpts).

**Permissions.** For unattended runs the documented controls are `--allowedTools`, `--permission-mode`, and `--permission-prompts none`, which denies anything that would prompt and lists denials in the result [1]. `--dangerously-skip-permissions` bypasses prompts; the documentation calls for isolation in a container or VM and a non-root user, and the mode refuses to start as root on Linux [4].

**Repository content is executed.** Without `--bare`, a `-p` session "runs the hooks in a project's `.claude/settings.json` and connects the servers in its `.mcp.json`, even in a folder you've never trusted", with no trust dialog [1]. `--bare` skips hooks, skills, plugins, MCP servers, memory, and `CLAUDE.md`, and is the recommended mode for scripted calls [1].

**Authentication.** Options include `ANTHROPIC_API_KEY`, an `apiKeyHelper` script, subscription login, and `CLAUDE_CODE_OAUTH_TOKEN` — a one-year token produced by `claude setup-token`, documented for "CI pipelines, scripts, or other environments where interactive browser login isn't available" [2]. The token authenticates with the subscription and can only make model requests [2]. Bare mode reads neither OAuth credentials nor this token; it requires an API key [1][2]. In `-p` mode an `ANTHROPIC_API_KEY` in the environment always wins over subscription credentials [2]. On Linux, login credentials live in `~/.claude/.credentials.json`; a stored login has an end date that use does not extend [2]. Confidence: high, single source.

**Usage policy.** Verbatim from Anthropic: OAuth authentication "is intended exclusively for purchasers of Claude Free, Pro, Max, Ultra, Team, and Enterprise subscription plans and is designed to support ordinary use of Claude Code and other native Anthropic applications." Developers building products "should use API key authentication"; Anthropic "does not permit third-party developers to offer Claude.ai login into their own applications, or to route requests through Free, Pro, Max, or Ultra plan credentials on behalf of their users", and developers "may not collect, store, or intermediate Claude.ai credentials or session tokens". The restriction does not "prevent an end user from signing in to the unmodified Claude Code binary with their own Claude subscription". Plan limits "assume ordinary, individual usage of Claude Code and the Agent SDK", and Anthropic "may [enforce] without prior notice" [3]. Confidence: high (verified; secondary sources agree on substance).

**Usage-limit signalling.** Documented: `system/api_retry` events carry an `error` category such as `rate_limit`, `overloaded`, or `billing_error` [1]. Undocumented: a `rate_limit_event` with `status`, `resetsAt` (epoch seconds), and `rateLimitType` (`five_hour`, `seven_day`), emitted on healthy runs too; a real stop is reported as `status: "rejected"` plus `usage_limit_reached` on the result [5][6]. One bug report describes a run that hit the weekly limit and exited with code 1 with no event and no result on stdout [5]. A `/usage` prompt in print mode reportedly returns utilisation and reset times without a model call [6]. Confidence: low — user reports and third-party code reading, partly against mocks.

## 2. Codex CLI — unattended execution

Everything in this section was read through search summaries of the official pages; treat specifics as medium confidence until checked against the installed version.

**Invocation and output.** `codex exec "<prompt>"` runs without the terminal interface; stdout carries the final message and progress goes to stderr [7]. `--json` turns stdout into a JSONL stream with `thread.started`, `turn.started`, `turn.completed`, `turn.failed`, `item.*`, and `error` events [7]. `--output-schema` constrains the final response; `-o` writes it to a file [7][8]. Confidence: medium (verified by third-party cheat sheets).

**Sessions.** `codex exec resume <SESSION_ID>` or `--last`; the id is reported as the `thread_id` of the first event [7].

**Sandbox.** `codex exec` defaults to read-only; edits need `--sandbox workspace-write`; `danger-full-access` and `--dangerously-bypass-approvals-and-sandbox` are intended for isolated runners or containers [7][8]. Whether Codex's own sandbox functions inside a Docker container was not established.

**Model and effort.** `-m`/`--model` selects the model per run [10]. Reasoning effort is the `model_reasoning_effort` configuration key; no dedicated flag was found, and a `-c` override for `codex exec` is unconfirmed [10]. Confidence: low.

**Authentication.** Methods are browser ChatGPT login, device code, API key, and access token; `codex login status` exits 0 when logged in [8]. OpenAI steers automation toward API keys (`CODEX_API_KEY` per run) [7]. ChatGPT-account auth in CI is documented as an advanced path: seed `auth.json`, persist the refreshed copy after each run, and treat the file as a password [7]. Enterprise workspaces can issue access tokens for scripts [9]. No OpenAI policy statement comparable to Anthropic's was found in this run; absence here is not evidence of permission. Confidence: medium (verified by community tooling using the same path).

**Usage-limit signalling.** A plan limit is reported as HTTP 429 with error type `usage_limit_reached`, carrying a reset time; the source has a `UsageLimitReachedError` with `resets_at` [10]. How this appears in the `--json` stream was not found. Automatic resume after reset is an open feature request, not a feature [10]. Confidence: low.

## 3. Containers, including Windows

Both tools are routinely run in Linux containers, and Anthropic's own documentation describes persisting credentials in a volume for dev containers [2]. Claude Code's bypass mode expects a non-root user [4].

Claude Code runs natively on Windows 10 1809+ and Windows Server 2019+, without its sandbox [16]. Codex has native Windows support with a Windows-specific sandbox [10]. **No source documents either tool running inside a Windows container**, and every published image found is Linux-based. Windows execution environments should be treated as unsupported until someone proves otherwise.

## 4. Agent Client Protocol

Only secondary sources were retrieved. They agree that neither tool speaks ACP natively: both are reached through adapters maintained in the ACP project, the Claude adapter built on the Agent SDK and the Codex adapter on the Codex App Server [11]. ACP v1 is described as stable since mid-2026 with v2 in draft and remote transport unfinished; sources contradict each other on package names and maintainers [11]. The protocol targets editor-to-agent sessions. Confidence: low.

## 5. GitHub integration

- **Identity.** A pull request's author cannot approve it [14], so Hephaestus needs an identity separate from the Owner's. Actions under a GitHub App installation token appear as the app's bot account [20].
- **Rate limits.** An installation token gets at least 5,000 REST requests per hour, with a separate 5,000-point GraphQL budget [12].
- **Polling.** Authenticated conditional requests that return `304 Not Modified` do not count against the primary limit; some endpoints return a minimum poll interval [13]. GitHub recommends webhooks, but polling is workable and needs no public endpoint.
- **Who applied a label.** The issue timeline endpoint returns `labeled` events with an `actor` object identifying the user [15]. This is what the trusted-labeller rule needs.

All single-source (GitHub Docs, read through summaries). Confidence: medium.

## 6. Competitive scan

- **OpenAI Symphony** is the closest analogue: an open specification with an Elixir reference implementation that polls a tracker, creates an isolated workspace per issue, and runs a coding agent there, with policy in a version-controlled `WORKFLOW.md` [17]. Two differences matter. The agent itself performs tracker writes [17], where Hephaestus keeps all GitHub credentials away from agents. And it is built around Codex, with no cross-vendor review or handoff described. Its specification is worth reading before the architecture is written: workspace lifecycle hooks, retry backoff, and reconciliation are solved problems there.
- **GitHub Agentic Workflows** compiles Markdown workflows into GitHub Actions and supports Claude Code and Codex as engines, but runs on Actions runners and accepts API keys or workload identity, not personal subscriptions; still a technical preview in October 2026 [18].
- **OpenHands** is a self-hostable agent that goes from issue to pull request in a container with any model; it is the agent, not a layer above other vendors' tools [19].

No project was found that combines existing vendor CLIs under the user's own subscriptions, independent cross-vendor review, limit-driven handoff, and orchestrator-owned GitHub writes. One scan is weak evidence of a gap; several small orchestrators surfaced incidentally and were not examined.

## Cross-dimension insights

1. **Authentication mode decides the security posture.** With an API key, Claude Code can run in `--bare` and ignore the repository's hooks and MCP servers. With a subscription it cannot, so anything merged into the repository's `.claude/` or `.mcp.json` runs inside the workspace [1][2]. This strengthens three PRD rules at once: the container boundary (FR-17), agents holding no GitHub credential (FR-18), and configuration taking effect only from the Base Branch (FR-2).
2. **The feature the Owner values most is the least documented.** Handoff and wait-and-resume (FR-27 to FR-29) depend on signals that vendors do not document for Claude and that were not confirmed for Codex's event stream. Both vendors leave resume-after-reset to the caller, which confirms the feature is worth building and that it must be built defensively.
3. **A subscription is both the reason for the product and its main external risk.** The economic case for handoff exists only in subscription mode, which is exactly the mode Anthropic's terms describe as intended for ordinary individual use [3].
4. **The Agent SDK is not an option for a .NET core.** It ships for Python and TypeScript only [1]; the command line is the integration surface, which also keeps the two adapters symmetrical.

## Recommendations

1. **Integrate through the CLIs behind one internal adapter per agent** (`claude -p --output-format stream-json --verbose`; `codex exec --json`), normalising events, results, session ids, and failures. Feeds: architecture — agent abstraction, ADR on agent integration. Basis: verified for both tools.
2. **Do not adopt ACP for the first releases.** Keep the adapter interface narrow enough that an ACP-backed adapter could be added. Feeds: ADR on agent integration. Basis: low-confidence sources, but the adapter-only status and churn are consistent across them.
3. **Support both authentication modes per agent, as FR-25 says, and document the risk plainly.** Hephaestus must never implement its own sign-in flow or handle anyone's credentials except those the Owner supplies to their own installation; sign-in completes through the vendor's tool. Installation docs should state that subscription use under automation may conflict with plan terms and can be restricted without notice, and that an API key is the conservative choice. Feeds: PRD FR-25, NFR-8; threat model. Basis: verified policy text.
4. **Choose the invocation by authentication mode.** API key: `--bare` plus explicit system prompt and tool permissions. Subscription: non-bare, inside the container, with the repository treated as untrusted executable content. Feeds: architecture — workspace and security; threat model. Basis: single official source, high confidence.
5. **Run agents as a non-root user in the container with permissions bypassed, and let the container enforce limits.** Feeds: ADR on workspace isolation. Basis: official excerpts, medium confidence.
6. **Classify failures from layered signals, never from one.** For Claude: the `rate_limit_event` status and reset time when present, `api_retry` error categories, then exit code with transcript inspection as the fallback, plus a `/usage` probe before starting a Task. For Codex: the `usage_limit_reached` error and reset time. Treat an unclassifiable failure as "unknown", not as a limit. Feeds: PRD FR-27, NFR-11; architecture — failure classification. Basis: low-confidence sources; requires the spike in Open Question 1.
7. **Map Effort per agent inside the adapter.** Claude takes `--effort`; Codex takes a configuration key, so each run may need its own configuration. An Alias must resolve to agent-specific parameters, not a shared flag. Feeds: architecture — alias resolution. Basis: medium for Claude, low for Codex.
8. **Persist each agent's session state per Task** (configuration directory volume) so a restarted container can resume, and keep Handoff between vendors artifact-based as the PRD already requires. Feeds: architecture — workspace lifecycle. Basis: high for Claude.
9. **Use a GitHub App as Hephaestus's identity, poll with conditional requests, and read the issue timeline to verify who applied the Marker Label.** Feeds: ADR on task provider; PRD FR-6, NFR-6. Basis: GitHub Docs, medium confidence.
10. **Keep Windows execution environments out of scope**, as the PRD does. Basis: absence of evidence.
11. **Read the Symphony specification before writing the architecture**, and state the differentiation as hypotheses. Feeds: architecture; `docs/vision.md`.

## Open questions

1. **What do both tools actually emit when a real subscription limit is hit, in their event streams?** Needs a spike: run each tool to its limit (or capture a real occurrence) and record exit code, events, and reset-time fields. Blocks the detailed design of FR-27 to FR-29, not the v0.1 architecture.
2. **Can project hooks and MCP servers be disabled in a non-bare Claude Code run** (for example through settings), so subscription mode can also ignore repository-supplied configuration? Check the settings reference.
3. **Does `codex exec` accept a per-run reasoning-effort override**, and does its sandbox work inside a container or must it be disabled there?
4. **Does OpenAI publish terms on ChatGPT-plan credentials in automation** comparable to Anthropic's?
5. **Does Codex report token usage or cost per run** in its event stream, as Claude Code does? Needed for the spend limit in NFR-5.
6. **Does either tool run in a Windows container?** Only matters if Windows execution environments are revived.
7. **ACP facts from primary sources** (specification site, adapter repositories), if ACP is reconsidered.

## Source appendix

| [n] | Supports | Publisher | Pub date | Accessed | Confidence |
|---|---|---|---|---|---|
| [1] | Claude Code non-interactive mode: flags, output, exit codes, sessions, bare mode, permissions | [Anthropic — Run Claude Code programmatically](https://code.claude.com/docs/en/headless) | live docs, undated | 2026-11-25 | high (fetched in full) |
| [2] | Claude Code authentication, token for CI, precedence, storage | [Anthropic — Authentication](https://code.claude.com/docs/en/authentication) | live docs, undated | 2026-11-25 | high (fetched in full) |
| [3] | Usage policy on OAuth and subscription credentials | [Anthropic — Legal and compliance](https://code.claude.com/docs/en/legal-and-compliance) | live docs, undated | 2026-11-25 | high (fetched in full) |
| [4] | Model, effort, fallback, max-turns, bypass-permissions flags | [Anthropic — CLI reference](https://code.claude.com/docs/en/cli-reference) | live docs, undated | 2026-11-25 | medium (excerpts) |
| [5] | Undocumented rate-limit event; run ending at weekly limit without it | [anthropics/claude-code issue #103820](https://github.com/anthropics/claude-code/issues/103820) and related issues #78476, #77018 | 2026 | 2026-11-25 | low (user reports, summaries) |
| [6] | Shape of `rate_limit_event`, limit-stop markers, `/usage` probe | [lucas-devries/groundhog issue #11](https://github.com/lucas-devries/groundhog/issues/11) and other third-party trackers | 2026 | 2026-11-25 | low (third-party, summaries) |
| [7] | Codex non-interactive mode, JSON events, resume, sandbox, CI auth | [OpenAI — Non-interactive mode](https://developers.openai.com/codex/noninteractive) | live docs, undated | 2026-11-25 | medium (summaries) |
| [8] | Codex flags and login methods | [OpenAI — Developer commands](https://developers.openai.com/codex/cli/reference) | live docs, undated | 2026-11-25 | medium (summaries) |
| [9] | Codex access tokens for automation | [OpenAI — Access tokens](https://developers.openai.com/codex/enterprise/access-tokens) | live docs, undated | 2026-11-25 | medium (summaries) |
| [10] | Codex usage-limit error, model flag, effort key, no auto-resume, Windows support | [openai/codex issue #12299](https://github.com/openai/codex/issues/12299), #21073, and third-party guides | 2026 | 2026-11-25 | low (summaries) |
| [11] | ACP status and adapters | [agent.space — What Is the Agent Client Protocol](https://agent.space/blog/what-is-agent-client-protocol) and other secondary write-ups | 2026-03 to 2026-10 | 2026-11-25 | low (secondary only) |
| [12] | Installation token rate limits | [GitHub Docs — Rate limits for the REST API](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api) | live docs, undated | 2026-11-25 | medium (summaries) |
| [13] | Conditional requests and polling | [GitHub Docs — Best practices for using the REST API](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api) | live docs, undated | 2026-11-25 | medium (summaries) |
| [14] | Author cannot approve own pull request | [GitHub Docs — Approving a pull request with required reviews](https://docs.github.com/en/pull-requests/how-tos/review-pull-requests/approving-a-pull-request-with-required-reviews) | live docs, undated | 2026-11-25 | medium (summaries) |
| [15] | `labeled` timeline events carry the actor | [GitHub Docs — REST API endpoints for timeline events](https://docs.github.com/en/rest/issues/timeline) | live docs, undated | 2026-11-25 | medium (summaries) |
| [16] | Claude Code Windows requirements | [Anthropic — Advanced setup](https://code.claude.com/docs/en/setup) | live docs, undated | 2026-11-25 | medium (excerpts) |
| [17] | Symphony specification | [openai/symphony — SPEC.md](https://github.com/openai/symphony/blob/main/SPEC.md) | 2026-04 | 2026-11-25 | medium (summaries) |
| [18] | GitHub Agentic Workflows engines, auth, status | [carrick-sh/carrick issue #644](https://github.com/carrick-sh/carrick/issues/644) and github/gh-aw issues | 2026-10 | 2026-11-25 | low (third-party evaluation) |
| [19] | OpenHands positioning | [Winder.ai — A Comparison of AI Agent Harnesses in 2026](https://winder.ai/ai-agent-harness-comparison/) | 2026 | 2026-11-25 | low (secondary) |
| [20] | App bot identity; webhooks preferred | [GitHub Docs — Best practices for creating a GitHub App](https://docs.github.com/en/apps/creating-github-apps/about-creating-github-apps/best-practices-for-creating-a-github-app) | live docs, undated | 2026-11-25 | medium (summaries) |

## Staleness map

Re-check dates computed by `recon_kit.py staleness` from the access date, using these windows: integration and authentication details 1 month, policy and landscape 3 months, platform limits 6 months.

| Claim group | Class | Re-check by |
|---|---|---|
| Claude Code non-interactive interface and flags [1][4] | integration | 2026-12-25 |
| Claude Code authentication options and CI token [2] | auth | 2026-12-25 |
| Claude `rate_limit_event` behaviour [5][6] | integration | 2026-12-25 |
| Codex exec interface, authentication and limits [7][8][10] | integration | 2026-12-25 |
| Windows container support [16] | integration | 2026-12-25 |
| Symphony, gh-aw, OpenHands positioning [17][18][19] | landscape | 2027-01-01 |
| ACP status and adapters [11] | landscape | 2027-01-20 |
| Anthropic policy on subscription credentials [3] | policy | 2027-02-25 |
| GitHub rate limits, polling, approval rule, timeline [12][13][14][15] | platform | 2027-05-25 |

Earliest re-check: **2026-12-25**. Both command-line tools release frequently; re-verify flags against the installed versions when implementation of each adapter starts, whatever the date.

**Process notes.** Run inline without research subagents; the semantic citation check by a fresh-context reviewer and the prose polish pass were not performed. Sources [4] and [7]-[20] were read through search summaries, not fetched in full.
