# Digest — Codex CLI unattended execution (round 1)

Sources (accessed 2026-11-25; all read through search summaries, not fetched in full):
- S8: OpenAI, "Non-interactive mode", https://developers.openai.com/codex/noninteractive (official)
- S9: OpenAI, "Developer commands" (CLI reference), https://developers.openai.com/codex/cli/reference (official)
- S10: OpenAI, "Access tokens", https://developers.openai.com/codex/enterprise/access-tokens (official)
- S11: openai/codex issue #9253 (GitHub, Jan 2026)
- S12: community sources (amchii/codexx fork docs, icoretech/codex-action, OpenSandbox docs)

## Claims

| # | Claim | Source | Class | Confidence |
|---|---|---|---|---|
| 31 | `codex exec "<prompt>"` runs Codex without the TUI for scripts and CI; stdout carries only the final agent message and progress goes to stderr; the prompt can come from stdin. | S8 | integration | medium |
| 32 | `--json` turns stdout into a JSONL event stream with event types `thread.started`, `turn.started`, `turn.completed`, `turn.failed`, `item.*`, `error`; items cover agent messages, reasoning, command executions, file changes, MCP tool calls, web searches, plan updates. | S8 | integration | medium |
| 33 | `--output-last-message`/`-o` writes the final message to a file; `--output-schema <path>` constrains the final response to a JSON Schema. | S8, S9 | integration | medium |
| 34 | Resume: `codex exec resume --last "<prompt>"` or `codex exec resume <SESSION_ID>`; the session id is the `thread_id` of the first `thread.started` event (the last detail from a third-party doc). | S8, S12 | integration | medium |
| 35 | Sandbox: `codex exec` defaults to read-only; edits need `--sandbox workspace-write`; `danger-full-access` is intended for isolated runners or containers; `--dangerously-bypass-approvals-and-sandbox` (`--yolo`) drops both approvals and sandbox; `--full-auto` is deprecated. | S8, S9 | security | medium |
| 36 | `--ephemeral` avoids writing session files; `--ignore-user-config` skips `$CODEX_HOME/config.toml`. | S9 | integration | medium |
| 37 | Login methods: browser ChatGPT OAuth (default), device code, API key (`codex login --with-api-key`, secret on stdin), access token (`--with-access-token`). `codex login status` exits 0 when logged in. | S9 | auth | medium |
| 38 | OpenAI's guide steers automation toward API keys; a per-run key can be passed as `CODEX_API_KEY`. | S8 | auth | medium |
| 39 | ChatGPT-account auth in CI is documented as an "advanced" path for trusted runners or for using ChatGPT/Codex rate limits instead of API billing: seed `auth.json` and persist the refreshed copy after each run; the file must be treated like a password. | S8 | auth | medium |
| 40 | Enterprise workspaces can issue Codex access tokens (`CODEX_ACCESS_TOKEN`) for scripts, scheduled jobs and CI. | S10 | auth | medium |
| 41 | `auth.json` lives at `$CODEX_HOME/auth.json` (default `~/.codex`), is not host-bound, and can be copied into a container. | S12 | auth | low |
| 42 | Device-code login can be disabled by a workspace admin, which broke headless logins in January 2026. | S11 | auth | low |

## Leads
- How a ChatGPT-plan usage limit surfaces in `codex exec --json` (error payload, reset time).
- Model and reasoning-effort selection flags.
- Official Docker / Windows guidance.

## Looked for, not found
- Full per-event JSON schema.
- An OpenAI policy statement equivalent to Anthropic's on subscription credentials in automation.
