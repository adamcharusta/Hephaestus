# Every pass, cell by cell

Passes on the small model: 3 (`results/`, `results/run-2/`, `results/run-3/`). A cell was repeated on the strong model in pass 1 only, when it did not complete there. Every pass also ran a short model probe per CLI and model (`model-*.txt`), the strong models included; the probes are no cells and are counted under the cost.

## 02-run-workflows

| Cell | Small model, pass 1 | Small model, pass 2 | Small model, pass 3 | Completed | Strong model |
| --- | --- | --- | --- | --- | --- |
| correction-build-auto-claude | no-artifact (the workflow halted itself: blocked) | no-artifact (the workflow halted itself: blocked) | no-artifact (the workflow halted itself: blocked) | 0 of 3 | completed |
| correction-build-auto-codex | completed | no-artifact | completed | 2 of 3 | - |
| correction-build-auto-inline-claude | completed | completed | completed | 3 of 3 | - |
| correction-build-auto-inline-codex | completed | completed | completed | 3 of 3 | - |
| correction-correct-course-claude | completed | completed | completed | 3 of 3 | - |
| correction-correct-course-codex | completed | completed | completed | 3 of 3 | - |
| entry-bare-readiness-claude | completed | completed | completed | 3 of 3 | - |
| entry-bare-readiness-codex | completed | completed | completed | 3 of 3 | - |
| entry-direct-readiness-claude | completed | completed | completed | 3 of 3 | - |
| entry-direct-readiness-codex | halted-for-input (no artifact) | halted-for-input (no artifact) | halted-for-input (no artifact) | 0 of 3 | halted-for-input (no artifact) |
| entry-mount-root-planning-claude | no-artifact | no-artifact | no-artifact | 0 of 3 | no-artifact |
| entry-mount-root-planning-codex | completed | completed | completed | 3 of 3 | - |
| implementation-build-auto-claude | no-artifact (the workflow halted itself: blocked) | no-artifact (the workflow halted itself: blocked) | no-artifact (the workflow halted itself: blocked) | 0 of 3 | no-artifact |
| implementation-build-auto-codex | completed | no-artifact (the workflow halted itself: blocked) | no-artifact | 1 of 3 | - |
| implementation-build-auto-inline-claude | completed | completed | completed | 3 of 3 | - |
| implementation-build-auto-inline-codex | no-artifact | completed | completed | 2 of 3 | completed |
| implementation-build-auto-inline-git-claude | completed | completed | completed | 3 of 3 | - |
| implementation-build-auto-inline-git-codex | completed | completed | no-artifact (the workflow halted itself: blocked) | 2 of 3 | - |
| implementation-build-claude | no-artifact | no-artifact | completed | 1 of 3 | completed |
| implementation-build-codex | completed | completed | completed | 3 of 3 | - |
| planning-build-auto-claude | no-artifact (the workflow halted itself: blocked) | no-artifact (the workflow halted itself: blocked) | completed | 1 of 3 | no-artifact (the workflow halted itself: blocked) |
| planning-build-auto-codex | completed | no-artifact (the workflow halted itself: blocked) | completed | 2 of 3 | - |
| planning-build-auto-inline-claude | completed | completed | completed | 3 of 3 | - |
| planning-build-auto-inline-codex | completed | completed | completed | 3 of 3 | - |
| planning-build-claude | completed | completed | completed | 3 of 3 | - |
| planning-build-codex | completed | no-artifact | no-artifact | 1 of 3 | - |
| planning-spec-claude | completed | completed | halted-for-input (artifact present) | 2 of 3 | - |
| planning-spec-codex | completed | completed | completed | 3 of 3 | - |
| readiness-sprint-planning-claude | completed | completed | completed | 3 of 3 | - |
| readiness-sprint-planning-codex | completed | completed | completed | 3 of 3 | - |
| review-build-auto-claude | no-artifact (the workflow halted itself: blocked) | no-artifact (the workflow halted itself: blocked) | no-artifact (the workflow halted itself: blocked) | 0 of 3 | no-artifact (the workflow halted itself: blocked) |
| review-build-auto-codex | no-artifact | completed | no-artifact | 1 of 3 | no-artifact (the workflow halted itself: blocked) |
| review-build-auto-inline-claude | no-artifact (the workflow halted itself: blocked) | completed | completed | 2 of 3 | completed |
| review-build-auto-inline-codex | completed | completed | completed | 3 of 3 | - |
| review-code-review-claude | completed | completed | no-artifact | 2 of 3 | - |
| review-code-review-codex | completed | completed | completed | 3 of 3 | - |
| review-code-review-no-git-claude | completed | completed | completed | 3 of 3 | - |
| review-code-review-no-git-codex | no-artifact | no-artifact | no-artifact | 0 of 3 | completed |
| review-review-claude | completed | completed | completed | 3 of 3 | - |
| review-review-codex | completed | completed | completed | 3 of 3 | - |

