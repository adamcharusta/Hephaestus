# Digest — Claude Code: flags, usage limits, usage policy (round 2)

Sources (accessed 2026-11-25):
- S3: Anthropic, "Legal and compliance", https://code.claude.com/docs/en/legal-and-compliance (official; fetched in full)
- S4: Anthropic, "CLI reference", https://code.claude.com/docs/en/cli-reference (official; read through search excerpts only)
- S5: anthropics/claude-code issues #78476, #77018, #103820, #49640 (GitHub issue tracker; user reports) — read through search summary
- S6: third-party orchestrator issue trackers and blogs on `rate_limit_event` (lucas-devries/groundhog #11, maj0rpain/orchestrator #1610, littlebearapps cheatsheet) — read through search summary
- S7: Anthropic, "Agent SDK overview", https://code.claude.com/docs/en/agent-sdk/overview (official; search excerpt only)

## Claims

| # | Claim | Source | Class | Confidence |
|---|---|---|---|---|
| 19 | "OAuth authentication is intended exclusively for purchasers of Claude Free, Pro, Max, Ultra, Team, and Enterprise subscription plans and is designed to support ordinary use of Claude Code and other native Anthropic applications." | S3 (verbatim) | policy | high |
| 20 | "Anthropic does not permit third-party developers to offer Claude.ai login into their own applications, or to route requests through Free, Pro, Max, or Ultra plan credentials on behalf of their users. Moreover, developers may not collect, store, or intermediate Claude.ai credentials or session tokens — sign-in to a Claude account must complete through Anthropic's own flow." | S3 (verbatim) | policy | high |
| 21 | The restriction does not "prevent an end user from signing in to the unmodified Claude Code binary with their own Claude subscription, including where a platform hosts Claude Code". Customers may provision their own API keys in environments, secret managers, or machine images. | S3 (verbatim/paraphrase) | policy | high |
| 22 | Running Claude Code inside a product or service requires the binary to be unmodified, no authentication method removed or restricted, and each end user authenticating with their own credential and being billed directly. | S3 | policy | high |
| 23 | "Advertised usage limits for Pro, Max, and Ultra plans assume ordinary, individual usage of Claude Code and the Agent SDK." Anthropic "reserves the right to take measures to enforce these restrictions and may do so without prior notice." | S3 (verbatim) | policy | high |
| 24 | `--model` takes an alias or full model name; `--effort` takes low/medium/high/xhigh/max (availability varies by model); `--fallback-model` takes up to three models used when the primary is overloaded or unavailable, per turn; `--max-turns` caps agentic turns in `-p` mode and exits with an error when reached. | S4 (excerpt) | integration | medium |
| 25 | `--dangerously-skip-permissions` equals `--permission-mode bypassPermissions`; the docs call for isolation (container, VM, or sandbox runtime) and a non-root user; running as root on Linux blocks startup in this mode. | S4 (excerpt) | security | medium |
| 26 | In `stream-json` output a `rate_limit_event` carries `rate_limit_info` with `status`, `resetsAt` (epoch seconds), `rateLimitType` (`five_hour`, `seven_day`, ...), and overage fields; it is emitted on healthy runs as well. Not found in official documentation. | S5, S6 | integration | low (undocumented) |
| 27 | Reported markers of a real subscription limit stop: `status: "rejected"` with `resetsAt`, and `usage_limit_reached` on the result; `error: "rate_limit"` alone does not distinguish a subscription limit from an API 429. Evidence comes largely from mocks and code reading. | S6 | integration | low |
| 28 | Bug report #103820 (v2.1.304): a `-p` run that hit the weekly limit exited with code 1 with no rate-limit event and no result on stdout; the message was only in the transcript. | S5 | integration | low (single report) |
| 29 | `claude -p "/usage" --output-format stream-json --verbose` reportedly returns per-window utilisation and reset times without a model call. | S6 | integration | low |
| 30 | A third-party product may offer claude.ai sign-in or subscription rate limits only with Anthropic's prior approval; the Agent SDK docs point developers to API-key authentication. | S7 (excerpt) | policy | medium |

## Interpretation notes (not claims)
- Hephaestus is self-hosted, open source, and never handles Claude credentials for anyone other than the person running it. The owner signs in to the unmodified binary with their own subscription or sets their own token. This fits the carve-out in claim 21 for the owner's own use; it is nevertheless automated orchestration, which claim 23 says plan limits are not sized for. This is a policy risk to state, not a settled permission.
- Usage-limit detection for subscription mode rests on undocumented behaviour (claims 26-29). It must be validated by a spike against a real limit and guarded by a fallback (non-zero exit + transcript inspection).
