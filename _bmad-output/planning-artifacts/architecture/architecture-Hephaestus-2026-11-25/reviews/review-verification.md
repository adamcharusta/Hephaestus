# Verification review — Architecture Spine, Hephaestus

- **Reviewed:** `ARCHITECTURE-SPINE.md` (status draft, 2026-11-25), with `.memlog.md` and the technical research report as supporting evidence
- **Lens:** was every committed decision web-researched or reality-checked, and are the technical claims the ADs depend on true today
- **Date of checks:** 2026-11-25. Every source below was fetched live on that date unless marked otherwise.
- **Spine not edited.**

## Verdict

The Stack table is accurate to the patch level and every named technology exists and fits its use, but four decisions rest on claims that are only conditionally true or already out of date: bundle object checking (AD-9) silently does nothing on Git older than 2.46, Open Question 4 is already answered by the vendor documentation, the "schemas generated from Core record types" convention does not produce a schema Codex will accept without transformation, and the Codex subscription path conflicts with the "HOME is discarded" convention.

Legend: **VERIFIED** (source confirms), **QUALIFIED** (true, with a condition the spine does not state), **CONTRADICTED** (source says otherwise), **NOT VERIFIED** (no primary source found in this pass).

---

## 1. Findings that need action

### F1 — AD-9 "imports the bundle with object checking" is version-dependent, and the spine pins no Git version (CONTRADICTED on Git < 2.46)

- Before Git 2.46.0, `git fetch <bundle>` ignored `fetch.fsckObjects` and `transfer.fsckObjects` entirely. The upstream patch series states "there were no object validation supports for unbundle" and adds the check to `transport.c:fetch_refs_from_bundle`. Source: https://80x24.org/lore/git/pull.1730.v5.git.1718088126.gitgitgadget@gmail.com/
- Gitaly confirms the release: "Since Git 2.46.0, executing git-fetch(1) on a bundle performs fsck checks when `transfer.fsckObjects` is enabled"; before that "this configuration was always ignored and fsck checks were not run". It also reports that fsck message-severity configuration is ignored for bundle fetches in that version. Source: https://gitlab.com/gitlab-org/gitaly/-/merge_requests/7347
- Git 2.48 extended the check to `git bundle unbundle` and `git clone <bundle>` "with configurable fsck check levels". Source: https://raw.githubusercontent.com/git/git/master/Documentation/RelNotes/2.48.0.adoc
- Why this bites: .NET 10 container images default to Ubuntu 24.04 ("Debian container images will not be shipped for .NET 10"), and Ubuntu 24.04 packages Git 2.43.0. A `hephaestus` image built on the default base with `apt install git` would set the config and get no checking, with no error. Sources: https://github.com/dotnet/dotnet-docker/discussions/6801 and https://packages.ubuntu.com/noble/git
- Second caveat, from Git's own documentation of `transfer.fsckObjects`: fetch-side checking is not quarantined. "As objects are unpacked they're written to the object store, so there can be cases where malicious objects get introduced even though the 'fetch' failed, only to have a subsequent 'fetch' succeed because only new incoming objects are checked". Git recommends fetching untrusted objects into one repository and pushing (which does quarantine) into the one that is consumed. Source: https://raw.githubusercontent.com/git/git/master/Documentation/config/transfer.adoc
- **Correction for the spine:** add Git to the Stack table with a floor of 2.48 (2.46 at minimum) for the orchestrator image, and state the import as two-stage (scratch repository, then push into the mirror) or as an explicit `git index-pack --fsck-objects` pass. A startup check of `git --version` is cheap insurance.

### F2 — Open Question 4 is already answered by current Claude Code documentation (CONTRADICTED as an open question)

- The spine says it is unknown "whether repository hooks and MCP servers can be disabled in a non-bare run". The research report says subscription mode leaves the repository as executable configuration.
- The permissions documentation lists the controls for exactly this case: "Pass `--setting-sources user` ... so Claude Code reads neither the project's settings files nor its `.mcp.json`"; "Pass `--settings '{"disableAllHooks": true}'` to turn hooks off for that run. Setting it in your user settings alone isn't enough, because the repository's project settings take precedence"; and `disabledMcpjsonServers`. `--strict-mcp-config` is in the CLI reference. Sources: https://code.claude.com/docs/en/permissions (section "What runs before you trust a folder") and https://code.claude.com/docs/en/cli-reference
- The same table confirms the risk the research found: in a `claude -p` run, hooks, the `env` block and helper commands in project settings are "Used", and `.mcp.json` servers are "Connected without asking".
- **Correction:** close Open Question 4 and move the flags into the Claude Code adapter's invocation contract. The AD-7 stance (the container is the boundary) stays correct, because Validation commands execute repository code anyway.

