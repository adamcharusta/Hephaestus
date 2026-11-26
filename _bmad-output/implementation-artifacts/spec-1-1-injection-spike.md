---
title: 'Story 1.1: Injection spike — Agent CLIs in a hardened container behind a proxy'
type: 'chore'
created: '2026-11-25'
status: 'done'
route: 'dispatch'
baseline_commit: 'aa40d45901f263a8162774d82e45f52b51876fdb'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Hephaestus-2026-11-25/ARCHITECTURE-SPINE.md'
  - '{project-root}/docs/adr/0011-agent-clis-pinned-and-injected.md'
  - '{project-root}/docs/security/threat-model.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** AD-7 (hardened step container behind an egress proxy) and AD-8 (pinned Agent CLIs injected into any Project image) are conditional on evidence nobody has yet. Everything in Epic 3 is built on them.

**Approach:** Build a reproducible Docker harness under `docs/spikes/injection/` that fetches the pinned CLIs from the vendors' official channels and exercises them under the exact step-container definition, with planted repository configuration, behind a proxy on an internal network, together with seven package managers. Record the evidence in a report that states "confirmed" or "fallback: Hephaestus base image" for AD-7 and for AD-8.

## Boundaries & Constraints

**Always:**
- The Owner supplies every vendor credential and takes part in the credentialed runs. Credentials are read only from a git-ignored `.env`; no value is echoed, logged, committed, or written to a result file.
- A cell that needs a credential the Owner did not supply is recorded as not run, and its authentication mode stays unverified, which the report treats as unsupported.
- Pins: Claude Code 2.1.332 and Codex CLI 0.172.0, verified by SHA-256 against the vendor's published checksum before use.
- Containers use the AD-7 definition verbatim: user `10001:10001` with no passwd entry, all capabilities dropped, `no-new-privileges`, default seccomp, binaries read-only at `/hephaestus/agents`, throwaway `HOME` at `/hephaestus/home`, repository at `/workspace`, named volumes only.
- Every claim in the report cites a captured result file or a vendor source with its access date. Every result is reproducible by a script in the same directory.
- Model requests use the cheapest model and a one-line prompt.

**Never:**
- No change to the architecture spine, an ADR, the threat model, `SPEC.md`, or `deploy/`. A change the findings require is listed in the report and raised with the Owner.
- No live planted configuration in this repository's tree: hooks, MCP servers, settings, and plugins for the test repository are generated inside a container volume.
- The proxy used here does not decide the product's egress proxy; that choice stays deferred.
- No usage-limit testing (Story 7.1) and no BMAD workflow testing (Story 1.2).
- The story is not marked done by a dev agent alone.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Credential absent | `.env` lacks a mode's variable | Cells of that mode recorded `not-run`; all other cells run | Script continues and exits 0 |
| Checksum mismatch | Downloaded binary differs from the pin | Nothing is placed in the agents volume | Fetch exits non-zero and names the file |
| Planted configuration runs | A canary marker appears after a run | Cell recorded `executed`; the mode is marked unsupported | None; this is a finding |
| Request leaves without the proxy | No proxy variables, internal network | Recorded as a failed isolation check; AD-7 is not confirmed | None; this is a finding |
| CLI fails on an image | Non-zero exit or no stream | Exit code and output captured for that cell | Matrix continues with the next cell |

</frozen-after-approval>

## Code Map

- `docs/spikes/` -- does not exist; this story creates it. No application code exists in the repository.
- `_bmad-output/planning-artifacts/epics.md` (Story 1.1, lines 662-701) -- the seven acceptance criteria the report must answer one by one.
- `docs/adr/0011-agent-clis-pinned-and-injected.md` -- "What the spike must show"; do not edit.
- `docs/security/threat-model.md` -- T5, T14, and section 8 row "Disabled account tools and repository configuration"; do not edit.
- `.gitignore` -- already ignores `.env` and `.env.*` and keeps `.env.example`; reuse, do not change.
- `.gitattributes` -- forces LF on `*.sh`, `*.yaml`, `*.lock`, `Dockerfile`; scripts must be written with LF.
- Official channels, checked 2026-11-25: `https://downloads.claude.ai/claude-code-releases/<version>/manifest.json` lists a SHA-256 per platform (`linux-x64`, `linux-x64-musl`, `linux-arm64`, `linux-arm64-musl`); GitHub release `rust-v0.172.0` of `openai/codex` carries `codex-<arch>-unknown-linux-musl.tar.gz` with a digest.
- Host: Docker Engine 29.9.3 on Docker Desktop (WSL2, linux/amd64), Compose v5.7.1, Git Bash. Scripts must not depend on host bind-mount path translation.

