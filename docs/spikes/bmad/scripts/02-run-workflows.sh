#!/usr/bin/env bash
# The matrix of the BMAD spike: each candidate BMAD workflow, by step kind,
# under `claude -p` and under `codex exec`, in the hardened step container of
# Story 1.1, on the workspace network behind the proxy, with BMAD's files
# mounted read-only at /hephaestus/methodology.
#
# The prompt of every cell is one sentence that points at a file. The file,
# /hephaestus/context/instruction.md, says which workflow to run and where its
# files are; the cells named entry-* vary how that is said.
#
# Every cell runs on the cheapest model first. A cell that did not complete is
# repeated on a stronger model, into a file of its own ending in "-strong", so
# that "fails on the small model" can be told apart from "fails".
#
# A cell that fails is a finding, not a failure of the script: the verdicts are
# in summary.md. A CLI whose credential is missing from .env is recorded
# not-run and the script goes on.
#
# Usage: bash 02-run-workflows.sh [claude|codex]...
#   SPIKE_ONLY='<regular expression>'  repeat only the cells whose file name matches
#   SPIKE_STREAM=<name>                use a proxy of its own, to run beside another script
#   BMAD_STRONG=0                      do not repeat on the stronger model
#   BMAD_DEADLINE, BMAD_LONG_DEADLINE  deadlines in seconds (900 and 1800)

# shellcheck source=lib-bmad.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib-bmad.sh"

out="$RESULTS_DIR/02-run-workflows"
clis=("$@")
[ ${#clis[@]} -gt 0 ] || clis=(claude codex)
for cli in "${clis[@]}"; do
  case "$cli" in claude | codex) ;; *) log "unknown CLI: $cli"; exit 2 ;; esac
done
short="${BMAD_DEADLINE:-900}"
long="${BMAD_LONG_DEADLINE:-1800}"

ensure_prepared
load_credentials
mkdir -p "$out"
if have_credential ANTHROPIC_API_KEY || have_credential OPENAI_API_KEY; then start_proxy; fi
files="$SPIKE_TMP/context"
mkdir -p "$files"

# --- The models -------------------------------------------------------------------
# One short run per CLI and model, so that the result names the model each
# alias resolved to and shows that the key may use it.

for cli in "${clis[@]}"; do
  for tier in small strong; do
    result="$out/model-$cli-$tier.txt"
    credential="$("${cli}_credential_var" api-key)"
    if ! have_credential "$credential"; then not_run "$result" "no credential ($credential)"; continue; fi
    cell_selected "$result" || continue
    use_models "$tier"
    ws="$(sample_workspace "model-$cli${SPIKE_STREAM:+-$SPIKE_STREAM}" task/low-stock)" || exit 1
    agent_cell "$result" "$cli" "$BMAD_IMAGE" "$ws" - none 180 "$PONG_PROMPT" || exit 1
    drop_volumes "$ws"
  done
done

# --- The cells ----------------------------------------------------------------------

# cell <name> <skill> <entry> <branch> <alterations> <image> <expect> <deadline> <step-kind> <intent> [<name>=<file>...]
#   entry: how the Agent is told which workflow to run (see instruction in
#   lib-bmad.sh), or "direct": the prompt points straight at the skill file and
#   there is no instruction file.
cell() {
  local name="$1" skill="$2" entry="$3" branch="$4" alter="$5" image="$6" expect="$7" deadline="$8" kind="$9" intent="${10}"
  shift 10
  local cli prompt="$DEFAULT_PROMPT" context=()
  task_json "$kind" "$branch" en > "$files/task.json"
  context=("task.json=$files/task.json" "$@")
  if [ "$entry" = direct ]; then
    prompt="Read /hephaestus/methodology/skills/$skill/SKILL.md and follow it."
  else
    instruction "$entry" "$skill" "$intent" > "$files/instruction.md"
    context+=("instruction.md=$files/instruction.md")
  fi
  for cli in "${clis[@]}"; do
    log "$name, $cli"
    workflow_cell "$out" "$name" "$cli" "$image" "$branch" "$alter" "$expect" "$deadline" "$prompt" "${context[@]}"
  done
}

issue="issue.md=$BMAD_DIR/context/issue.md"
findings="findings.json=$BMAD_DIR/context/findings.json"
change="change.md=$BMAD_DIR/context/change.md"
todo=task/low-stock
built=task/low-stock-implemented
story_spec='_bmad-output/implementation-artifacts/spec-1-2-low-stock.md'

task_intent='The intent is the Task in `/hephaestus/context/issue.md`.'
readiness_intent='Intent: check implementation readiness only, the `readiness` intent of the workflow, headless. Do not generate or change the sprint status file. End with the JSON response the workflow documents for headless mode.'