### F3 — "Result schemas are generated from Core record types" is feasible, but the generated schema is not accepted by Codex as-is (QUALIFIED)

- The built-in exporter exists: "The `JsonSchemaExporter` class, introduced in .NET 9, lets you extract JSON schema documents from .NET types". Source: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/extract-schema
- Its default output for a record is `"type": ["object", "null"]` at the root, with `required` only for non-optional constructor parameters and no `additionalProperties` unless `UnmappedMemberHandling.Disallow` is set (same page).
- `codex exec --output-schema` is enforced under OpenAI strict Structured Outputs. Official rules: "the root level object of a schema must be an object, and not use `anyOf`"; "all fields or function parameters must be specified as `required`"; "we require developers to set `additionalProperties: false`"; `allOf`, `not`, `if/then/else` unsupported; at most 10 levels of nesting. Source: https://developers.openai.com/api/docs/guides/structured-outputs
- Third-party reports show the failure mode in Codex: an `error` event, then `turn.failed` with `invalid_json_schema`, exit code 1, before any generation. Sources: https://github.com/coleam00/Archon/issues/1843 and https://github.com/eyaltoledano/claude-task-master/issues/1522 (third-party, consistent with each other)
- Claude side: the Agent SDK "validates schemas with JSON Schema draft-07, so schemas that declare a newer version are rejected", and `format` is accepted but not enforced. Source: https://code.claude.com/docs/en/agent-sdk/structured-outputs
- The exporter only exports. AD-13 says "Core validates every result", which needs either a validator library or strict deserialization. `JsonSchema.Net 9.7.0` appears in the memlog's version sweep but not in the Stack table, and AD-3 forbids infrastructure packages in Core.
- **Correction:** state that the Core schema is the canonical form and each adapter normalises it for its CLI (AD-10 already allows this), set `TreatNullObliviousAsNonNullable` and `UnmappedMemberHandling.Disallow`, make every property required with nullability for optional values, and name how Core validates.

### F4 — Codex subscription sign-in conflicts with "HOME is discarded with the container" (QUALIFIED; turns Open Question 3 into a blocker for that mode)

- OpenAI's documented CI path for ChatGPT-account authentication is: "seed `auth.json` through secure storage, run Codex on the runner so Codex refreshes it in place" and then "persist the updated file between runs". It adds "Treat `~/.codex/auth.json` like a password" and "Do not use this workflow for public or open-source repositories." Source: https://learn.chatgpt.com/docs/non-interactive-mode
- The step-container convention discards `HOME` and AD-6 gives the container only a credential, with no write-back path. As written, Codex in subscription mode would lose its refreshed token.
- The same question is half-answered on the usage side: `turn.completed` carries a `usage` object with `input_tokens`, `cached_input_tokens`, `output_tokens` and `reasoning_output_tokens` (same page). That part of Open Question 3 can be closed.
- **Correction:** decide the write-back mechanism (or declare Codex API-key-only in v0.1) before the Codex adapter story, not during it.

### F5 — The vendor's "documented answer" for Codex in a container carries a caveat AD-6 and AD-7 do not state (QUALIFIED)