## 03-which-copy

| Cell | Small model, pass 1 | Small model, pass 2 | Small model, pass 3 | Completed | Strong model |
| --- | --- | --- | --- | --- | --- |
| control-workspace-copy-claude | completed | halted-for-input (artifact present) | completed | 2 of 3 | - |
| control-workspace-copy-codex | completed | completed | completed | 3 of 3 | - |
| methodology-only-claude | completed | completed | completed | 3 of 3 | - |
| methodology-only-codex | completed | completed | completed | 3 of 3 | - |
| named-only-claude | completed | completed | completed | 3 of 3 | - |
| named-only-codex | completed | completed | completed | 3 of 3 | - |
| pointed-at-methodology-claude | completed | completed | completed | 3 of 3 | - |
| pointed-at-methodology-codex | completed | completed | no-artifact | 2 of 3 | - |

## 04-language

| Cell | Small model, pass 1 | Small model, pass 2 | Small model, pass 3 | Completed | Strong model |
| --- | --- | --- | --- | --- | --- |
| planning-bmad-config-claude | completed | completed | completed | 3 of 3 | - |
| planning-bmad-config-codex | completed | completed | completed | 3 of 3 | - |
| planning-instruction-claude | completed | completed | completed | 3 of 3 | - |
| planning-instruction-codex | completed | no-artifact (the workflow halted itself: blocked) | completed | 2 of 3 | - |
| planning-not-told-claude | completed | completed | completed | 3 of 3 | - |
| planning-not-told-codex | completed | completed | completed | 3 of 3 | - |
| review-bmad-config-claude | completed | completed | completed | 3 of 3 | - |
| review-bmad-config-codex | completed | completed | completed | 3 of 3 | - |
| review-instruction-claude | completed | completed | completed | 3 of 3 | - |
| review-instruction-codex | completed | completed | completed | 3 of 3 | - |
| review-not-told-claude | completed | completed | completed | 3 of 3 | - |
| review-not-told-codex | completed | completed | completed | 3 of 3 | - |

## Totals

- Cells: 60. Workflow runs: 191, of which 180 on the small model and 11 repeats on the strong model.
- Agent runs recorded: 203. The difference, 12, is the model probes.
- Small model: completed 142, no-artifact 33, halted-for-input 5.
- Strong model: completed 5, no-artifact 5, halted-for-input 1.
- Cells by how many of their small-model runs completed: 3 of 3: 39; 2 of 3: 10; 1 of 3: 5; 0 of 3: 6. Completed in some runs and not in others: 15.

## What the runs cost

Codex CLI reports token counts and no cost. "Workflow" rows are the runs of cells; "model probe" rows are the one-line runs that name the model.

| CLI | Model | Kind | Runs | Seconds | Seconds per run | Cost reported | Cost per run | Tokens in | Tokens out | Tokens in per run |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Claude Code | claude-haiku-5-5 | model probe | 3 | 5 | 2 | $0.00 | $0.000 | 18231 | 12 | 6077 |
| Claude Code | claude-haiku-5-5 | workflow | 90 | 3330 | 37 | $0.73 | $0.008 | 18085659 | 656393 | 200952 |
| Claude Code | claude-sonnet-5-5 | model probe | 3 | 6 | 2 | $0.01 | $0.004 | 15762 | 12 | 5254 |
| Claude Code | claude-sonnet-5-5 | workflow | 7 | 172 | 25 | $0.55 | $0.078 | 913579 | 17818 | 130511 |
| Codex CLI | gpt-5 (as asked for) | model probe | 3 | 9 | 3 | none | none | 22326 | 214 | 7442 |
| Codex CLI | gpt-5 (as asked for) | workflow | 4 | 393 | 98 | none | none | 1701808 | 39012 | 425452 |
| Codex CLI | gpt-5-mini (as asked for) | model probe | 3 | 12 | 4 | none | none | 22326 | 154 | 7442 |
| Codex CLI | gpt-5-mini (as asked for) | workflow | 90 | 8873 | 99 | none | none | 30022720 | 645266 | 333586 |

## Readiness verdicts

Every run of the readiness gate of `bmad-sprint-planning`, in every cell that ran it. A verdict is counted as verdicts.sh counts it.

| Verdict | Runs |
| --- | --- |
| PASS | 30 |
| CONCERNS | 7 |
| FAIL | 1 |
| none given | 5 |

FAIL:

- `03-which-copy/control-workspace-copy-codex.txt`

none given:

- `02-run-workflows/entry-direct-readiness-codex-strong.txt`
- `02-run-workflows/entry-direct-readiness-codex.txt`
- `run-2/02-run-workflows/entry-direct-readiness-codex.txt`
- `run-3/02-run-workflows/entry-direct-readiness-codex.txt`
- `run-3/03-which-copy/pointed-at-methodology-codex.txt`

## Repository instruction files

