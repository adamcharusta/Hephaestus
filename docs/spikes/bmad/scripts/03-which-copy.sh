#!/usr/bin/env bash
# Which copy of a workflow an Agent follows (AD-14).
#
# A BMAD Project carries BMAD's files in its repository, so in a step container
# the same workflow exists twice: read-only at /hephaestus/methodology, taken
# from the Base Branch, and in /workspace, where the Task's branch may have
# changed it. Codex CLI offers a repository's skills to the model whatever is
# switched off.
#
# Here the copies differ. The repository's copy of the bmad-sprint-planning
# skill, in .claude/skills and in .agents/skills, tells the Agent to run a
# command that leaves a marker file, and the repository's copy of BMAD's
# scripts leaves one when it runs. The copy at /hephaestus/methodology is
# unchanged. Each Agent then runs that workflow's readiness gate, whose input
# is the planning documents and not the changed files, told in different ways
# where the workflow is:
#
#   control-workspace-copy  pointed at the repository's copy. Shows that the
#                           markers are live: without it, no marker elsewhere
#                           would mean nothing.
#   pointed-at-methodology  pointed at the mount, as the matrix does
#   named-only              the workflow is named and no path is given
#   methodology-only        pointed at the mount, in a repository with no BMAD
#                           file of its own
#
# The script exits 1 when a control left no marker in any run, because the
# other cells then show nothing.
#
# Usage: bash 03-which-copy.sh [claude|codex]...

# shellcheck source=lib-bmad.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib-bmad.sh"

out="$RESULTS_DIR/03-which-copy"
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

branch=task/low-stock
intent='Intent: check implementation readiness only, the `readiness` intent of the workflow, headless. Do not generate or change the sprint status file. End with the JSON response the workflow documents for headless mode.'

# The skill directory each CLI reads a repository's skills from.
skills_dir() { if [ "$1" = claude ]; then echo .claude; else echo .agents; fi; }

# copy_cell <name> <entry> <alterations> <cli>
copy_cell() {
  local name="$1" entry="$2" alter="$3" cli="$4"
  task_json Readiness "$branch" en > "$files/task.json"
  instruction "$entry" bmad-sprint-planning "$intent" > "$files/instruction.md"
  log "$name, $cli"
  workflow_cell "$out" "$name" "$cli" "$BMAD_IMAGE" "$branch" "$alter" readiness-verdict "$deadline" "$DEFAULT_PROMPT" \
    "task.json=$files/task.json" "instruction.md=$files/instruction.md"
}

for cli in "${clis[@]}"; do
  copy_cell control-workspace-copy "workspace:$(skills_dir "$cli")" altered-skills "$cli"
  copy_cell pointed-at-methodology split altered-skills "$cli"
  copy_cell named-only named altered-skills "$cli"
  copy_cell methodology-only split no-bmad "$cli"
done

# --- Judgement and summary ---------------------------------------------------

path_count() {
  local count
  count="$(section "$1" paths | sed -n "s/^$2: //p" | head -n 1)"
  echo "${count:-0}"
}

failures=0
{
  echo "| Cell | Verdict | Model | Markers left by the repository's copy | Tool calls naming the mount's skills | the repository's skills | the mount's \`_bmad/\` | the repository's \`_bmad/\` |"
  echo "| --- | --- | --- | --- | --- | --- | --- | --- |"
  for result in "$out"/*.txt; do
    [ -f "$result" ] && is_cell "$result" || continue
    name="$(basename "$result" .txt)"
    verdict="$(cell_verdict "$result")"
    if [ "$verdict" = not-run ]; then
      echo "| $name | not-run: $(sed -n 's/^# not-run: //p' "$result") | | | | | | |"
      continue
    fi
    markers="$(cell_followed "$result" | paste -sd ' ' -)"
    repository=$(( $(path_count "$result" workspace-claude-skills) + $(path_count "$result" workspace-agents-skills) ))
    echo "| $name | $verdict | $(cell_model "$result") | ${markers:-none} | $(path_count "$result" methodology-skills) | $repository | $(path_count "$result" methodology-bmad) | $(path_count "$result" workspace-bmad) |"
  done
} > "$out/summary.md"
cat "$out/summary.md"

# A control counts when any of its runs, small model or strong, left a marker.
for cli in "${clis[@]}"; do
  [ -f "$out/control-workspace-copy-$cli.txt" ] || continue
  grep -q '^# not-run:' "$out/control-workspace-copy-$cli.txt" && continue
  live=no
  for result in "$out/control-workspace-copy-$cli.txt" "$out/control-workspace-copy-$cli-strong.txt"; do
    [ -f "$result" ] && cell_followed "$result" | grep -q '^workspace-copy-' && live=yes
  done
  if [ "$live" = no ]; then
    log "FAIL: control-workspace-copy-$cli left no marker, so the other $cli cells show nothing"
    failures=$((failures + 1))
  fi
done
if [ "$failures" -ne 0 ]; then exit 1; fi
