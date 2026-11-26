# Injection spike: Agent CLIs in a hardened container behind a proxy

Story 1.1. The spike that conditions AD-7 (the container is the security boundary, behind an egress proxy) and AD-8 (pinned Agent CLIs supplied by Hephaestus, not baked into Project images).

- **Run:** 2026-11-25, with the Owner's credentials for all four authentication modes. Every result file names the harness version that wrote it; all are from one version (`bdfdba94e1fa`).
- **Pins:** Claude Code 2.1.332, Codex CLI 0.172.0 with its `codex-code-mode-host` ([`scripts/agents.lock`](scripts/agents.lock)).
- **Host:** Docker Engine 29.9.3 on Docker Desktop, WSL2 kernel 6.6.87.2, linux/amd64 ([`host.txt`](results/02-run-matrix/host.txt)).
- **Models:** Claude Code `haiku` (resolved to `claude-haiku-5-5`). Codex `gpt-5-mini` with an API key; under subscription sign-in the CLI's default, which the stream does not name.

## Verdicts

| Decision | Verdict | Conditions the findings attach |
| --- | --- | --- |
| **AD-8** — CLIs pinned and injected | **confirmed** | The Claude Code build must match the image's C library, so two builds per architecture are pinned. Codex is two files, `codex` and `codex-code-mode-host`, and needs a CA bundle, which Hephaestus supplies beside the binaries. Fetching from the official channels at startup is permitted; redistribution of Claude Code is not. |
| **AD-7** — the container is the boundary, behind an egress proxy | **confirmed** | Both CLIs and all seven package managers work with the proxy as the only way out; Maven and Gradle need the proxy written into the Project image. With the hardened invocation no planted hook, MCP server, or setting executed in any authentication mode. `--bare` or `--safe-mode` alone is not enough. Whether the hardened invocation stops a repository's plugin was not shown, because no planted plugin loaded even with nothing switched off. |

Supported authentication modes, by the rule of AD-7 that a mode which cannot be guaranteed is unsupported:

| CLI | API key | Subscription sign-in |
| --- | --- | --- |
| Claude Code | **supported** | **supported technically**, with a token from `claude setup-token`. The vendor's terms leave a risk open that is the Owner's to accept (FR-25): see [Vendor terms](#5-vendor-terms). |
| Codex CLI | **supported** | **unsupported**. No planted configuration executed, but the credential file is a full account credential that must be kept and refreshed between runs, and the tools the account brings cannot be listed from the CLI (AR-8 stands). |

The fallback "Hephaestus base image" is not needed.

Two judgements in this report are the Owner's to confirm:

- An instruction file or a skill in the repository that the model acts on is counted as prompt content, not as executable configuration. AD-7 and the story name hooks, MCP servers, settings, and plugins. If instructions were to count, Codex would be unsupported in both modes, because it offers a repository's skills in every variant tried.
- "Supported" rests on short runs: one-line prompts and at most one tool call.

## What each acceptance criterion found

### 1. Both CLIs in the hardened container, on three images

The container definition, read from inside ([`debian-container.txt`](results/02-run-matrix/debian-container.txt), [`ubuntu-container.txt`](results/02-run-matrix/ubuntu-container.txt), [`alpine-container.txt`](results/02-run-matrix/alpine-container.txt)): user `10001:10001` with no passwd entry, every capability set empty, `NoNewPrivs: 1`, seccomp in filter mode, limits of 512 processes, 4 GB, and 2 CPUs, `/hephaestus/agents` read-only, `/hephaestus/home` a throwaway tmpfs, `/workspace` writable. The exact `docker run` arguments are in the header of every result file.

Each run asked the Agent to execute `id -u; echo tool-$((6*7))` with its shell tool and answer `pong`. "Completed" means exit 0 and the output `tool-42` in the stream, which only an executed command produces. The stream is `claude -p --output-format stream-json --verbose` and `codex exec --json`. The script fails if a cell does not meet this ([`summary.md`](results/02-run-matrix/summary.md), column "Tool ran").

| Image ([digests](results/02-run-matrix/base-images.txt)) | C library | Claude Code, API key | Claude Code, subscription | Codex, API key | Codex, subscription |
| --- | --- | --- | --- | --- | --- |
| `debian:13-slim` | glibc | completed | completed | completed | completed |
| `ubuntu:24.04` | glibc | completed | completed | completed | completed |
| `alpine:3.23` plus `bash` | musl | completed | completed | completed | completed |

Results: `<image>-<cli>-<mode>-stream.txt` in [`results/02-run-matrix/`](results/02-run-matrix/).

**The Claude Code build must match the image's C library.** `linux-x64` on glibc images, `linux-x64-musl` on musl images (and `linux-arm64`, `linux-arm64-musl`). The other build does not start: `exec ...: no such file or directory`, exit 255 ([`debian-claude-other-libc-build.txt`](results/02-run-matrix/debian-claude-other-libc-build.txt), [`alpine-claude-other-libc-build.txt`](results/02-run-matrix/alpine-claude-other-libc-build.txt)). Codex publishes one statically linked build per architecture; it runs on all three images.

**Codex needs a CA bundle; Claude Code does not.** Codex verifies TLS against the operating system's certificate store. `debian:13-slim` and `ubuntu:24.04` ship none, and there Codex fails every request with `invalid peer certificate: UnknownIssuer` and was still retrying when stopped after 40 seconds ([`debian-codex-dummy-key-no-ca-bundle.txt`](results/02-run-matrix/debian-codex-dummy-key-no-ca-bundle.txt)). With `CODEX_CA_CERTIFICATE` pointing at a bundle placed beside the binaries it works on all three. `SSL_CERT_FILE` works as well ([`debian-codex-dummy-key-ssl-cert-file.txt`](results/02-run-matrix/debian-codex-dummy-key-ssl-cert-file.txt)), but most TLS libraries read it, so it would redirect other tools in the container too. Claude Code carries its own certificates.

**Codex is two executables.** The release publishes `codex-code-mode-host` as its own asset, and Codex looks for it beside `codex`. Without it the model used under subscription sign-in could not run any command: it answered `pong` without having executed anything, and the run exited 0 ([`debian-codex-subscription-without-code-mode-host.txt`](results/02-run-matrix/debian-codex-subscription-without-code-mode-host.txt)). `gpt-5-mini` with an API key ran its command either way.

**The images need nothing else.** `git` is absent from all three and a CA bundle from the two glibc images; both CLIs ran. A `HOME` mounted `noexec`, which is Docker's default for a tmpfs, works for both ([`debian-claude-api-key-home-noexec.txt`](results/02-run-matrix/debian-claude-api-key-home-noexec.txt), [`debian-codex-api-key-home-noexec.txt`](results/02-run-matrix/debian-codex-api-key-home-noexec.txt)).

**Schema-constrained output works in both CLIs, in both modes.**

| CLI | Flags | Where the result is | Evidence |
| --- | --- | --- | --- |
| Claude Code | `--json-schema '<schema>'` with `-p` | the `structured_output` field of the `result` event | [`debian-claude-api-key-schema.txt`](results/02-run-matrix/debian-claude-api-key-schema.txt), [`debian-claude-subscription-schema.txt`](results/02-run-matrix/debian-claude-subscription-schema.txt) |
| Codex | `--output-schema <file>`; `--output-last-message <file>` writes the final message | the last `agent_message` item, and the file | [`debian-codex-api-key-schema.txt`](results/02-run-matrix/debian-codex-api-key-schema.txt), [`debian-codex-subscription-schema.txt`](results/02-run-matrix/debian-codex-subscription-schema.txt) |

Every result had exactly the required keys with the required types. One Claude Code result put a JSON string inside the `answer` string: the schema constrains shape, not meaning.

**`codex exec` accepts a per-run reasoning-effort override (AR-8): `-c model_reasoning_effort="<value>"`.** In both authentication modes `low` and `high` are accepted, and an unknown value is passed on and refused by the service, which names the supported values: `none`, `minimal`, `low`, `medium`, `high`, `xhigh`, `max`. That shows the value reaches the request. With an API key the recorded run reported 0 reasoning tokens at `low` and 64 at `high`; an earlier repeat of the same one-word prompt reported 0 at both, and so did the subscription runs, so the size of the effect was not measured. Results: `debian-codex-<mode>-effort-<value>.txt`.

### 2. Planted configuration and account-linked tools

The test repository is generated in a container volume by [`plant-claude.sh`](scripts/container/plant-claude.sh) and [`plant-codex.sh`](scripts/container/plant-codex.sh). Each planted item leaves a marker file when it executes. The prompt makes the Agent run one shell command, so hooks around tool use and a planted environment variable are exercised. A control variant switches nothing off and proves the planted configuration is live.

Results per cell: [`results/03-planted-config/`](results/03-planted-config/), table in [`summary.md`](results/03-planted-config/summary.md). A cell counts only when its evidence is complete: the CLI started, the planted repository was in the workspace, and the Agent ran the probe command. The script fails when a control shows nothing live, when a hardened variant executed anything, or when a hardened cell's evidence is incomplete. All cells were complete. The outcome was the same in both authentication modes unless noted.

**Claude Code**

| Variant | Planted configuration that executed |
| --- | --- |
| control (no flags) | hooks from `.claude/settings.json` on `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `Stop`, `SessionEnd`; the `SessionStart` hook from `.claude/settings.local.json`; the project MCP server's command from `.mcp.json`; the planted environment variable; a planted `apiKeyHelper` command. No trust prompt. |
| `--bare` (API key only) | **the planted environment variable** |
| `--safe-mode` | **the planted environment variable** |
| `--safe-mode --setting-sources user` | none |
| `--setting-sources user --strict-mcp-config --settings '{"disableAllHooks":true,...}'` | none |
| hardened: `--safe-mode`, the flags of the two rows above, a tool allow-list, and the variables that switch account features off | none, also with the planted `apiKeyHelper` |
| hardened with `--bare` in place of `--safe-mode` (API key only) | none |

`--bare` and `--safe-mode` switch hooks and MCP servers off, but both still apply the `env` block of the repository's `.claude/settings.json`. `--setting-sources user` is what stops it: adding that one flag to `--safe-mode` left nothing. It works because the user's settings live in the throwaway `HOME`, which Hephaestus controls. The spike planted one inert variable. What a hostile `env` block could do with the variables Claude Code reads, such as the address requests are sent to, follows from the mechanism and was not demonstrated.

**Codex**

| Variant | Planted configuration that executed |
| --- | --- |
| control (`--trust --dangerously-bypass-hook-trust`) | hooks from `.codex/hooks.json` on `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `Stop`; the project MCP server's command from `.codex/config.toml`; the planted environment variable |
| default (no trust flag) | none: `codex exec` treats the project as untrusted and ignores its `.codex/` layer |
| hardened: project marked untrusted, user configuration and rules ignored, features off | none |
| hardened plus `-c project_doc_max_bytes=0` | none |

**Not shown either way.** The plugin planted for each CLI, declared by a marketplace inside the repository and enabled in the repository's settings, loaded in no run, the controls included; nor did Codex's `notify`, which it ignores in project configuration and says so. A variant that leaves no trace of them therefore shows nothing about them ([`summary.md`](results/03-planted-config/summary.md) lists these items). What can be said is that in these versions a repository did not get a plugin loaded by its settings alone in a non-interactive run. The spike has no run in which a repository's plugin executed, so it does not show that the hardened flags would stop one.

**Instructions, as distinct from configuration.** An instruction file or a skill is text; nothing runs unless the model chooses to act on it. The spike planted both and recorded when the model did.

| | Repository instruction file | Repository skill |
| --- | --- | --- |
| Claude Code, control | `CLAUDE.md` followed | not followed |
| Claude Code, every other variant | not followed | not followed |
| Codex, API key (`gpt-5-mini`), default and hardened | `AGENTS.md` followed | not followed |
| Codex, subscription, default and hardened | `AGENTS.md` followed | followed |
| Codex, hardened plus `project_doc_max_bytes=0` | not read | followed under subscription |

No switch that removes a repository's skills from Codex was found among those tried. Whether the model acts on one depends on the model.

**Tools linked to the account**

Claude Code lists what it offers in the `system/init` event of its stream.

| Claude Code | Tools listed | MCP servers listed |
| --- | --- | --- |
| API key, control | 24 built-in tools, among them `WebSearch`, `WebFetch`, `PushNotification`, `DesignSync`, `CronCreate` | the planted project server |
| subscription, control | the same and **`RemoteTrigger`**, which appears only under subscription sign-in | the planted project server; no account connector |
| either mode, hardened | `Bash`, `Edit`, `Read`, `Write` | none |
| API key, hardened with `--bare` | `Bash`, `Edit`, `Read` (`Write` was asked for and not offered) | none |

The hardened event still lists bundled skills, the built-in agents `Explore`, `general-purpose`, and `Plan`, three built-in plugins, and slash commands. With the allow-list there is no `Skill` and no `Task` tool to reach the first two, and Hephaestus writes the prompt, so no slash command is sent; the spike did not try to reach them another way.

No connector of the Owner's Claude account was listed in any run, including the control. The vendor documents the token from `claude setup-token` as able to make model requests only, which would explain it; the spike did not test another kind of subscription credential or confirm that the account has connectors attached. The hardened invocation also sets `ENABLE_CLAUDEAI_MCP_SERVERS=false` and `disableClaudeAiConnectors`.

Codex does not list its tools in its stream. The nearest evidence is the model's own account of what it can call ([`codex-*-tool-inventory.txt`](results/03-planted-config/)), which is informative and not proof:

| Codex | What the model reported beyond its shell, patch, and goal tools |
| --- | --- |
| API key, default | web search, sub-agents |
| API key, hardened | nothing more |
| subscription, default | web search, image generation, plugin installation, MCP resource access, collaboration with other agents |
| subscription, hardened | MCP resource access and collaboration with other agents remain, although `multi_agent` was switched off |

No tool of a connected ChatGPT app appeared in either list; the account may have none connected. An API key belongs to no ChatGPT account, so there are no account connectors for it to reach; what remains for it is what the key itself may do.

**A limit of this criterion.** Switching tools off limits what the Agent is offered. It does not limit what code running in the container can do with the credential the container holds. An API key can do what the key allows. The `setup-token` credential is documented as limited to model requests. A Codex `auth.json` is a full ChatGPT account credential. This, with the refresh question, is why Codex subscription sign-in stays unsupported. In no subscription run did Codex rewrite the credential file (17 runs, recorded in each result), but its access token was fresh; that does not settle what happens when it expires.

### 3. The proxy is the only way out

Topology ([`topology.txt`](results/04-proxy/topology.txt)): the workspace network is a Docker internal network; the proxy container is attached to it and to a network with outbound access, runs as an unprivileged user with all capabilities dropped, and has no mount and no Docker socket.

From a step container without the proxy variables ([`isolation-no-proxy-variables.txt`](results/04-proxy/isolation-no-proxy-variables.txt)): a public name does not resolve (`SERVFAIL`), a public address and a link-local address are `Network unreachable`, there is no default route, and the bridge's own address refuses the connection.

Each CLI then made the same model request, with one tool call, with the generic proxy variables (`HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY`, and their lower-case forms) and without them ([`summary.md`](results/04-proxy/summary.md)):

| CLI and mode | With the variables | Without them | Hosts asked of the proxy |
| --- | --- | --- | --- |
| Claude Code, API key | succeeds | fails | `api.anthropic.com` |
| Claude Code, subscription | succeeds | fails | `api.anthropic.com` |
| Codex, API key | succeeds | fails (`failed to lookup address information`) | `api.openai.com`, `chatgpt.com`, `ab.chatgpt.com` |
| Codex, subscription | succeeds | fails (`workspace routing discovery failed`) | `chatgpt.com`, `ab.chatgpt.com` |

All hosts on port 443. Both CLIs honor the generic variables with no CLI-specific setting. These hosts are what one short run asked for: a starting point for the list an adapter declares per authentication mode (AD-10), not a proof that the list is sufficient.

- Without the proxy neither CLI gave up within the 60-second deadline of these cells. Claude Code was on attempt 7 of a stated maximum of 10, with growing delays. Codex reported five reconnection attempts and then waited for the network, stating no limit.
- Claude Code with nothing switched off also asks for `http-intake.logs.us5.datadoghq.com` (telemetry). `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` in the hardened invocation removes it.
- The `setup-token` credential needed neither `claude.ai` nor `platform.claude.com`, which the vendor lists for sign-in and token refresh ([network configuration](https://code.claude.com/docs/en/network-config), read 2026-11-25).
- Codex asks for `chatgpt.com` even with an API key and with apps and plugins off, and for `ab.chatgpt.com` in both modes. The spike did not establish what the second is for or whether Codex works when it is refused.
- The proxied runs used the Debian-based image only.

### 4. Package managers behind the proxy

One small dependency restored in the hardened container on the internal network, in stock toolchain images ([`summary.md`](results/05-package-managers/summary.md)):

| Manager | Generic variables alone | What the Project image must carry | Hosts used |
| --- | --- | --- | --- |
| npm 11.19 | works | nothing | `registry.npmjs.org` |
| `dotnet restore` (SDK 10.0.404) | works | nothing | `api.nuget.org`, and `crl3.digicert.com` over plain HTTP |
| pip 26.2 | works | nothing | `pypi.org`, `files.pythonhosted.org` |
| cargo 1.100 | works | nothing | `index.crates.io`, `static.crates.io` |
| go 1.25 | works | nothing | `proxy.golang.org`, `sum.golang.org` |
| Gradle 9.9 | **fails** | the proxy as JVM system properties, for example `JAVA_TOOL_OPTIONS="-Dhttps.proxyHost=<host> -Dhttps.proxyPort=<port> -Dhttp.proxyHost=<host> -Dhttp.proxyPort=<port>"` | `repo.maven.apache.org` |
| Maven 3.10 | **fails**, also with the JVM properties | a `<proxy>` element in `settings.xml` | `repo.maven.apache.org` |

For Maven the run wrote the element to `~/.m2/settings.xml` inside the throwaway `HOME`, which shows the element works. A Project image cannot use that place, because `HOME` is replaced in every step container; it would carry the file in `$MAVEN_HOME/conf/settings.xml`, which the spike did not run. Both JVM tools name the proxy by host and port. An image can only carry that if the proxy's address is fixed and documented.

### 5. Vendor terms

Full reading with quotations: [`results/06-terms/vendor-terms.md`](results/06-terms/vendor-terms.md).

| Vendor | Fetching the binary from the official channel at startup | Redistributing it in an image |
| --- | --- | --- |
| Anthropic, Claude Code | **Permitted.** The vendor documents direct download of a pinned version from `downloads.claude.ai/claude-code-releases`, verified against a signed manifest, and `DISABLE_UPDATES` "when you distribute Claude Code through your own channels". | **Not permitted without an agreement.** "All rights reserved"; no licence to redistribute. |
| OpenAI, Codex CLI | **Permitted.** Apache-2.0; GitHub Releases is the official channel. | Permitted under the Apache-2.0 conditions. |

Conditions that bind Hephaestus for Claude Code: the binary stays unmodified, and no authentication method built into it is removed, disabled, or restricted.

**The risk left open for Claude subscription sign-in.** The vendor says plan limits "assume ordinary, individual usage" and reserves enforcement "without prior notice". It forbids developers to "collect, store, or intermediate" Claude.ai credentials, and does not say whether a self-hosted tool that keeps its Owner's own token in the Owner's own installation is doing that. An API key raises neither question. This was already a recorded policy risk (FR-25); the spike changes nothing about it and the mode is "supported" in the technical sense only.

The spike verified each download against the checksum the vendor publishes. It did not verify the GPG signature of Anthropic's manifest or the sigstore bundle of the Codex release, which the startup fetch should.

## The invocations that held

What the adapters can start from, exactly as every hardened cell ran them ([`lib.sh`](scripts/lib.sh), variant `hardened`).

**Claude Code, both modes**

```sh
claude -p "<prompt>" --output-format stream-json --verbose \
  --dangerously-skip-permissions --no-session-persistence --model <model> \
  --safe-mode --setting-sources user --strict-mcp-config \
  --settings '{"disableAllHooks":true,"disableClaudeAiConnectors":true,"enableAllProjectMcpServers":false}' \
  --tools Bash,Read,Edit,Write
```

with `DISABLE_AUTOUPDATER=1`, `DISABLE_UPDATES=1`, `ENABLE_CLAUDEAI_MCP_SERVERS=false`, `CLAUDE_CODE_ENABLE_CFC=0`, `CLAUDE_CODE_DISABLE_ARTIFACT=1`, `CLAUDE_CODE_DISABLE_AGENT_VIEW=1`, `CLAUDE_CODE_DISABLE_CRON=1`, `CLAUDE_CODE_DISABLE_OFFICIAL_MARKETPLACE_AUTOINSTALL=1`, `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1`, and one of `ANTHROPIC_API_KEY` or `CLAUDE_CODE_OAUTH_TOKEN`.

**Codex, API key**

```sh
codex exec --json --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox --ephemeral \
  --model <model> --ignore-user-config --ignore-rules \
  -c 'projects."/workspace".trust_level="untrusted"' -c 'web_search="disabled"' \
  --disable hooks --disable plugins --disable remote_plugin --disable plugin_sharing \
  --disable apps --disable browser_use --disable browser_use_external --disable computer_use \
  --disable image_generation --disable memories --disable multi_agent \
  --disable skill_mcp_dependency_install \
  "<prompt>"
```

with `CODEX_API_KEY`, `CODEX_CA_CERTIFICATE=/hephaestus/agents/certs/ca-certificates.crt`, and `codex-code-mode-host` beside `codex`. Adding `-c 'project_doc_max_bytes=0'` keeps a Project's `AGENTS.md` from the Agent; it ran in the planted-configuration cells only, and whether it belongs in the invocation is a product choice.

## Findings the architecture has to absorb

None was applied to the spine, an ADR, or the threat model by this story. Each names the stories that depend on it; a change to the spine goes through `bmad-correct-course` before the first of those stories starts.

| # | Finding | Touches | Blocks |
| --- | --- | --- | --- |
| 1 | **`--bare` or `--safe-mode` alone leaves a hole.** Both apply the `env` block of the repository's settings. The Claude Code adapter must also send `--setting-sources user`. The ADR and the threat model should name the flag. | AD-7, T14 | 3.8, 3.21 |
| 2 | **One Claude Code invocation serves both modes.** `--safe-mode` works with an API key and keeps the `Write` tool, which `--bare` did not offer. The adapter need not choose the invocation by authentication mode. | AD-10 | 3.8, 3.10 |
| 3 | **CA bundle for Codex.** Either the Project image contract gains "CA certificates present", or Hephaestus places a bundle in the agent binaries volume and the Codex invocation sets `CODEX_CA_CERTIFICATE`. The second keeps the contract at "Linux, glibc or musl, `bash`" and is what the spike did, with the bundle of a stock Alpine image; where the product's bundle comes from and how it is refreshed is open. | AD-8, AD-10 | 3.1, 3.9 |
| 4 | **Two Claude Code builds per architecture.** The lock needs the glibc and the musl build; the runtime must detect the image's C library and select the matching one. A wrong choice is an immediate start failure. | AD-8 | 3.1, 3.2 |
| 5 | **Codex is two pinned files.** The lock needs `codex-code-mode-host` beside `codex`. Without it a model can be unable to run any command and still finish with exit 0 and a confident answer, which is the case AD-13 exists for: only Validation and the recorded tree decide. | AD-8, AD-13 | 3.1, 3.9 |
| 6 | **The lock should pin each executable, not only each download.** For Codex the vendor publishes the digest of an archive; the extracted binary's digest differs and cannot be checked against it later. | AD-8 | 3.1 |
| 7 | **Task volumes must be mounted `volume-nocopy`.** Docker fills an empty volume from the image on first use. With `/workspace` as the working directory, the directory Docker has just created as root is copied over the volume's root and the step user cannot write to its own workspace ([`debian-workspace-without-volume-nocopy.txt`](results/02-run-matrix/debian-workspace-without-volume-nocopy.txt)). | AD-6, AD-7 | 3.2, 3.5 |
| 8 | **The proxy's host name and port are a public contract.** Maven and Gradle need the address written into the Project image. Part of the decision the spine defers to the execution epic. | AD-7 | 3.4, installation documentation |
| 9 | **Hosts.** Codex: `api.openai.com`, `chatgpt.com`, and `ab.chatgpt.com` with an API key. Installation documentation should say that Codex contacts `chatgpt.com` even in API key mode. Projects that set an allow-list need their registry's hosts, and `dotnet restore` also fetches a certificate revocation list over plain HTTP. | AD-10 | 3.4, 3.9 |
| 10 | **Neither CLI stops soon when the network is gone.** The runtime's deadline is the stop that can be relied on. | AD-18 | 3.3, 3.16 |
| 11 | **The models Codex accepts differ by authentication mode.** `gpt-5-mini` ran with an API key and was refused under subscription sign-in ([`debian-codex-subscription-with-api-key-model.txt`](results/02-run-matrix/debian-codex-subscription-with-api-key-model.txt)). Should Codex subscription ever be supported, an Alias cannot resolve to one model for both modes. | AD-10 | 1.9, 3.9 |
| 12 | **The two CLIs treat the repository's instructions and skills differently.** Hardened Claude Code reads neither `CLAUDE.md` nor repository skills, and its tool allow-list removes the `Skill` and `Task` tools. Codex reads `AGENTS.md` unless told not to and offers repository skills regardless. | AD-12, AD-14 | 1.2, 3.11 |
| 13 | **Repository plugins are an open question.** No run loaded one, so nothing shows the hardened flags stop one. A test that first gets a repository plugin to load is owed before the security statement claims it. | AD-7, T14 | 3.21 |
| 14 | **The fallback has a licence limit.** A "Hephaestus base image" containing Claude Code could not be published by the project. Worth recording in ADR-0011 now that the fallback is not needed. | ADR-0011 | none |
| 15 | **For the usage-limit spike and the spend limit.** Every Claude Code run under subscription sign-in emitted a `rate_limit_event` with `status`, `resetsAt`, and `rateLimitType`; API key runs did not. Claude Code reports `total_cost_usd`; Codex reports token counts in `turn.completed` and no cost. | AD-20 | 7.1, 9.4 |

## How the spike was run

Everything runs in containers; nothing is installed on the host. The scripts need Docker and Bash 4.4 or later with the usual `base64`, `tar`, `sed`, and `grep`; Git Bash on Windows provides them. Every Docker object carries the label `hephaestus.spike=injection`.

```sh
cd docs/spikes/injection/scripts
bash 01-fetch-agents.sh       # download and verify the pinned CLIs into a volume (about 1.2 GB); run this first
bash 00-self-test.sh          # the harness checks itself; no credential
cp .env.example .env          # then fill in the credentials you have; .env is git-ignored
bash 02-run-matrix.sh         # CLI by image; schema output; effort override
bash 03-planted-config.sh     # planted configuration per authentication mode
bash 04-proxy.sh              # internal network, proxy, hosts
bash 05-package-managers.sh   # seven package managers (pulls about 4.5 GB of toolchain images)
bash 99-cleanup.sh            # remove everything the spike created
```

Afterwards delete `.env`, and revoke the keys and the `setup-token` if they were created for the spike.

`SPIKE_ONLY='<regular expression>'` repeats only the cells whose result file name matches. Scripts 04 and 05 share one proxy and read its log by time; do not run them at the same moment.

| File | What it does |
| --- | --- |
| [`scripts/lib.sh`](scripts/lib.sh) | The step container definition of AD-7 as one set of `docker run` arguments, the invocation variants, credential loading, masking, the result writer, and the checks on whether a cell can show anything. |
| [`scripts/agents.lock`](scripts/agents.lock) | Versions, platforms, checksums, and official URLs. |
| [`scripts/container/`](scripts/container/) | Scripts piped into containers: the two that plant configuration, and the wrapper that runs one Agent invocation and reports what executed. |
| [`scripts/images/`](scripts/images/), [`scripts/proxy/`](scripts/proxy/) | The three test images (stock base images; Alpine plus `bash`), a git-only tools image, and the proxy. |
| [`results/`](results/) | One file per cell: the harness version, the container's arguments, the command, its exit code, stdout, and stderr. |

Credentials are read from `.env` only: a credential already exported in the calling shell is cleared, not used. A cell whose credential is missing is recorded as `not-run` and the script goes on. Cells that need no credential use a dummy key, which the vendor refuses with 401.

Before a result is written, credential values are replaced, then anything shaped like a key or a token, and the subscription's `utilization` figure in rate-limit events. [`00-self-test.sh`](scripts/00-self-test.sh) checks this and the rest of the harness, 23 checks in all ([`self-test.txt`](results/00-self-test/self-test.txt)). Scripts 02 and 03 judge their own results and exit 1 when a claim of this report would no longer hold, so they can be repeated against a new pin.

The proxy is tinyproxy on Alpine. It stands in for the product's egress proxy, which is not chosen; it applies no allow-list and does not refuse private addresses.

## What the spike did not cover

- arm64. The lock file pins the arm64 builds; none was run.
- A native Linux Docker Engine, rootless Docker, and hosts with SELinux or AppArmor profiles. Everything ran on Docker Desktop with WSL2.
- The read-only context and Methodology mounts of a step container, and a time limit set by the runtime; the harness stops a run with `timeout`.
- The egress proxy's own rules: allow-lists, refusing private addresses, recording refusals, and keeping step containers on one network apart. Those belong to Story 3.4. IPv6 was not probed.
- Settings that can run a command other than those planted: `statusLine`, `otelHeadersHelper`, the cloud credential helpers, repository agents and commands; and for Codex, model providers and profiles in project configuration. The hardened invocation does not read the repository's settings at all, which is why they were not planted one by one.
- A repository plugin that actually loads.
- What the CLIs emit at a real usage limit (Story 7.1), and BMAD workflows (Story 1.2).
- OpenAI's service terms beyond the Codex licence.
- A Claude subscription credential other than the `setup-token` token, and an account with connectors known to be attached.
- Whether a Codex subscription credential can be refreshed safely between runs (spine Open Question 3).
- Longer runs. Every prompt was one line and at most one tool call.