- Confirmed: "If the container is your intended security boundary, run Codex with `--sandbox danger-full-access` inside the container", "so Codex does not try to create a second sandbox layer." Linux "uses `bwrap` plus `seccomp` by default"; "starting in `0.115`, the Linux sandbox moved to `bwrap`".
- The same page continues: "a malicious project can exfiltrate anything available inside the devcontainer, including Codex credentials" and "Use this pattern only with trusted repositories". Source: https://learn.chatgpt.com/docs/agent-approvals-security.md
- The memlog states the sandbox "fails under Docker's default seccomp profile" as fact. The documentation says only that it "may not work if the host or container configuration blocks the namespace, setuid `bwrap`, or `seccomp`" operations. The unconditional version comes from third-party guides.
- **Correction:** the threat model must own Agent-credential exfiltration by repository content as an accepted risk (NFR-4's egress allowlist is the mitigation and is currently deferred). Soften "fails" to "is not relied on".

### F6 — Withholding the `workflows` permission blocks the whole push, and does not stop existing workflows (QUALIFIED)

- Confirmed that GitHub rejects such pushes at the git server with "refusing to allow a GitHub App to create or update workflow ... without `workflows` permission". Sources: https://github.com/orgs/community/discussions/35410 and https://github.com/orgs/community/discussions/27224 (GitHub community; no docs.github.com page was found stating the rule)
- Consequence not in the spine: if an Agent edits any file under `.github/workflows`, every later checkpoint push for that Task is rejected, which collides with AD-9 ("after every step ... pushes it") and FR-20. The rejection needs a failure class and an Escalation path.
- The mitigation is partial. Pushes made with a GitHub App installation token do trigger workflows: "you can use a GitHub App installation access token or a personal access token instead of `GITHUB_TOKEN`" to create runs. Workflows already on the Base Branch therefore run Task-branch code with repository secrets. Source: https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow

### F7 — System.CommandLine 3.0.0 contradicts the spine's own rationale for staying on .NET 10 (VERIFIED version, flagged choice)

- 3.0.0 is real and stable, published 2026-11-10, targeting net10.0 and netstandard2.0. Source: https://www.nuget.org/packages/System.CommandLine/3.0.0
- It is the .NET 11-wave release: its previews are numbered in lockstep with EF Core 11 (`3.0.0-rc.3.26522.148` beside `11.0.0-rc.3.26522.148`). The 2.0.x line is the one serviced with .NET 10 (2.0.15 published 2026-11-10, the same day as 10.0.15). Sources: https://api.nuget.org/v3-flatcontainer/system.commandline/index.json and https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.sqlite/index.json
- The memlog rejects .NET 11 because it is "two weeks old, and coding agents know it less well", and picks EF Core 10.0.15 over 11.0.0 on that basis. System.CommandLine 3.0.0 is equally new. Either choose 2.0.15 or record why this one package is the exception.

### F8 — Octokit 14.0.0 is the latest release, and it is 22 months old (VERIFIED version, staleness flag)

- Latest release v14.0.0, published 2025-01-08. The repository is not archived and was last pushed 2026-09-14. The package targets only netstandard2.0. Sources: https://api.github.com/repos/octokit/octokit.net/releases/latest, https://api.github.com/repos/octokit/octokit.net, https://www.nuget.org/packages/Octokit
- GitHub App support is real but incomplete: `GitHubApps.CreateInstallationToken(...)` exists, and the docs say "we do not curently have the ability to generate/sign JWT tokens for you". Source: https://octokitnet.readthedocs.io/en/latest/github-apps/
- The Stack table names no JWT approach. `Microsoft.IdentityModel.JsonWebTokens 8.23.0` is in the memlog's version sweep only. Hand-signing an RS256 JWT with `System.Security.Cryptography` is a valid zero-dependency alternative; the spine should say which.
- Conditional requests are supported through `Octokit.Caching.CachingHttpClient`, which sets `If-None-Match` from a cached ETag and returns the cached response on 304, for GET only. The `IResponseCache` implementation must be supplied by the application. Source: https://raw.githubusercontent.com/octokit/octokit.net/main/Octokit/Caching/CachingHttpClient.cs
- Not checked: whether 14.0.0 models every endpoint the gateway needs.

---

## 2. Stack table

| Row | Spine says | Result | Evidence |
| --- | --- | --- | --- |
| .NET (LTS) | 10.0, runtime 10.0.15 | VERIFIED | Support policy page, last updated 2026-11-10: .NET 10 LTS, released 2025-11-11, latest patch 10.0.15 dated 2026-11-10, end of support 2028-11-14. .NET 11 STS, GA 2026-11-10, same end date. .NET 8 and 9 out of support. https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core |
| C# | 14 | VERIFIED | ".NET 10.x: C# 14"; "C# 15 is supported only on .NET 11 and newer". https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning |
| EF Core SQLite | 10.0.15 | VERIFIED | Latest 10.x is 10.0.15; 11.0.0 also exists. https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.sqlite/index.json |
| Octokit | 14.0.0 | VERIFIED, see F8 | https://api.nuget.org/v3-flatcontainer/octokit/index.json |
| Docker.DotNet.Enhanced | 4.4.1 | VERIFIED | Published 2026-11-14; net8.0, net9.0, net10.0, netstandard2.0/2.1; "A .NET client for the Docker Engine API (v29.8.0)"; MIT; maintained under `testcontainers/Docker.DotNet`. https://www.nuget.org/packages/Docker.DotNet.Enhanced |
| System.CommandLine | 3.0.0 | VERIFIED, see F7 | https://www.nuget.org/packages/System.CommandLine/3.0.0 |
| YamlDotNet | 18.1.0 | VERIFIED | https://api.nuget.org/v3-flatcontainer/yamldotnet/index.json |
| xunit.v3 | 4.0.3 | VERIFIED | Published 2026-11-09; targets net8.0 and net472; depends on `xunit.v3.mtp-v2 = 4.0.3`. https://www.nuget.org/packages/xunit.v3/4.0.3 |
| NSubstitute | 6.2.0 | VERIFIED | https://api.nuget.org/v3-flatcontainer/nsubstitute/index.json |
| Testcontainers | 4.17.0 | VERIFIED | Published 2026-11-16; requires Docker.DotNet.Enhanced >= 4.3.3, so it is compatible with 4.4.1 in the same test project. https://www.nuget.org/packages/Testcontainers/4.17.0 |
| Docker Engine | 29.x | VERIFIED | Current line is 29; latest 29.10.1 dated 2026-11-24. https://docs.docker.com/engine/release-notes/ |
| Claude Code CLI | 2.1.x | VERIFIED | npm `latest` is 2.1.332. https://registry.npmjs.org/@anthropic-ai/claude-code/latest |
| Codex CLI | 0.171.0 or later | VERIFIED, already superseded | 0.171.0 published 2026-11-20; 0.172.0 published 2026-11-24. https://api.github.com/repos/openai/codex/releases/latest |
| Git | absent | GAP | See F1. |

Docker.DotNet.Enhanced capability check (interface source, `main` branch):

- `IContainerOperations` declares `CreateContainerAsync`, `StartContainerAsync`, `WaitContainerAsync`, `GetContainerLogsAsync` (stream and progress overloads), `AttachContainerAsync`, `GetArchiveFromContainerAsync`, `ExtractArchiveToContainerAsync`, `KillContainerAsync`, `StopContainerAsync`, `RemoveContainerAsync`. https://raw.githubusercontent.com/testcontainers/Docker.DotNet/main/src/Docker.DotNet/Endpoints/IContainerOperations.cs
- `IVolumeOperations`: `CreateAsync`, `InspectAsync`, `ListAsync`, `RemoveAsync`, `PruneAsync`. `INetworkOperations`: `CreateNetworkAsync`, `ConnectNetworkAsync`, `DisconnectNetworkAsync`, `DeleteNetworkAsync`, `ListNetworksAsync`. Same repository, `IVolumeOperations.cs` and `INetworkOperations.cs`.
- The README notes `WaitContainerAsync` and `GetContainerLogsAsync` have an infinite timeout, which matters for AD-6's runtime-enforced time limits: the runtime must cancel, not rely on a client timeout. https://github.com/testcontainers/Docker.DotNet

---

## 3. Claims the ADs depend on

### AD-5 — effect lookup by natural key

- **Pull request by head branch: VERIFIED.** `GET /repos/{owner}/{repo}/pulls` takes `head`: "Filter pulls by head user or head organization and branch name in the format of user:ref-name or organization:ref-name", and `state` (`open`, `closed`, `all`; default `open`). The lookup must pass `state=all` and the `owner:` prefix. https://docs.github.com/en/rest/pulls/pulls
- **Comment and issue by hidden marker: VERIFIED** that HTML comments are hidden from rendered Markdown. https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax
- **Container and volume by Docker label: VERIFIED for volumes** ("The `label` filter matches volumes based on the presence of a `label` alone or a `label` and a value"). https://docs.docker.com/reference/cli/docker/volume/ls/
- **`workflows` permission: VERIFIED in substance, see F6.**

### AD-7 and AD-8 — agent CLIs in containers

- **Claude Code refuses bypass mode as root: VERIFIED.** "On Linux and macOS, Claude Code refuses to start in `bypassPermissions` mode when running as root or under `sudo`". The same page adds "The check is skipped automatically inside a recognized sandbox", and for unattended container runs "on Linux and macOS, run it as a non-root user". https://code.claude.com/docs/en/permission-modes
- **Native binary with glibc and musl builds: VERIFIED.** Platforms `linux-x64`, `linux-arm64`, `linux-x64-musl`, `linux-arm64-musl`; "the package downloads a native binary that doesn't use your Node.js at runtime"; Ubuntu 20.04+, Debian 10+, Alpine 3.19+; "Claude Code itself requires `bash` or `zsh`". https://code.claude.com/docs/en/setup
- **Updates can be disabled: VERIFIED.** "`DISABLE_AUTOUPDATER` only stops the background check ... To block all update paths, including manual updates, set `DISABLE_UPDATES` instead. Use this when you distribute Claude Code through your own channels". Pinned-version install and a GPG-signed `manifest.json` with SHA256 per platform binary are documented on the same page.
- **Relevant to the injection spike:** "A running session reads parts of the Claude Code executable from disk as it works, not only at startup." The agent-binaries volume must not be swapped while a step runs (same page).
- **Codex ships musl binaries: VERIFIED.** README lists `codex-x86_64-unknown-linux-musl.tar.gz` and `codex-aarch64-unknown-linux-musl.tar.gz`; the 0.172.0 release also ships `bwrap-*-linux-musl` assets. https://raw.githubusercontent.com/openai/codex/main/README.md — "statically linked" is NOT VERIFIED (see section 5).
- **`danger-full-access` is the documented answer in a container: VERIFIED, see F5.**
- **Stale flag to avoid:** "`--full-auto` is no longer supported" for `codex exec`. https://learn.chatgpt.com/docs/non-interactive-mode

### AD-9 — bundle boundary

- **A bundle carries a branch between repositories: VERIFIED.** "Bundles are used for the 'offline' transfer of Git objects"; `git bundle create file.bundle master`; `git fetch mybundle master:localRef`; `git ls-remote` works on a bundle. https://git-scm.com/docs/git-bundle
- **Running git in an untrusted repository is a code-execution risk: VERIFIED.** "it is not safe to run Git commands in a `.git` directory (or the working tree that surrounds it) when that `.git` directory itself comes from an untrusted source. The commands in its config and hooks are executed in the usual way." https://git-scm.com/docs/git#_security
- **Object checking on import: see F1.**
- **Observation outside strict verification:** the checkpoint commit is made by git inside the agent-writable repository, so the repository's hooks and config run there. That container is credential-free, which is correct, but a `commit-msg` hook can alter or strip the trailers. The gateway should verify trailers on the imported tip and not assume them.

### AD-13 — schema-constrained output

- **`claude --json-schema`: VERIFIED.** "use `--output-format json` with `--json-schema` ... with the structured output in the `structured_output` field"; "If a run ends without valid structured output ... the response has no `structured_output` field". The failure subtype is `error_max_structured_output_retries`. https://code.claude.com/docs/en/headless and https://code.claude.com/docs/en/agent-sdk/structured-outputs
- **`codex exec --output-schema`: VERIFIED.** "use `--output-schema` to request a final response that conforms to a JSON Schema"; `-o`/`--output-last-message` writes it to a file. https://learn.chatgpt.com/docs/non-interactive-mode
- **Schema compatibility between the two: see F3.**

### AD-17 — local API on a Unix domain socket

- **Kestrel: VERIFIED.** `serverOptions.ListenUnixSocket("/tmp/kestrel-test.sock")`. https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0
- **HttpClient: VERIFIED.** `SocketsHttpHandler.ConnectCallback` ("Gets or sets a custom callback used to open new connections") applies to .NET 5 through 11; the callback returns a `NetworkStream` over a `UnixDomainSocketEndPoint`. https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0 and worked example at https://andrewlock.net/using-unix-domain-sockets-with-aspnetcore-and-httpclient/
- Kestrel does not remove a stale socket file after a crash; startup should delete it. This is from general knowledge, not checked in this pass.

### Conventions and other claims

- **UUID version 7: VERIFIED.** `Guid.CreateVersion7()` and `CreateVersion7(DateTimeOffset)`, .NET 9 and later; the second overload fits the `TimeProvider` rule. https://learn.microsoft.com/en-us/dotnet/api/system.guid.createversion7?view=net-10.0
- **Polling with conditional requests: VERIFIED.** A conditional request "does not count against your primary rate limit if a `304` response is returned", when made "with an `Authorization` header". https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api
- **Actor on `labeled` events (AD-15): VERIFIED.** `actor`: "The person who generated the event", available in the issue events and timeline APIs. https://docs.github.com/en/rest/using-the-rest-api/issue-event-types

---

## 4. Likely out of date or drifting

| Item | Why |
| --- | --- |
| Research sources [7], [8], [9] | `developers.openai.com/codex/*` now answers 308 to `learn.chatgpt.com/docs/*`. The citations still resolve but the pages were read through summaries and have moved. |
| "Claude Code 2.1.x" as a compatibility range | Behaviour changes inside the line: "With Claude Code v2.1.302 or later, `claude -p` runs start in Manual mode when nothing sets a permission mode"; "`--bare` ... will become the default for `-p` in a future release". Only the exact pin (which the spine does require) is meaningful, and the adapter must pass every mode flag explicitly. https://code.claude.com/docs/en/headless |
| "Codex 0.171.0 or later" | Two releases in five days (0.171.0 on 2026-11-20, 0.172.0 on 2026-11-24). A floor is not a pin; the spine's "exact version pinned per release" is the operative rule. |
| Research recommendation 4 and cross-dimension insight 1 | Superseded by F2. |
| Research section 2, "Whether Codex's own sandbox functions inside a Docker container was not established" | Now established by the vendor page in F5. |
| Octokit | See F8. |
| Research staleness map | Earliest re-check 2026-12-25 for both CLIs. Still in date. |

## 5. Could not verify

1. That the Codex Linux binaries are statically linked and need nothing from the image. The README names musl targets and does not say "static".
2. That Codex's sandbox fails under Docker's default seccomp profile as an unconditional fact. The vendor page says "may not work"; only third-party guides state it flatly.
3. That Codex self-update or its startup update check can be disabled as AD-8 requires. `check_for_update_on_startup = false` is reported by a maintainer reply in https://github.com/openai/codex/issues/3855 and third-party posts; no official page was fetched.
4. That `codex exec` accepts a per-run `-c model_reasoning_effort=...`. The CLI page confirms `-c key=value` overrides in general; its options table did not render.
5. That `--json-schema` works together with `--output-format stream-json`. The documentation shows it with `json`; the SDK result message carries `structured_output`, so it is likely.
6. An official docs.github.com statement of the `workflows` permission rule (found only in GitHub community discussions and third-party issues quoting the server message).
7. What GitHub returns when a pull request already exists for the head branch. The create endpoint documents only a generic 422.
8. That the Docker archive endpoints copy to and from a path on a named volume in a created or stopped container. Docker's `cp` reference says copying "mounts created by the user in the container" is not possible, which is ambiguous for declared volumes. Add to the injection spike. https://docs.docker.com/reference/cli/docker/container/cp/
9. That both CLIs run from a read-only mount as UID 10001 with no passwd entry and a throwaway `HOME`. The spine already holds this as Open Question 1.
10. That `fetch.fsck.<msg-id>` severities apply to `git fetch <bundle>` on Git 2.48 and later (Gitaly reports they did not on 2.46).
11. That Octokit 14.0.0 covers every endpoint the gateway needs (timeline events, reviews, labels, pull request listing by head).
12. Label filters for containers and networks specifically (verified for volumes only).

## 6. How well the run's own verification holds up

- The memlog's two `(version)` entries are accurate: every NuGet version, the .NET support dates, and the Docker.DotNet.Enhanced details match what was fetched today.
- The memlog's Claude Code distribution note ("fetched in full") matches the setup page.
- The memlog's Codex note is honestly labelled "medium confidence, via search summaries" and holds in substance, with the over-statement noted in F5.
- No entry in the memlog shows a check of Git's bundle behaviour, of schema acceptance by either CLI, of Octokit's App or caching support, or of the `workflows` rule's side effects. Those four are where the findings above cluster: the decisions were reasoned, not reality-checked.
- The research report declares 4 claims verified and 13 unverified, with sources [4] and [7] to [20] read through search summaries. The spine builds AD-7, AD-8 and AD-13 on that set. This review fetched the primary pages for most of them and they hold, except where noted.
