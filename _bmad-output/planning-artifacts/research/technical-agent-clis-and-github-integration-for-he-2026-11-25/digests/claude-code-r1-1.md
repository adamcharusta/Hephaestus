# Digest — Claude Code unattended execution (round 1)

Sources (both official, accessed 2026-11-25):
- S1: Anthropic, "Run Claude Code programmatically", https://code.claude.com/docs/en/headless (no publication date shown; references versions up to v2.1.302)
- S2: Anthropic, "Authentication", https://code.claude.com/docs/en/authentication (no publication date shown; references versions up to v2.1.318)

## Claims

| # | Claim | Source | Class | Confidence |
|---|---|---|---|---|
| 1 | Non-interactive mode is `claude -p "<prompt>"` (`--print`); it is the CLI face of the Agent SDK, which also ships as Python and TypeScript packages. | S1 | integration | high |
| 2 | Exit code is 0 on success and non-zero on failure; invalid flags are reported on stderr before the run; a failure inside the run (e.g. missing auth) is printed as the result on stdout. | S1 | integration | high |
| 3 | `--output-format` accepts `text` (default), `json`, `stream-json` (newline-delimited events). `json` output carries `result`, `session_id`, usage metadata and `total_cost_usd` with per-model breakdown; costs are client-side estimates. | S1 | integration | high |
| 4 | `--json-schema` with `--output-format json` returns schema-conforming data in `structured_output`; the field is absent if the run ends without valid structured output. | S1 | integration | high |
| 5 | Streaming: `--output-format stream-json --verbose --include-partial-messages`. First event is `system/init` (model, tools, MCP servers, plugins, `capabilities`). `result` messages must be found by `type` because further turns can follow. | S1 | integration | high |
| 6 | Retryable API failures emit `system/api_retry` events with `attempt`, `max_retries`, `retry_delay_ms`, `error_status`, and an `error` category from a fixed list including `rate_limit`, `overloaded`, `billing_error`, `authentication_failed`, `server_error`. | S1 | integration | high |
| 7 | Sessions: `--continue` resumes the most recent conversation; `--resume <session_id>` (or absolute path to the `.jsonl` transcript) resumes a specific one; the session id is in the JSON output. Sessions are found by id across projects on the same machine. | S1 | integration | high |
| 8 | SIGTERM makes `claude -p` exit with code 143, leaving the in-progress turn unfinished and killing running Bash process trees; SIGINT (or SDK `interrupt()`) ends the turn cleanly. `CLAUDE_CODE_RESUME_INTERRUPTED_TURN=1` continues an interrupted turn on resume. | S1 | integration | high |
| 9 | Permissions: `--allowedTools` (permission-rule syntax, e.g. `Bash(git diff *)`), `--permission-mode` (`auto`, `dontAsk`, `acceptEdits`, manual default since v2.1.302), `--permission-prompts none` for unattended runs (v2.1.259+) — anything that would prompt is denied and listed in `permission_denials`. | S1 | integration | high |
| 10 | `--bare` skips auto-discovery of hooks, skills, commands, subagents, plugins, MCP servers, auto memory and CLAUDE.md; it is the recommended mode for scripted calls and will become the default for `-p`. Bare mode never reads OAuth credentials: it requires `ANTHROPIC_API_KEY` or `apiKeyHelper`. | S1, S2 | integration | high |
| 11 | Without `--bare`, a `-p` session runs the hooks in the project's `.claude/settings.json` and connects servers from its `.mcp.json` even in a never-trusted folder, with no trust dialog. | S1 | security | high |
| 12 | `--max-budget-usd` caps spend for a run; `--append-system-prompt` / `--system-prompt` customise the system prompt. | S1 | integration | high |
| 13 | Subagent activity appears in the stream with `parent_tool_use_id`; `--forward-subagent-text` includes subagent text. | S1 | integration | high |
| 14 | Auth methods include API key (`ANTHROPIC_API_KEY`), `apiKeyHelper`, subscription login (`/login`, Pro/Max/Ultra/Team/Enterprise), and a long-lived token: `claude setup-token` generates a one-year OAuth token to be set as `CLAUDE_CODE_OAUTH_TOKEN`, documented "for CI pipelines, scripts, or other environments where interactive browser login isn't available". | S2 | auth | high |
| 15 | The setup-token credential authenticates with the Claude subscription, can only make model requests, and is not read in bare mode. | S2 | auth | high |
| 16 | Credential precedence: cloud provider > `ANTHROPIC_AUTH_TOKEN` > `ANTHROPIC_API_KEY` > `apiKeyHelper` > `CLAUDE_CODE_OAUTH_TOKEN` > profiles > subscription `/login`. In `-p` mode an `ANTHROPIC_API_KEY` in the environment is always used when present. | S2 | auth | high |
| 17 | On Linux credentials are stored in `~/.claude/.credentials.json` (mode 0600), or under `CLAUDE_CONFIG_DIR` when set; the devcontainer docs describe persisting them in a volume across rebuilds. | S2 | auth | high |
| 18 | A stored claude.ai login has an end date and is not extended by use; `claude auth status --text` shows it; `claude -p` warns on stderr when under 10 days remain. | S2 | auth | high |

## Leads
- CLI reference for `--model`, `--effort`, `--fallback-model`, `--max-turns`, `--dangerously-skip-permissions`.
- How a subscription usage limit (as opposed to an API 429) surfaces in `-p` output.
- Any usage-policy statement restricting subscription credentials in third-party automation.
- Sandboxing / devcontainer guidance for running with permissions bypassed.

## Looked for, not found on these pages
- An explicit usage-limit result subtype or reset time field.
- Windows container support.
