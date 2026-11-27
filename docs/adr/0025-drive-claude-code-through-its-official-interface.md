# ADR-0025: Drive Claude Code with one hardened invocation, read what it printed, and name a failure only when the stream does

- **Status:** Accepted
- **Date:** 2026-11-27
- **Spine:** AD-7, AD-10, AD-11, AD-12

## Context

Story 1.1 ran Claude Code 2.1.332 in a hardened container with an API key and with a subscription token, and recorded the invocation that held. Story 3.8 turns that into the adapter of Claude Code: pure, so that it starts no process and knows nothing of Docker, and tested with no vendor call.

## Decision

**One vector for both modes.** `ClaudeCodeAgentAdapter.DescribeInvocation` returns `claude -p <sentence> --output-format stream-json --verbose --dangerously-skip-permissions --no-session-persistence --model <model> [--effort <level>] [--json-schema <schema>] --safe-mode --setting-sources user --strict-mcp-config --settings <json> --tools Bash,Read,Edit,Write`. Only the secret
reference differs between an API key (`anthropic-api-key` to `ANTHROPIC_API_KEY`) and a subscription token (`claude-code-oauth-token` to `CLAUDE_CODE_OAUTH_TOKEN`). `--safe-mode` alone leaves the `env` block of a repository's settings in force and `--bare` does not offer the `Write` tool, so the invocation is `--safe-mode` with `--setting-sources user` (spike finding 1 and 2).
The hardening comes after every option that takes a value and ends with `--tools`, whose list is variadic. The tool list is closed: no `Skill`, no `Task`, nothing that acts outside the container. The environment holds the switches of Story 1.1 and, with them, `DISABLE_AUTOUPDATER` and `DISABLE_UPDATES`; it holds no secret value.

**The prompt is Core's.** One sentence, `AgentPrompt.Sentence`, names `/hephaestus/context/instruction.md`; it is in Core so that the adapter of Story 3.9 sends the same one and an Agent can be replaced by another without changing what is asked (AD-12).

**Support is declared, not discovered by failing.** `IAgentAdapter.SupportsAuthentication` and `SupportsEffort` return `Support`, which is yes or no with a message id for the Owner. `DescribeInvocation` refuses what is declared unsupported with `UnsupportedInvocationException`. Claude Code supports both modes, because the spike showed the hardening in both, and every Effort of Core, because `--effort` takes `low`, `medium`, `high`, `xhigh`, and `max`; a value outside the set is declared unsupported and never replaced.
The Effort's meaning is the CLI's; whether a given Model accepts a given level is for the vendor's service to refuse.

**The result is in the output stream.** The invocation leaves no file. With a result schema the CLI's final `result` event carries the document in `structured_output`; without one, its text in `result`. `InvocationRequest.ResultSchema` is the schema Core generated for the step kind, which the adapter gives to `--json-schema` as one line of JSON; the schema constrains shape and not meaning, so Core still validates the document (AD-13).

**Normalized, and nothing invented.** `Parse` yields events (`Started`, `Message`, `ToolCall`, `ToolResult`, `RateLimit`), the last result, the limit reports, and a count of lines it could not read. Usage is what the `result` event reported, and a field it did not report is null. A `rate_limit_event` is kept as it came and decides nothing: no recorded stream shows what a refused request looks like under a subscription, and AD-11 forbids an unrecognised failure from being taken for a usage limit,
so `usage-limit` is Story 7.1's.

**A class only when the stream names it.** `ClassifyExit` returns none for exit 0 with a result that is not an error; `agent-error` when the result is an error and the stream names it, which is an assistant message with the error `authentication_failed` (recorded with a wrong key and with no credential) or a result subtype that starts with `error_` (the CLI's own endings); and `unknown` for everything else that failed. An HTTP status in the result is not read as a class.

**The probe is the same hardening with a one-word request.** It needs a credential that works and capacity to answer, which is what AD-20 asks of a probe; reading its output is `Parse` and `ClassifyExit`, and what Story 3.10 does with the answer is its own.

**The planted-configuration test needs no credential and no vendor call.** The repository it plants has an `env` block that sets a key and `ANTHROPIC_BASE_URL` to a listener on the machine. Started with nothing switched off, the pinned CLI runs the hooks, starts the MCP server, and connects to the listener; started with the adapter's invocation and no credential, it does none of it and ends with `authentication_failed`.
The test runs the executable `deploy/agents.lock` pins, from `HEPHAESTUS_CLAUDE_CODE` or downloaded from the vendor and checked against the lock, and is skipped where there is none, but not in GitHub Actions. What runs only around a tool call needs a model and stays with the credentialed runs of AR-56.

## Alternatives considered

- *Two vectors, one per mode.* Rejected: the spike showed one that works in both, and a second would be a second thing to prove.
- *Classifying a rate limit event or an HTTP 429 as a usage limit now.* Rejected: nothing recorded shows what they look like when a limit is hit.
- *Reading the result from a file the Agent writes.* Rejected: a document the CLI constrains by schema in its own stream is not something the Agent can forget to write.
- *Showing the `env` block with a real credential and a vendor call.* Rejected: it would make the test depend on the Owner's credentials, and the listener on the machine shows the same thing, that the CLI honoured a setting the repository planted.
- *Counting every connection the CLI makes, to show it makes none.* Not done: the test shows that nothing reached the listener the repository pointed it at, and the stream shows it stopped for want of a credential; it claims nothing about other hosts.

## Consequences

A change of the pin in `deploy/agents.lock` fails `The_adapter_is_the_one_of_Claude_Code_and_runs_only_against_the_version_the_lock_pins` until the adapter, its transcripts, and the credentialed runs of `docs/spikes/injection` are repeated for the new version. Plugins are not claimed about: no run has loaded one from a repository.