The sample repository's `AGENTS.md` and `CLAUDE.md` each carry a line that leaves a marker file when an Agent acts on it. Runs whose tool calls named one of the two files: 44 of 203. Runs that left a marker:

- `02-run-workflows/correction-build-auto-claude.txt`: instruction-AGENTS.md instruction-CLAUDE.md
- `02-run-workflows/correction-correct-course-codex.txt`: instruction-AGENTS.md
- `02-run-workflows/implementation-build-auto-inline-claude.txt`: instruction-CLAUDE.md
- `03-which-copy/pointed-at-methodology-claude.txt`: instruction-CLAUDE.md
- `04-language/planning-bmad-config-claude.txt`: instruction-AGENTS.md instruction-CLAUDE.md
- `run-2/02-run-workflows/correction-correct-course-codex.txt`: instruction-AGENTS.md
- `run-3/02-run-workflows/correction-build-auto-claude.txt`: instruction-CLAUDE.md
- `run-3/02-run-workflows/entry-bare-readiness-claude.txt`: instruction-CLAUDE.md
- `run-3/02-run-workflows/planning-build-claude.txt`: instruction-AGENTS.md

## Commits the runs added

Commits: 25, in 22 runs. The identities they were made under:

- `Auto Builder <devnull@example.com>`: 1
- `BMAD Bot <bmad-bot@example.com>`: 3
- `Claude <noreply@anthropic.com>`: 6
- `Claude Code <noreply@anthropic.com>`: 2
- `Codex <codex@example.com>`: 1
- `Codex Bot <devnull@example.com>`: 1
- `Codex CI <devnull@example.com>`: 6
- `bmad-bot <bmad@local>`: 1
- `bmad-bot <devnull@example.com>`: 1
- `bmad-build-auto <bmad-automation@localhost>`: 1
- `bmad-build-auto <bmad-build-auto@localhost>`: 1
- `claude <noreply@anthropic.com>`: 1

Runs in an image with `git` that ended with uncommitted files and no commit of their own:

- `02-run-workflows/correction-build-auto-claude.txt` (no-artifact (the workflow halted itself: blocked))
- `02-run-workflows/correction-build-auto-inline-codex.txt` (completed)
- `02-run-workflows/review-build-auto-claude-strong.txt` (no-artifact (the workflow halted itself: blocked))
- `02-run-workflows/review-build-auto-claude.txt` (no-artifact (the workflow halted itself: blocked))
- `02-run-workflows/review-build-auto-codex-strong.txt` (no-artifact (the workflow halted itself: blocked))
- `02-run-workflows/review-build-auto-inline-claude.txt` (no-artifact (the workflow halted itself: blocked))
- `run-2/02-run-workflows/correction-build-auto-claude.txt` (no-artifact (the workflow halted itself: blocked))
- `run-2/02-run-workflows/review-build-auto-claude.txt` (no-artifact (the workflow halted itself: blocked))
- `run-3/02-run-workflows/correction-build-auto-claude.txt` (no-artifact (the workflow halted itself: blocked))
- `run-3/02-run-workflows/correction-build-auto-inline-codex.txt` (completed)
- `run-3/02-run-workflows/implementation-build-auto-inline-git-codex.txt` (no-artifact (the workflow halted itself: blocked))
- `run-3/02-run-workflows/review-build-auto-claude.txt` (no-artifact (the workflow halted itself: blocked))
- `run-3/02-run-workflows/review-code-review-claude.txt` (no-artifact)
- `run-3/02-run-workflows/review-code-review-codex.txt` (completed)

## Tools the Agents called

| Tool | Runs that called it | Calls |
| --- | --- | --- |
| Bash | 97 | 546 |
| Edit | 31 | 98 |
| Read | 97 | 544 |
| Write | 32 | 35 |
| file_change | 11 | 33 |
| shell | 94 | 1721 |

A row "refused" would be a call to a tool the CLI did not offer. Claude Code's tools are `Bash`, `Edit`, `Read`, and `Write`; Codex's are `shell` and `file_change`.

## Review runs of `bmad-build-auto` that changed the code under review

- `02-run-workflows/review-build-auto-inline-claude-strong.txt`: changed `shelf/inventory.py` (completed)
- `02-run-workflows/review-build-auto-inline-claude.txt`: did not change it (no-artifact (the workflow halted itself: blocked))
- `02-run-workflows/review-build-auto-inline-codex.txt`: changed `shelf/inventory.py` (completed)
- `run-2/02-run-workflows/review-build-auto-inline-claude.txt`: changed `shelf/inventory.py` (completed)
- `run-2/02-run-workflows/review-build-auto-inline-codex.txt`: changed `shelf/inventory.py` (completed)
- `run-3/02-run-workflows/review-build-auto-inline-claude.txt`: changed `shelf/inventory.py` (completed)
- `run-3/02-run-workflows/review-build-auto-inline-codex.txt`: changed `shelf/inventory.py` (completed)