## Tasks & Acceptance

**Execution:**
- [x] `docs/spikes/injection/scripts/agents.lock` -- record the pinned versions and SHA-256 per platform -- input for `deploy/agents.lock` later
- [x] `docs/spikes/injection/scripts/lib.sh` -- one shared hardened `docker run` definition, object names and labels, credential loading, result writer -- every cell runs the same container shape
- [x] `docs/spikes/injection/scripts/01-fetch-agents.sh` -- download, verify, and fill the agents volume from a container -- mirrors the startup fetch of AD-8
- [x] `docs/spikes/injection/scripts/images/` -- Dockerfiles for the Debian-, Ubuntu-, and Alpine-based test images, limited to the Project image contract plus what a finding shows is required
- [x] `docs/spikes/injection/scripts/02-run-matrix.sh` -- per CLI and image: start-up without a credential, then a model run with the machine-readable stream, schema-constrained output, and the Codex effort override
- [x] `docs/spikes/injection/scripts/03-planted-config.sh` -- generate the planted repository in a volume; run each CLI in each authentication mode with the disabling flags; report canaries and the tools the CLI lists
- [x] `docs/spikes/injection/scripts/04-proxy.sh` and `proxy/` -- internal network, unprivileged proxy; model request with and without proxy variables; hosts taken from the proxy log
- [x] `docs/spikes/injection/scripts/05-package-managers.sh` (sample projects are generated in the container; no `fixtures/`) -- npm, `dotnet restore`, pip, cargo, go, Maven, Gradle restoring one dependency with the generic variables alone, then with the settings each needs
- [x] `docs/spikes/injection/scripts/.env.example`, `99-cleanup.sh` -- credential variable names with no values; removal of every object the spike created
- [x] `docs/spikes/injection/results/` -- captured output per cell, free of secrets
- [x] `docs/spikes/injection/README.md` -- the report: method, one section per acceptance criterion, vendor terms with citations, verdicts for AD-7 and AD-8, and required architecture changes

**Acceptance Criteria:**
- Given both pinned CLIs in the agents volume, when each runs non-interactively in each of the three images, then the report records per CLI and image whether the run completed and emitted its stream, names the Claude Code build for glibc and for musl, and records schema-constrained output with its flags and the `codex exec` effort override.
- Given the planted repository, when each CLI runs in each authentication mode with the disabling configuration, then the report states per CLI and mode whether none of the planted configuration executed and which account-linked tools stay reachable, and marks a mode unsupported where this is not guaranteed.
- Given a container on the internal network, when each CLI makes a model request with and without the proxy variables, then the first succeeds, the second fails, and the report lists the hosts needed per authentication mode.
- Given the same network, when each of the seven package managers restores a dependency, then the report records whether the generic variables suffice and which settings the Project image must carry.
- Given each vendor's published terms, when read, then the report states whether fetching the binaries at startup is permitted, with the source cited.
- Given all findings, when the spike closes, then the report states "confirmed" or "fallback: Hephaestus base image" for AD-7 and for AD-8, and the Owner has confirmed the credentialed results.

## Implementation Notes

