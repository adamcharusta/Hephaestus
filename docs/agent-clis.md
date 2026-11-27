# Agent CLIs: pinned, fetched, and verified

Story 3.1, AD-8, ADR-0011, AR-40. Hephaestus drives two Agent CLIs, Claude Code and Codex CLI. A release of Hephaestus works with exactly the versions it was
tested with, and neither CLI is baked into an image: Claude Code is proprietary and may not be redistributed, and a Project names its own image, which must need
no Agent installed. So the `hephaestus` image carries a lock file and no Agent binary, and the host fetches the pinned executables from the vendors at start.

## The lock

`deploy/agents.lock` has one line per executable, with white-space separated columns:

```text
agent  version  arch  libc  file  sha256  url  [archive-sha256]
```

| Column | Meaning |
| --- | --- |
| `agent` | `claude-code` or `codex-cli` |
| `version` | one version per Agent, the same on every line of that Agent |
| `arch` | `x64` or `arm64` |
| `libc` | `glibc` or `musl` where the vendor builds per C library (Claude Code); `static` for a statically linked build (Codex CLI) |
| `file` | the executable's name: `claude`, or `codex` and `codex-code-mode-host` |
| `sha256` | the digest of the executable itself, which the host checks at every start |
| `url` | where the vendor publishes it, https only |
| `archive-sha256` | when the url is a `.tar.gz` holding the one executable (Codex CLI): the digest the vendor publishes for the archive |

A lock that omits an executable, names one twice, gives an Agent two versions, or has a value that is not valid is refused at start. The message names the executable that is missing, or the line that is not valid. A version is three numbers, `2.1.332`; a pre-release is not pinned.
Claude Code is one executable in four builds (glibc and musl, for each architecture). Codex CLI is two executables for each architecture: `codex`, and
`codex-code-mode-host`, which `codex` looks for beside itself and without which a model may be unable to run a command and still finish with a confident answer.
For Codex the lock pins the archive and the executable taken out of it, because an archive's digest cannot check a file after it is extracted.

## What happens at start

The host starts in this order (AR-40): it migrates the database, then prepares the agent binaries volume, then starts the loop and the poller. Nothing else runs
until the volume is ready.

1. The host reads the lock the image carries, `agents.lock` beside the executable (`HEPHAESTUS_AGENTS_LOCK`), and takes the lines for its own architecture.
2. For each executable it hashes the file at `<agents volume>/<agent>/<arch>-<libc>/<file>`, for example `claude-code/x64-musl/claude`. A file with the pinned digest is left
   alone and **nothing is downloaded**.
3. Anything else, a missing file, a file with another digest, or a link, is removed and downloaded again from the lock's url into a staging directory inside the volume. The
   download is checked against the vendor's digest (the archive's, for Codex), the executable is taken out of the archive and checked against its own digest, and only then is it
   moved into place. A download that does not match stops startup with a message that names the executable, the address, and both digests; **nothing unverified is left in
   the volume**, and neither is the file that was there before.
4. Once the volume is filled, every executable the lock names must be present with its digest, or startup stops naming the file. Everything in the volume that the lock does not name is
   removed.
5. The CA certificate bundle of the `hephaestus` image's own store (`/etc/ssl/certs/ca-certificates.crt`, `HEPHAESTUS_CA_BUNDLE`) is copied to `certs/ca-certificates.crt`, replacing the
   file there, at every start. Codex CLI needs one, and a Project's image need not hold any.

A host that cannot reach the vendors' download hosts on first start stops with the failed download named; it starts when they can be reached. They are `downloads.claude.ai` for Claude Code and,
for Codex CLI, `github.com`, which redirects each download to `release-assets.githubusercontent.com`; an installation behind an allow-list of hosts needs all three. A stopped host is a stopped container,
and Compose's restart policy (`unless-stopped`) starts it again, which downloads again what is still missing: read the message with `docker compose logs hephaestus` and fix the cause, or run
`docker compose stop`, rather than leave it restarting. The first start can take minutes, and the container is `starting` in the meantime, because the control API answers only once the volume is ready. Executables are readable and
runnable by every user and writable by none, because a step container runs as a user of its own, with the volume mounted read-only. The volume is named `hephaestus-agents`.
It is a cache and holds no installation state.

## Changing a pin

A pin changes when Hephaestus moves to a newer version of a CLI. The adapter and the CLI are always the tested pair, so a change of pin is a change of what was tested.

