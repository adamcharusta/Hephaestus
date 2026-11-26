---
title: 'Story 1.2: BMAD spike — unattended workflows under both Agents'
type: 'chore'
created: '2026-11-26'
status: 'done'
route: 'dispatch'
baseline_commit: '6bf3ebf6e5d4c282d14a8eb0e49d175427cdccef'
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
- [x] `docs/spikes/bmad/scripts/lib-bmad.sh` -- source the injection library; add the Methodology volume, the sample repository generator, cell verdicts (`completed`, `halted-for-input`, `timeout`, `no-artifact`), and cost extraction
- [x] `docs/spikes/bmad/scripts/images/` -- one test image: a stock base plus `uv` and Python, nothing else until a finding requires it
- [x] `docs/spikes/bmad/scripts/00-self-test.sh` -- cover every I/O matrix row without a credential
- [x] `docs/spikes/bmad/scripts/01-prepare.sh` -- fill the Methodology volume from this repository's BMAD; generate the sample repository (small tested project, a story and its issue text) in a volume
- [x] `docs/spikes/bmad/scripts/02-run-workflows.sh` -- each candidate by step kind and CLI; the entry prompt variants; the read-only Methodology
- [x] `docs/spikes/bmad/scripts/03-which-copy.sh` -- workflow files at `/hephaestus/methodology` and, altered, in `/workspace`; record which copy each Agent followed
- [x] `docs/spikes/bmad/scripts/04-language.sh` -- a Polish Task; how the workflow is told the language and whether the output follows
- [x] `docs/spikes/bmad/results/` and `docs/spikes/bmad/README.md` -- captured cells and the report, with findings for the architecture

**Acceptance Criteria:**
- Given the candidates, when each runs under both CLIs, then the report records per workflow and CLI the verdict and what it produced, and names the workflow per step kind or states that none does, with the consequence for FR-63.
- Given the completed workflows, when their needs are examined, then the report lists what the Project image must provide, how a workflow is started with no `Skill` tool and no instruction file, and whether any needs a tool beyond `Bash`, `Read`, `Edit`, and `Write`.
- Given issues and BMAD's story and spec files, when the mapping is examined, then the report recommends how an issue maps to the artifact a workflow works from and how a Story is identified stably across renumbering and retitling.
- Given a workflow that completes only with a switched-off tool or setting, when the spike closes, then the report names it and it is raised with the Owner.

## Implementation Notes

- 2026-11-26. The Owner accepted the report and settled the six decisions it raised; they are recorded in the report's section "Raised with the Owner" and in pull request #4. The story is closed in `sprint-status.yaml` in that pull request, at the Owner's instruction. The 15 findings for the architecture and the six decisions are not applied yet; they go through `bmad-correct-course`.

