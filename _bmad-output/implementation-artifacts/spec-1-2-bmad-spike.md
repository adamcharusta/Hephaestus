---
title: 'Story 1.2: BMAD spike — unattended workflows under both Agents'
type: 'chore'
created: '2026-11-26'
status: 'ready-for-dev'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/docs/spikes/injection/README.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** BMAD Method support (FR-63) and the Methodology mount (AD-14) assume that BMAD workflows finish with nobody watching, under both Agents, when their files sit read-only outside the repository. Nobody has run one that way. Hardened Claude Code has no `Skill` and no `Task` tool, and neither Agent reads the repository's instruction file, so how a workflow is even started is unknown.

**Approach:** Build a reproducible harness under `docs/spikes/bmad/` on top of the injection spike's container definition and hardened invocations. Run each candidate BMAD workflow for readiness, planning, implementation, review, and correction under `claude -p` and `codex exec` against a generated sample repository, with the Methodology at `/hephaestus/methodology`. Record the evidence in a report that names the workflow serving each step kind, or states that none does.

## Boundaries & Constraints

**Always:**
- Containers and invocations are those Story 1.1 confirmed, taken from `docs/spikes/injection/scripts/lib.sh` unchanged; Codex runs with `project_doc_max_bytes=0` added.
- The Owner supplies every credential through the git-ignored `.env`; no value reaches a result file, a log, or a commit. A cell whose credential is missing is recorded `not-run`.
- "Completed with no human input" is judged by the harness from evidence a run cannot fake: exit code, the stream's final result, and the files the workflow left in `/workspace`. A run that ends by asking a question is recorded as not unattended.
- A prompt is one sentence pointing at a file the Agent reads; no slash command, no skill name the CLI must resolve.
- Every claim in the report cites a result file. Every result is reproducible by a script in the same directory.
- Each run has a deadline and a recorded cost or token count.
- Models (Owner, 2026-11-26): every cell runs first on the cheapest model, `haiku` for Claude Code and `gpt-5-mini` for Codex CLI. Only a cell that did not complete is repeated on a stronger model, and the report keeps "fails on the small model" apart from "fails".
- Authentication (Owner, 2026-11-26): an API key for both CLIs. Subscription sign-in is not exercised.

**Never:**
- No change to `docs/spikes/injection/`, the architecture spine, an ADR, the threat model, `SPEC.md`, `epics.md`, or the installed BMAD (`_bmad/`, `.claude/skills/`, `.agents/skills/`).
- No change to a hardened invocation to make a workflow pass. A workflow that needs a switched-off tool or setting is a finding raised with the Owner.
- No BMAD files or planted configuration added to this repository's tree for the sample repository; it is generated in a container volume from this repository's own installed BMAD.
- No usage-limit testing (Story 7.1) and no Methodology templates (Epic 5).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Credential absent | `.env` lacks a CLI's variable | That CLI's cells recorded `not-run`; the rest run | Script continues, exits 0 |
| Workflow asks a question | Final message is a question or a menu | Cell recorded `halted-for-input`, with the question | None; a finding |
| Run exceeds its deadline | No result before the deadline | Cell recorded `timeout`, partial stream kept | Matrix continues |
| Workflow writes to the Methodology mount | A write under `/hephaestus/methodology` | Write fails; the path and the error are recorded | None; a finding |
| Workflow claims success, changed nothing | Exit 0, no expected artifact in `/workspace` | Cell recorded `no-artifact`, not completed | None; a finding |

</frozen-after-approval>

## Code Map