# Readiness. BMAD's one readiness workflow is the gate of bmad-sprint-planning.
cell readiness-sprint-planning bmad-sprint-planning split "$todo" "" "$BMAD_IMAGE" readiness-verdict "$short" Readiness \
  "$readiness_intent"

# Planning.
cell planning-build-auto bmad-build-auto split "$todo" "" "$BMAD_IMAGE" ready-spec "$short" Planning \
  "$task_intent Halt after planning." "$issue"
cell planning-build-auto-inline bmad-build-auto split "$todo" inline-build-auto "$BMAD_IMAGE" ready-spec "$short" Planning \
  "$task_intent Halt after planning." "$issue"
cell planning-spec bmad-spec split "$todo" "" "$BMAD_IMAGE" spec-kernel "$short" Planning \
  'Headless. The input is the Task in `/hephaestus/context/issue.md`; the slug is `low-stock`. Return the JSON the workflow documents for headless mode.' "$issue"
cell planning-build bmad-build split "$todo" "" "$BMAD_IMAGE" ready-spec "$short" Planning \
  "$task_intent Plan it, and stop when the spec is ready for development." "$issue"

# Implementation.
cell implementation-build-auto bmad-build-auto split "$todo" "" "$BMAD_IMAGE" implementation "$long" Implementation \
  "$task_intent Implement it on the current branch." "$issue"
# The same workflow with a BMAD customization that needs no subagent, in the
# image without git and in the image with git.
cell implementation-build-auto-inline bmad-build-auto split "$todo" inline-build-auto "$BMAD_IMAGE" implementation "$long" Implementation \
  "$task_intent Implement it on the current branch." "$issue"
cell implementation-build-auto-inline-git bmad-build-auto split "$todo" inline-build-auto "$BMAD_GIT_IMAGE" implementation "$long" Implementation \
  "$task_intent Implement it on the current branch." "$issue"
cell implementation-build bmad-build split "$todo" "" "$BMAD_IMAGE" implementation "$long" Implementation \
  "$task_intent Implement it on the current branch." "$issue"

# Review, of a branch on which the Story is implemented with two defects.
review_intent='Review the diff of this branch against `main`, a branch diff.'
cell review-code-review bmad-code-review split "$built" "" "$BMAD_GIT_IMAGE" review-findings "$short" Review \
  "$review_intent The spec file is \`$story_spec\`. Report the findings and change no code."
cell review-code-review-no-git bmad-code-review split "$built" "" "$BMAD_IMAGE" review-findings "$short" Review \
  "$review_intent The spec file is \`$story_spec\`. Report the findings and change no code."
cell review-review bmad-review split "$built" "" "$BMAD_GIT_IMAGE" review-findings "$short" Review \
  "$review_intent Report the findings as the Output section of the workflow says, and change no file."
cell review-build-auto bmad-build-auto split "$built" "" "$BMAD_GIT_IMAGE" review-findings "$long" Review \
  "The spec file is \`$story_spec\`. It is in review."
cell review-build-auto-inline bmad-build-auto split "$built" inline-build-auto "$BMAD_GIT_IMAGE" review-findings "$long" Review \
  "The spec file is \`$story_spec\`. It is in review."

# Correction.
correction_intent='The intent is to resolve the blocking Findings in `/hephaestus/context/findings.json`. They were raised in the review of the work on this branch for the Task in `/hephaestus/context/issue.md`.'
cell correction-correct-course bmad-correct-course split "$todo" "" "$BMAD_IMAGE" change-proposal "$short" Correction \
  'The change that triggers the workflow is described in `/hephaestus/context/change.md`. Mode: Batch. Write the sprint change proposal the workflow produces.' "$change"
cell correction-build-auto bmad-build-auto split "$built" "" "$BMAD_GIT_IMAGE" implementation "$long" Correction \
  "$correction_intent" "$issue" "$findings"
cell correction-build-auto-inline bmad-build-auto split "$built" inline-build-auto "$BMAD_GIT_IMAGE" implementation "$long" Correction \
  "$correction_intent" "$issue" "$findings"

# How the workflow is started: the same readiness run, told less.
cell entry-bare-readiness bmad-sprint-planning bare "$todo" "" "$BMAD_IMAGE" readiness-verdict "$short" Readiness \
  "$readiness_intent"
cell entry-direct-readiness bmad-sprint-planning direct "$todo" "" "$BMAD_IMAGE" readiness-verdict "$short" Readiness ""

# The read-only Methodology: a workflow that renders a snapshot of itself, told
# that BMAD's own project root is the mount.
cell entry-mount-root-planning bmad-build-auto mount-root "$todo" "" "$BMAD_IMAGE" ready-spec "$short" Planning \
  "$task_intent Halt after planning." "$issue"

write_summary "$out"