- 2026-11-26. SonarQube Cloud failed the quality gate of pull request #4 on one condition, the security rating of new code: rule `docker:S6471`, the test image's default user is root. Fixed by `USER 10001:10001` in `bmad.Dockerfile`; no cell is run again, because every container is started with `--user 10001:10001`. The 104 maintainability issues it reported in the shell scripts do not fail the gate and are left: the scripts are the harness as it ran.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-26. Sources: BH = Blind Hunter, VG = Verification Gap, ECH = Edge Case Hunter. Files that a result's harness digest covers (`lib-bmad.sh`, `container/`, `images/`, `context/`, and the numbered scripts whose results are recorded) were left as they ran; where a finding sits in one of them and did not change a recorded verdict, the patch is a stated limit in the report.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| BH1 | A review that produced no findings is judged `completed` | medium | `results/run-3/02-run-workflows/review-code-review-claude.txt` opens "Code review did not run to findings"; its `ok:` line comes from the diff the run copied into four prompt files. It is the only one of 51 review results that flips when the pattern must be in the final message. | patch |
| BH2 | A menu line is read as a readiness verdict `FAIL` | medium | `results/02-run-workflows/entry-direct-readiness-codex.txt`: the final message lists "report PASS/CONCERNS/FAIL" as an option; the artifact line says `FAIL, stated in a sentence`. The cell's verdict stays `halted-for-input`; the tally note and the README's count are wrong. | patch |
| BH3 | "The same plan drew FAIL in 2" is unsupported | medium | Distribution over the results: 30 PASS, 7 CONCERNS, 2 FAIL, 4 without a verdict. One FAIL is BH2; the other (`03-which-copy/control-workspace-copy-codex.txt`) found no planning documents and no configuration. | patch |
| BH4 | One cell that did not complete in pass 1 has no strong-model repeat | medium | `tally.md`: `review-build-auto-inline-claude` is `no-artifact (blocked)` in pass 1 and "-" in the strong column. The frozen model rule asks for the repeat. | patch |
| BH5 | The evidence script can import from `/workspace`, and reads a stream file and a marker directory the Agent can write | low | `run-workflow.sh:415-431` runs `python3 -c` with `/workspace` as working directory; `$HOME/.spike-stream` and `$HOME/followed` are in the step user's home. No run did this and the sample is not hostile; the script's comment and the report overstate. | patch (report states the limit) |
| BH6 | The implementation check does not require added tests or a zero-quantity item | low | `expect()` runs the probe and the existing tests only. All 26 completed implementation-expectation results show a change under `tests/`, so no recorded verdict is affected. | patch (report states the limit) |
| BH7 | The `git` condition on `bmad-review` was not tested | medium | `02-run-workflows.sh:127` runs `review-review` in the image with `git` only; the no-`git` cell is `bmad-code-review`. | patch |
| BH8 | The which-copy result rests on one workflow that does not render | medium | `03-which-copy.sh` runs only the readiness gate of `bmad-sprint-planning`; `bmad-build-auto` reads its steps from `_bmad/render/` in the Task's tree and no cell alters that. | patch |
| BH9 | What "without subagents" gives up is not in the findings | medium | `checkout-sample.sh:107-115` sets two of four review layers to `instruction = ""`, and line 117 writes `epic-1-context.md` by hand. | patch |
| BH10 | Two Agent behaviours are missing from the findings table | low | The report's text has both (a Git library fetched from the package index; a whole sprint planning run with no intent); the table does not. | patch |
| BH11 | Aggregate claims have no script behind them | medium | The readiness distribution, 44 and 9 of the runs, 11 identities in 24 commits, the tools called, and 5 of 6 are hand counts. They match the results (recounted by two reviewers), but the frozen rule asks that a script reproduce every result. | patch |
| BH12 | The Methodology's provenance is asserted, not checked | low | `git status` for `_bmad`, `.claude/skills`, `.agents/skills` was empty before the run and is empty now, so the recorded commit is what the cells used. A guard for a state that did not occur. | rejected |
| BH13 | The Story-identity recommendation outruns what was tested | medium | `05-story-identity.sh:62` tests `<!-- story-id: … -->`; `README.md` recommends `<!-- story: … -->`, and says it survives renumbering and retitling, which no cell combined with the marker. | patch |
| BH14 | Three gaps in the language section | low | `04-language.sh` header names `_bmad/config.toml`, "which every workflow reads"; the sixth run wrote `1.2-unresolved.md` at 0 Polish letters per thousand (`run-2/04-language/planning-instruction-codex.txt`); the planning cells ran the customization. | patch (report) |
| BH15 | Cost and time are stated loosely | low | `tally.md`: 3335 s over 93 `haiku` runs (36 s), 8885 s over 93 `gpt-5-mini` runs (96 s); "one cent" is Claude Code only. | patch |
| BH16 | The story's bookkeeping is inconsistent | false | `sprint-status.yaml` moves to `review` in the workflow's next step; the rest asks for edits to this spec. | rejected |
| BH17 | "Raised with the Owner" is incomplete | medium | Item 4 is a heading, item 5 asks nothing, "T-level review" is defined nowhere. Polish Agent output is in the results against the repository's rule and needs the Owner's decision before a commit. | patch; the Polish text is raised with the Owner |
| BH18a | A finding phrased as a question would be judged `halted-for-input` | low | The six `halted-for-input` results were read: each ends by asking for a decision. No recorded verdict is affected. | rejected |
| BH18b | The exit-137 branch, the tally, and the summaries of `03` and `04` have no self-test | low | True; none of the paths decided a recorded verdict (no run exceeded 235 of 1800 seconds). | rejected |
| BH18c | `BMAD_STRONG=0` does not stop the strong model probe | low | `run-2` and `run-3` hold `model-*-strong.txt`; the tally says the strong model was asked in pass 1 only. | patch (tally wording) |
| BH18d | A run wrote a file name with a colon | low | `sprint-change-proposal-2026-11-26T11:23:50Z.md` in one Codex result; such a name cannot be checked out on Windows. | patch (report, finding 9) |
| VG1 | Same as BH1, filed with the missing self-test checks | medium | As filed. | patch |
| VG2 | Same as BH2, filed with the missing self-test checks | medium | As filed. | patch |
| VG3 | Nothing checks `06-tally.sh` | low | As filed; the reviewer re-added the committed tally by hand with no mismatch. A fixture test for a finished spike. | rejected |
| VG4 | The language judgement is re-implemented in the self-test | low | As filed; every committed figure is 0 or between 22 and 44 per thousand, far from the threshold of 15. | rejected |
| VG5 | Same as BH4 | medium | As BH4. | patch |
| VG6 | Report numbers to correct after VG1 and VG2 | medium | Follows from BH1 to BH4. | patch |
| VG7 | A summary column of `03` mixes two kinds of marker | low | `03-which-copy.sh:92` prints every marker; row `pointed-at-methodology-claude` shows the planted `CLAUDE.md` marker under "left by the repository's copy". | patch (report) |
| ECH1 | Same as BH1 | medium | As BH1. | patch |
| ECH2 | Same as BH2 | medium | As BH2. | patch |
| ECH3 | A final message longer than 150 lines is cut at its end | medium | `run-workflow.sh:277-280` prints the first 150 lines. Eleven Codex review results are cut (`review-review-codex` twice; nine in `04-language`), so the question check never saw their ending, and the stream is cut too. | patch (report states the limit and names the results) |
| ECH4 | Same as BH18a | low | As BH18a. | rejected |
| ECH5 | A question followed by five option lines would be missed | maybe-false | Would need a run that ends so; none was found among the results read. Low if true. | rejected |
| ECH6 | A final message could forge an evidence line | low | Sections are not prefixed. No run did; same class as BH5. | patch (report, with BH5) |
| ECH7 | The stream file is writable by the Agent | low | As BH5. | patch (report, with BH5) |
| ECH8 | A background process could hold the pipe open past the deadline | low | No run hung: the longest took 235 of 1800 seconds. | rejected |
| ECH9 | A failure of the evidence script would be recorded as `no-artifact` | low | Every Agent result has a complete `expected artifact` section. | rejected |
| ECH10 | A failed turn with exit 0 would be judged `completed` | low | The only stream errors in completed cells are Codex's "Model metadata … not found" warnings. | rejected |
| ECH11 | A FIFO or a huge file would block the inventory | low | The sample has none and no run made one. | rejected |
| ECH12 | A stale `-strong` result survives a re-run | low | All ten strong results belong to cells that did not complete in pass 1. | rejected |
| ECH13 | A missing credential overwrites recorded results with `not-run` | low | The matrix's first row asks for `not-run`; the self-test writes to a directory of its own. | rejected |
| ECH14 | A stale or empty volume passes `ensure_prepared` | low | `01-prepare.sh` rebuilt the volumes before each pass. | rejected |
| ECH15 | Same as BH12 | low | As BH12. | rejected |
| ECH16 | An alteration whose anchor is missing fails silently | low | The control cells left the skill's marker, so the alteration was applied. | rejected |
| ECH17 | Same as BH18c | low | As BH18c. | patch |
| ECH18 | The tally hides strong results of later passes yet counts their cost | low | Those results are the model probes of BH18c. | patch (with BH18c) |
| ECH19 | `pipefail` could end the self-test before it prints a failure | low | Only when a child script wrote nothing; the self-test still exits non-zero. | rejected |
| ECH20 | `05-story-identity.sh` exits 0 on a failed fetch | low | `story-keys.txt` holds the keys the report cites. | rejected |
| ECH21 | An unknown instruction variant yields the default instruction | low | `direct` is handled in `cell()` before `instruction()`; every other variant used is documented. | rejected |
| ECH22 | A deadline with a unit suffix breaks the timeout verdict | low | The scripts set whole seconds. | rejected |
| ECH23 | A second `[core]` table would invalidate the sample's configuration | low | The sample is generated and has none. | rejected |
| ECH24 | Same as BH1, filed as a claim | medium | As BH1. | patch |

## Verification

**Commands:**
- `bash docs/spikes/injection/scripts/01-fetch-agents.sh` -- expected: both CLIs in the agents volume, checksums verified
- `bash docs/spikes/bmad/scripts/00-self-test.sh` -- expected: `failures: 0`
- `bash docs/spikes/bmad/scripts/02-run-workflows.sh` (and `03`, `04`) -- expected: exit 0, one result file per cell, `not-run` only for cells without a credential
- `git grep --untracked -n -I -E "sk-ant-|sk-proj-|oat01|eyJ[A-Za-z0-9]{16}" -- docs/spikes/bmad/results` -- expected: no match
- `git status --short docs/spikes/injection _bmad .claude/skills .agents/skills` -- expected: empty