- 2026-11-25. Implemented inline, not by a dispatched subagent: the second pass needs the Owner in the conversation. Work is on branch `feature/1-1-injection-spike`.
- Harness layout under `docs/spikes/injection/scripts/`: `lib.sh` (shared container definition, invocation variants), `00-self-test.sh` (added: covers every I/O matrix row without a credential), `01` to `05`, `99-cleanup.sh`, `container/` (scripts piped into containers), `images/`, `proxy/`. `fixtures/` was not needed: sample projects are generated in the container.
- Cells that need no credential use a dummy key: a 401 from the vendor shows the CLI started, used its network path, and reported the failure in its stream.
- Proxy for the spike: tinyproxy on Alpine, unprivileged. Not a product choice.
- Findings met while building the harness, all recorded in the report: Codex needs a CA bundle the Debian slim and Ubuntu base images lack (supplied beside the binaries through `CODEX_CA_CERTIFICATE`); an empty Task volume mounted at the working directory loses its ownership unless mounted `volume-nocopy`; the wrong-libc Claude Code build fails with "no such file or directory"; `codex exec` treats a project as untrusted by default and waits for the network without limit; the `codex` archive does not contain `codex-code-mode-host`.
- Pass 1 (no credential) is complete. Pass 2 (model runs, planted configuration under real sign-in, host lists) waits for the Owner's `.env`.
- 2026-11-25, pass 2. Run with the Owner's four credentials. The Codex credential file was found at the default location and its path appended to `.env` at the Owner's request. Verdicts: AD-7 confirmed, AD-8 confirmed; Codex subscription sign-in unsupported. Two readings were corrected during verification: the Codex subscription runs had answered without executing anything until `codex-code-mode-host` was pinned and fetched, and the effort override's effect on reasoning tokens did not repeat, so the report claims only that the value reaches the request. Added: `SPIKE_ONLY` cell selection, a model setting per Codex mode, variants `hardened-safe-mode` and `hardened-no-project-doc`, the tool inventory cells, masking of the subscription's utilization figure.
- 2026-11-25, after review pass 1. Every kept finding was patched (see the triage log). The harness now reads credentials from `.env` only, masks through a pipe, records its version and the container's arguments in each result, separates executed configuration from followed instructions, and judges its own results: 02 fails when a model run did not execute the tool, 03 fails when a control shows nothing live, a hardened variant executed anything, or a hardened cell's evidence is incomplete. `hardened` for Claude Code is now the `--safe-mode` invocation in both modes. The whole run was then repeated on the final harness (`bdfdba94e1fa`, 116 result files, all four scripts exit 0), `99-cleanup.sh --keep-agents` was run and left only the agents volume, and the report was rewritten from those results.
- For the Owner to confirm, stated in the report: instructions and skills the model acts on are counted as prompt content, not as executable configuration; no run loaded a repository plugin, so plugin suppression is unproven; "supported" rests on short runs.
- 2026-11-26. The Owner merged the report (pull request #1) and accepted the three items above and the credentialed results. The story is closed in `sprint-status.yaml`. The 15 findings for the architecture are not applied yet; they go through `bmad-correct-course`.
- I/O matrix audit: all five rows are covered by `00-self-test.sh`, which ran 23 checks with `failures: 0` on the final harness (`docs/spikes/injection/results/00-self-test/self-test.txt`).
- Earlier resume note, kept for the record: once `docs/spikes/injection/scripts/.env` exists, run `02-run-matrix.sh`, `03-planted-config.sh`, and `04-proxy.sh`; read every result for account data before it is committed; replace each "(pending)" in the report and state the two verdicts; then continue this workflow at "Tasks & Acceptance Verification" in step 3.
- Masking was strengthened after the vendor's 401 text was seen to quote part of a key: besides the known values, anything shaped like a key or a token is replaced. The dummy keys in `lib.sh` and `00-self-test.sh` are the only key-shaped strings in the tree, hence the changed secret-scan command below.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-25. Layers: blind hunter (BH), verification gap (VG, pre-verified), edge-case hunter (EC). The review diff left out the raw `results/**/*.txt` logs and this spec. No finding routes to intent_gap or bad_spec; every kept finding is a patch. After the patches the whole credentialed run is repeated on the final harness, which also closes VG8.

| Id | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| BH1, VG4, EC24 | API-key Claude cells in 02, 04, and the self-test ran `--bare`; the report recommends `--safe-mode` | high | `# command:` headers of `*-claude-api-key-*` show `--bare` | patch: `hardened` is the `--safe-mode` invocation in both modes; `--bare` kept as `hardened-bare` |
| BH2 | Codex invocation in the report adds `project_doc_max_bytes=0`, which only two cells ran | medium | `lib.sh` `hardened` omits it | patch: report shows the invocation as run and names the addition separately |
| BH3, VG3, EC25 | Planted plugins (and Codex `notify`) fired in no control, so their absence proves nothing | medium | no `*-plugin-*` canary in any `summary.md` row | patch: 03 lists items "not shown live"; report says plugin suppression is unproven |
| BH4 | Instruction-following markers are listed as "planted items that executed", against the frozen matrix row | medium | `summary.md` rows for Codex hardened list `codex-instruction-AGENTS.md`, `codex-skill` | patch: separate markers and columns; AD-7 and the story name exactly hooks, MCP servers, settings, plugins, so one reading exists; flagged to the Owner |
| BH5 | Codex API-key support depends on the model not following a repository skill | medium | subscription runs followed the skill in every variant | patch: report states it and that no switch removing repository skills was found |
| BH6 | Codex tool evidence is a self-report; sub-agent tools survive hardening under subscription | medium | tool-inventory cells | patch: report states the limit and why an API key carries no account connectors |
| BH7 | Claude Code account surface reads only `tools` and `mcp_servers` | low | init event also lists skills, agents, plugins, slash commands | patch: report names them and why the tool allow-list makes them unreachable |
| BH8 | "`--setting-sources user` is what stops it" was not isolated | medium | `flags` variant combines three switches | patch: variant `safe-mode-user-settings` |
| BH9 | Planted set omits other command-running settings; the address redirect was not demonstrated | low | `plant-claude.sh` | patch: report lists what was not planted and marks the consequence as inferred |
| BH10 | Three claims have no result: `SSL_CERT_FILE`, the ChatGPT-account model refusal, the `volume-nocopy` failure | medium | no result file holds them | patch: three cells in 02 |
| BH11 | "Codex retries without limit" overstates and singles out Codex | medium | Claude Code was also stopped at the deadline | patch: report rewritten from what the 60-second cells show |
| BH12 | "The only way out" is broader than tested: few probes, proxy runs on Debian only, Docker Desktop only | medium | `04-proxy.sh` | patch: more probes; limits added to "did not cover" |
| BH13 | Host lists are observed minimums | low | one prompt per mode | patch: report wording |
| BH14, EC15 | Host attribution compares the host clock with the VM's log clock | low | `proxy_hosts_since` | patch: timestamps from the proxy container |
| BH15, EC28 | Resource limits set but not evidenced; context and Methodology mounts absent | low | `*-container.txt` | patch: cgroup limits printed; absent mounts listed as not covered (the frozen block names only the mounts used) |
| BH16, VG-o4, EC19, EC21 | CA bundle from a floating tag; "present" path trusts a sidecar; archive pins cannot verify the extracted binary; base digests recorded after the build | low | `01-fetch-agents.sh`, `02-run-matrix.sh` | patch: binary digest recorded and re-checked; pull before build; report notes the lock needs a per-binary digest |
| BH17, VG2, EC4 | "No canary" passes when nothing could execute; scripts exit 0 whatever they find | high | `cell_canaries` returns empty for a cell that never started | patch: evidence checks (wrapper finished, CLI started, plant present, probe ran) and assertions in 03 |
| VG1, EC23 | 02 summary shows exit 0 for a run that executed no command | high | `without-code-mode-host` and `stream` rows identical | patch: "tool ran" column; prompt no longer contains the expected output |
| VG5, EC7 | Masking self-test cannot tell value masking from shape masking; `utilization` unchecked | medium | reviewer ran `mask` with no known values | patch: opaque value through `run_cell`; `utilization` assertion |
| VG6 | "Credential absent" self-test runs an inline copy of the branch | medium | `00-self-test.sh` | patch: runs `03-planted-config.sh` with no credential |
| VG7, EC6 | "Exit code is recorded" passes on an empty string | medium | `test "$(cell_exit …)" != 0` | patch: exact expected codes |
| VG8 | Results mix runs before and after harness changes; no result names its harness | medium | file times against `lib.sh` | patch: harness identity in every header; full re-run |
| VG-o1, EC1, EC2, EC3 | Failures of `new_workspace` and `plant_workspace` are swallowed in command substitution | medium | reproduced by the reviewer | patch: explicit `\|\| return 1` |
| VG-o2 | Shape masking has no left boundary and rewrote help text | low | `debian-codex-exec-help.txt` | patch: boundary in the pattern |
| VG-o3 | Report prescribes `$MAVEN_HOME/conf/settings.xml`; the run wrote `~/.m2` | low | `05-package-managers.sh` | patch: report says what was run and what follows from it |
| EC5 | No-proxy self-test passes when the vendor answers 4xx | medium | wget exits 1 on an HTTP error | patch: match the network error text |
| EC8, BH18b | Temp files with unmasked output or secrets survive an interrupt | medium | `mask`, `run_cell` | patch: no secret on disk; one temp directory removed on exit |
| EC9 | `.env` is sourced: a space or CRLF breaks it | medium | `load_credentials` | patch: parsed line by line |
| EC10 | A wrong `CODEX_AUTH_JSON_PATH` reads as "no credential" | low | `load_credentials` | patch: named in the log |
| EC11, BH18a | Ambient credential variables are used silently | medium | `load_credentials` never clears them; the frozen block says `.env` only | patch: cleared before loading |
| EC12 | Invalid `SPIKE_ONLY` skips everything silently | low | `cell_selected` | patch: validated |
| EC13, EC14 | 05: host file overwritten for a skipped cell; stale attempt files read as results | low | `05-package-managers.sh` | patch |
| EC16 | Proxy readiness is a fixed sleep | low | `start_proxy` | patch: wait for the port |
| EC17 | A Docker failure (125) is recorded as the cell's finding | medium | `run_cell` | patch: reported as a harness error and stops the script |
| EC18 | CLI ignoring SIGTERM hangs the harness | low | `run-agent.sh` | patch: `timeout -k`; host-side deadline rejected (cells without the wrapper are `--version` and `--help`) |
| EC20 | Download can stall without end | low | `01-fetch-agents.sh` | patch: `wget -T` |
| EC22 | 02 summary lists two information files as cells | low | committed `summary.md` | patch |
| EC26, BH20d | Bash 4.4 and host tools are required and unstated | low | `mapfile` | patch: check and report |
| EC27 | A cell script run before the fetch auto-creates an unlabelled agents volume that cleanup misses | low | Docker creates missing named volumes | patch: scripts require the volume; cleanup also removes by name |
| BH18c | Nothing says to delete `.env` or revoke the credentials | low | report, cleanup | patch |
| BH18d | Model under Codex subscription is never named; `.env.example` wording | low | report | patch: report says the stream does not name it |
| BH19 | "Supported" for Claude subscription omits the open terms risk | medium | `vendor-terms.md` against the verdict table | patch: verdict row carries the risk; licence link pinned |
| BH20a | Findings name no dependent story | medium | report | patch: each finding names the stories it blocks |
| BH20b | Registry hosts, the tool allow-list's effect on Story 1.2, and the HOME choice are missing from the findings | low | report | patch |
| BH20e | Spec says `in-review`, `sprint-status.yaml` says `in-progress` | false | the workflow syncs sprint status in its last step, not here | reject |

## Design Notes

The harness runs in two passes. The first needs no credential: fetch, start-up on the three images, proxy isolation, package managers, and the terms. The second needs the Owner: model runs, planted configuration per authentication mode, and the host lists. Scripts are plain Bash calling `docker`, with named volumes and `docker cp` instead of bind mounts, so they behave the same on Docker Desktop and on Linux.

## Verification

**Commands:**
- `bash docs/spikes/injection/scripts/01-fetch-agents.sh` -- expected: both CLIs in the agents volume, checksums verified
- `bash docs/spikes/injection/scripts/00-self-test.sh` -- expected: `failures: 0`; covers every row of the I/O matrix
- `bash docs/spikes/injection/scripts/02-run-matrix.sh` (and `03` to `05`) -- expected: exit 0, one result file per cell, `not-run` only for cells without a credential
- `git grep --untracked -n -I -E "sk-ant-|sk-proj-|oat01|eyJ[A-Za-z0-9]{16}" -- docs/spikes/injection/results` -- expected: no match
- `bash docs/spikes/injection/scripts/99-cleanup.sh && docker ps -a --filter label=hephaestus.spike=injection -q` -- expected: empty

**Manual checks (if no CLI):**
- Each of the story's seven acceptance criteria has a section in `docs/spikes/injection/README.md` whose statements cite a file in `results/` or a dated vendor source.