1. Edit `deploy/agents.lock`: the new version, the new url, and every digest. Claude Code's checksums are in the release manifest,
   `https://downloads.claude.ai/claude-code-releases/<version>/manifest.json`. Codex CLI's archive digests are the `digest` of each asset of the GitHub release `rust-v<version>`, and
   an executable's digest is the SHA-256 of the file in the archive.
2. Run `python3 eng/verify-agents-lock.py` with `gpg` and `cosign` installed. It checks that every url is on the vendor's official channel at the pinned version; for Claude Code, that the
   manifest's GPG signature verifies against Anthropic's release key (fingerprint `31DD DE24 DDFA B679 F42D 7BD2 BAA9 29FF 1A7E CACE`) and holds the lock's checksums; for Codex CLI, that the archive
   digests are the published ones, that the executables are what the archives hold, and that each executable's sigstore signature was made by the release workflow of `openai/codex` at that
   tag. The same check is the `Agent CLI pins` job of GitHub Actions, which fails a pull request that changes the lock unless it passes. The host does not repeat it at start; there the
   digest in the lock is the authority.
3. Repeat the credentialed runs of [`docs/spikes/injection`](spikes/injection/README.md), the matrix and the planted-configuration runs, with the Owner's credentials. They show that the hardened
   invocations still stop what they stopped, and that the adapters still understand the CLI. **A change of pin therefore needs the Owner's credentials and is not closed by a dev agent alone.**
4. The `Image (linux/arm64)` and `Image (linux/amd64)` checks start each pinned executable in a hardened container on a runner of its own architecture (`eng/check-agent-binaries.sh`) and check that it prints the
   version the lock pins.

To run the arm64 check by hand, start Hephaestus once on an arm64 machine so that the volume is filled, then run `bash eng/check-agent-binaries.sh`.

## The Claude Code adapter

Story 3.8, AD-10, [ADR-0025](adr/0025-drive-claude-code-through-its-official-interface.md). `Hephaestus.Agents` holds the adapter of Claude Code. It describes an invocation, a probe, how to read a stored output stream, and how to read the end of a CLI that exited by itself. It starts no process and knows nothing of Docker; the runtime does both. It is released with Claude Code 2.1.332, the version `deploy/agents.lock` pins, and a test fails when the two differ.

| Part | Value |
| --- | --- |
| Executable | `/hephaestus/agents/claude-code/claude`, whichever C library the Project's image has |
| Prompt | `Read /hephaestus/context/instruction.md and carry out the step it describes.`, the same for every Agent |
| Authentication | an API key (`anthropic-api-key` as `ANTHROPIC_API_KEY`) or a subscription token (`claude-code-oauth-token` as `CLAUDE_CODE_OAUTH_TOKEN`); the argument vector is the same, only the secret reference differs |
| Egress host | `api.anthropic.com`, in both modes |
| Effort | `--effort low`, `medium`, `high`, `xhigh`, or `max`; none given leaves the CLI's own |
| Result | the output stream: `structured_output` of the final `result` event when Core gave a schema, its `result` text otherwise |
| Failure class | `agent-error` when the stream names the error (`authentication_failed`, or a result ending that starts with `error_`); `unknown` for any other failure; never `usage-limit` (Story 7.1) |

The invocation switches off what a repository or the vendor account could run: `--safe-mode` (which the CLI describes as turning off `CLAUDE.md`, skills, installed plugins, hooks, MCP servers, and custom commands; the tests show a session-start hook, a prompt hook, an MCP server, and an `env` block, and nothing shows that it stops a repository's plugin), `--setting-sources user` so that the only settings are those of the throwaway `HOME`, `--strict-mcp-config`, `disableAllHooks`, `disableClaudeAiConnectors`, a closed list of tools (`Bash`, `Read`, `Edit`, `Write`),
the CLI's permission prompts (`--dangerously-skip-permissions`, because the container is the boundary), its self-update, and the traffic the vendor calls nonessential. It never sends `--bare`, `--resume`, or `--add-dir`.

`ClaudeCodePlantedConfigurationTests` starts the pinned CLI against a repository that plants hooks, an MCP server, and an `env` block, with no credential and no vendor call, and shows that none takes effect. It uses the executable named by `HEPHAESTUS_CLAUDE_CODE`, or else downloads the pinned one from the vendor and checks it against the lock; it is skipped where neither is possible, and fails in GitHub Actions.
The other tests read recorded streams of 2.1.332 (`tests/Hephaestus.Agents.Tests/Transcripts`): four are the stdout of the injection spike's runs, and the run with no credential was recorded from the pinned CLI with the adapter's invocation. They run no container and make no vendor call. A new pin needs its own transcripts, and the version test fails until they carry its version.

