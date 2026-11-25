# Digest — Codex limits and flags, ACP, GitHub limits, Windows containers (round 1-2)

All accessed 2026-11-25 through search summaries unless noted.

## Codex CLI: usage limits and model/effort selection
Sources: S13 openai/codex issues #12299, #21073 (GitHub); S14 third-party guides (router.one, developersdigest.tech, verboo.ai, teamorouter.com).

| # | Claim | Source | Class | Confidence |
|---|---|---|---|---|
| 43 | A ChatGPT-plan usage limit arrives as an HTTP 429 whose body has `error.type` `usage_limit_reached` and carries plan type and reset time; Codex's Rust source has a `UsageLimitReachedError` with a `resets_at` field. One source names the code `usage_limit_exceeded` instead. | S13, S14 | integration | low |
| 44 | Capacity failures are distinct (HTTP 503, `server_is_overloaded`); "exceeded retry limit, last status: 429" is the label for 429s without the usage-limit type. | S14 | integration | low |
| 45 | Per-run model selection: `codex exec -m <name>` / `--model`. Limits can differ per model (#12299). | S13 | integration | low |
| 46 | Reasoning effort is the `model_reasoning_effort` key in `config.toml`; no documented dedicated CLI flag was found. Whether `-c model_reasoning_effort=<level>` works with `codex exec` is unconfirmed. | S14 | integration | low |
| 47 | Auto-resume after a usage-limit reset is an open feature request in openai/codex (#21073), i.e. not built in. | S13 | integration | low |

Not found: the exact `--json` event emitted on a usage-limit failure.

## Agent Client Protocol
Sources: S15 secondary write-ups (agent.space, agentrq.com, danilchenko.dev, codex.danielvaughan.com, morphllm.com), dated March-October 2026. No primary source (agentclientprotocol.com, GitHub repos) was retrieved.

| # | Claim | Source | Class | Confidence |
|---|---|---|---|---|
| 48 | ACP v1 is the stable protocol version as of September 2026; v2 is a draft. Remote transport (Streamable HTTP) is still a draft proposal. | S15 | landscape | low |
| 49 | Neither Claude Code nor Codex speaks ACP natively; both are reached through adapters in the ACP project: `@agentclientprotocol/claude-agent-acp` (built on the Claude Agent SDK) and `@agentclientprotocol/codex-acp` (drives the Codex App Server). | S15 | landscape | low |
| 50 | ACP is designed for editor-to-agent sessions (IDE clients); sources conflict on package names and maintainers across 2026, indicating churn. | S15 | landscape | low |

## GitHub API
Sources (official): S16 "Rate limits for the REST API", https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api; S17 "Best practices for using the REST API", https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api; S18 "Approving a pull request with required reviews", https://docs.github.com/en/pull-requests/how-tos/review-pull-requests/approving-a-pull-request-with-required-reviews; S19 "Best practices for creating a GitHub App".

| # | Claim | Source | Class | Confidence |
|---|---|---|---|---|
| 51 | A GitHub App installation token gets at least 5,000 REST requests per hour (scaling up to 12,500); GraphQL has a separate 5,000 points per hour. | S16 | integration | medium |
| 52 | Conditional requests (`If-None-Match` with a saved ETag) that return 304 do not count against the primary rate limit when authenticated; some endpoints return `X-Poll-Interval`. GitHub recommends webhooks over polling. | S17, S19 | integration | medium |
| 53 | A pull request's author cannot submit an approving review on it. | S18 | integration | medium |
| 54 | Actions taken with an installation token appear under the app's bot identity (`name[bot]`). | S19 | integration | medium |

Not found: secondary rate limit specifics; which endpoint best reports who applied a label (expected: issue events / timeline API — to confirm).

## Windows containers
Sources: S20 Anthropic "Advanced setup", https://code.claude.com/docs/en/setup (official, excerpt); S21 community guides.

| # | Claim | Source | Class | Confidence |
|---|---|---|---|---|
| 55 | Claude Code runs natively on Windows 10 1809+ / Windows Server 2019+; its sandbox is unavailable natively on Windows (WSL 2 only). | S20 | compatibility | medium |
| 56 | Codex CLI has native Windows support with its own Windows sandbox (two modes); Windows 11 recommended. | S21 | compatibility | low |
| 57 | No source documents either CLI running inside a Windows container (Server Core / Nano Server). All published container images and guides are Linux-based. | absence of evidence | compatibility | finding: unverified/unsupported |
