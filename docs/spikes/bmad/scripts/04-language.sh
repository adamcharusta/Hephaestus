#!/usr/bin/env bash
# A Task whose language is Polish (FR-69): how a BMAD workflow is told which
# language to write in, and whether its output follows.
#
# The Task's language is a fact of the Task: `"language": "pl"` in
# /hephaestus/context/task.json. The issue and the repository are in English,
# so that a Polish answer can only come from what the workflow was told.
# BMAD has a language setting of its own, `communication_language` and
# `document_output_language` in `_bmad/config.toml`, which every workflow
# reads; in the sample repository both say English.
#
#   not-told     nothing names the language but task.json, which the
#                instruction does not mention
#   instruction  the instruction file tells the Agent to write in the Task's
#                language; BMAD's configuration still says English
#   bmad-config  BMAD's configuration in the repository says Polish; the
#                instruction file says nothing
#
# Two workflows: planning with bmad-build-auto, which writes a document and a
# final message, and review with bmad-review, whose final message is the result.
#
# The measure is the share of letters that exist only in Polish, counted in
# the final message and in each document the workflow wrote: Polish prose has
# about 60 to 70 such letters per thousand, English has none. 15 or more per
# thousand counts as Polish.
#
# Usage: bash 04-language.sh [claude|codex]...

# shellcheck source=lib-bmad.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib-bmad.sh"

out="$RESULTS_DIR/04-language"
clis=("$@")
[ ${#clis[@]} -gt 0 ] || clis=(claude codex)
for cli in "${clis[@]}"; do
  case "$cli" in claude | codex) ;; *) log "unknown CLI: $cli"; exit 2 ;; esac
done
deadline="${BMAD_DEADLINE:-900}"

ensure_prepared
load_credentials
mkdir -p "$out"
if have_credential ANTHROPIC_API_KEY || have_credential OPENAI_API_KEY; then start_proxy; fi
files="$SPIKE_TMP/context"
mkdir -p "$files"

told='Write everything a person will read in the language that `language` names in `/hephaestus/context/task.json`: your final message and every document you write. Code, identifiers, file names, commands, and the keys and status values of a file stay as they are.'
plan_intent='The intent is the Task in `/hephaestus/context/issue.md`. Halt after planning.'
review_intent='Review the diff of this branch against `main`, a branch diff. Report the findings as the Output section of the workflow says, and change no file.'

# language_cell <workflow> <variant> <cli>
language_cell() {
  local workflow="$1" variant="$2" cli="$3"
  local skill branch alter expect image intent kind=Review
  if [ "$workflow" = planning ]; then
    kind=Planning
    skill=bmad-build-auto; branch=task/low-stock; alter=inline-build-auto; expect=ready-spec; image="$BMAD_IMAGE"; intent="$plan_intent"
  else
    skill=bmad-review; branch=task/low-stock-implemented; alter=""; expect=review-findings; image="$BMAD_GIT_IMAGE"; intent="$review_intent"
  fi
  case "$variant" in
    instruction) intent="$intent"$'\n\n'"$told" ;;
    bmad-config) alter="$alter polish-config" ;;
  esac
  task_json "$kind" "$branch" pl > "$files/task.json"
  instruction split "$skill" "$intent" > "$files/instruction.md"
  log "$workflow-$variant, $cli"
  workflow_cell "$out" "$workflow-$variant" "$cli" "$image" "$branch" "$alter" "$expect" "$deadline" "$DEFAULT_PROMPT" \
    "task.json=$files/task.json" "instruction.md=$files/instruction.md" "issue.md=$BMAD_DIR/context/issue.md"
}

for cli in "${clis[@]}"; do
  for workflow in planning review; do
    for variant in not-told instruction bmad-config; do
      language_cell "$workflow" "$variant" "$cli"
    done
  done
done

# --- Summary --------------------------------------------------------------------

# polish <per-thousand>: the judgement of one measure.
polish() { if [ "${1:-0}" -ge 15 ]; then echo "Polish ($1)"; else echo "not Polish ($1)"; fi; }

{
  echo "| Cell | Verdict | Model | Final message | Documents the workflow wrote |"
  echo "| --- | --- | --- | --- | --- |"
  for result in "$out"/*.txt; do
    [ -f "$result" ] && is_cell "$result" || continue
    name="$(basename "$result" .txt)"
    verdict="$(cell_verdict "$result")"
    if [ "$verdict" = not-run ]; then
      echo "| $name | not-run: $(sed -n 's/^# not-run: //p' "$result") | | | |"
      continue
    fi
    final="$(section "$result" language | sed -n 's/^final message: [0-9]* letters, \([0-9]*\) Polish.*/\1/p' | head -n 1)"
    documents=""
    while IFS= read -r line; do
      [ -n "$line" ] || continue
      documents+="${documents:+; }\`${line%%: *}\`: $(polish "$(sed -n 's/.* letters, \([0-9]*\) Polish.*/\1/p' <<< "$line")")"
    done < <(section "$result" language | grep -v '^final message: ' || true)
    echo "| $name | $verdict | $(cell_model "$result") | $(polish "${final:-0}") | ${documents:-none written} |"
  done
} > "$out/summary.md"
cat "$out/summary.md"