- `docs/spikes/injection/scripts/lib.sh` -- source it, do not copy or edit. Reuse `hardened_args`, `new_workspace`, `run_cell`, `not_run`, `mask`, `load_credentials`, `claude_args`/`codex_args` with variants `hardened` and `hardened-no-project-doc`, `cell_selected` (`SPIKE_ONLY`). Set `SPIKE_RESULTS_DIR` to this spike's `results/`; `.env` stays where it is.
- `docs/spikes/injection/scripts/01-fetch-agents.sh` -- run first; the agents volume was removed after Story 1.1.
- `docs/spikes/injection/scripts/container/run-agent.sh` -- model for the in-container wrapper: deadline with `timeout -k`, markers reported after the run.
- `_bmad/` (6.12.4, modules `core` and `bmm`), `.claude/skills/`, `.agents/skills/` -- identical 29 skills; the source of the Methodology volume. Read-only here.
- `_bmad/scripts/render_skill.py` -- every step-file skill starts with `uv run` of it and writes under `{project-root}/_bmad/render/`: the first place a read-only mount will bite.
- Candidates: `bmad-build-auto` (unattended loop: plan, implement, review), `bmad-build`, `bmad-code-review`, `bmad-review`, `bmad-spec` and `bmad-sprint-planning` (both document a headless mode). Refine the list by reading each `workflow.md` before running.
- `_bmad-output/planning-artifacts/epics.md` (Story 1.2) -- the seven acceptance criteria the report answers one by one.
- `.gitattributes` forces LF on `*.sh`; host is Docker Desktop on WSL2, no bind mounts.

## Tasks & Acceptance

**Execution:**
- [ ] `docs/spikes/bmad/scripts/lib-bmad.sh` -- source the injection library; add the Methodology volume, the sample repository generator, cell verdicts (`completed`, `halted-for-input`, `timeout`, `no-artifact`), and cost extraction
- [ ] `docs/spikes/bmad/scripts/images/` -- one test image: a stock base plus `uv` and Python, nothing else until a finding requires it
- [ ] `docs/spikes/bmad/scripts/00-self-test.sh` -- cover every I/O matrix row without a credential
- [ ] `docs/spikes/bmad/scripts/01-prepare.sh` -- fill the Methodology volume from this repository's BMAD; generate the sample repository (small tested project, a story and its issue text) in a volume
- [ ] `docs/spikes/bmad/scripts/02-run-workflows.sh` -- each candidate by step kind and CLI; the entry prompt variants; the read-only Methodology
- [ ] `docs/spikes/bmad/scripts/03-which-copy.sh` -- workflow files at `/hephaestus/methodology` and, altered, in `/workspace`; record which copy each Agent followed
- [ ] `docs/spikes/bmad/scripts/04-language.sh` -- a Polish Task; how the workflow is told the language and whether the output follows
- [ ] `docs/spikes/bmad/results/` and `docs/spikes/bmad/README.md` -- captured cells and the report, with findings for the architecture

**Acceptance Criteria:**
- Given the candidates, when each runs under both CLIs, then the report records per workflow and CLI the verdict and what it produced, and names the workflow per step kind or states that none does, with the consequence for FR-63.
- Given the completed workflows, when their needs are examined, then the report lists what the Project image must provide, how a workflow is started with no `Skill` tool and no instruction file, and whether any needs a tool beyond `Bash`, `Read`, `Edit`, and `Write`.
- Given issues and BMAD's story and spec files, when the mapping is examined, then the report recommends how an issue maps to the artifact a workflow works from and how a Story is identified stably across renumbering and retitling.
- Given a workflow that completes only with a switched-off tool or setting, when the spike closes, then the report names it and it is raised with the Owner.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Verification

**Commands:**
- `bash docs/spikes/injection/scripts/01-fetch-agents.sh` -- expected: both CLIs in the agents volume, checksums verified
- `bash docs/spikes/bmad/scripts/00-self-test.sh` -- expected: `failures: 0`
- `bash docs/spikes/bmad/scripts/02-run-workflows.sh` (and `03`, `04`) -- expected: exit 0, one result file per cell, `not-run` only for cells without a credential
- `git grep --untracked -n -I -E "sk-ant-|sk-proj-|oat01|eyJ[A-Za-z0-9]{16}" -- docs/spikes/bmad/results` -- expected: no match
- `git status --short docs/spikes/injection _bmad .claude/skills .agents/skills` -- expected: empty
