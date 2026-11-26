#!/usr/bin/env bash
# One table over every pass of the cell scripts, and the counts the report
# states. No container is started and no credential is needed: it reads result
# files that already exist.
#
# A cell is one model run, and the same cell can end differently in two runs.
# So the cells are run more than once on the small model:
#
#   bash 02-run-workflows.sh                                   # pass 1, into results/
#   SPIKE_RESULTS_DIR="$PWD/../results/run-2" BMAD_STRONG=0 bash 02-run-workflows.sh
#   SPIKE_RESULTS_DIR="$PWD/../results/run-3" BMAD_STRONG=0 bash 02-run-workflows.sh
#
# and likewise for 03-which-copy.sh and 04-language.sh. A cell is repeated on
# the stronger model in pass 1 only, when it did not complete there.
# BMAD_STRONG=0 stops those repeats and nothing else: 02-run-workflows.sh still
# runs its short model probe (model-<cli>-strong.txt) in every pass.
#
# This script writes results/tally.md:
#   - per cell, the outcome of each pass on the small model, how many of them
#     completed, and the outcome on the strong model;
#   - the totals of those outcomes;
#   - what the runs cost, per model, workflow runs apart from model probes;
#   - the readiness verdicts the gate gave, with the files behind each FAIL;
#   - the runs that read a repository instruction file, and those that acted
#     on the line planted in it;
#   - the commits the runs added and the identities they were made under;
#   - the tools the Agents called;
#   - which review runs of bmad-build-auto changed the code under review.
#
# Usage: bash 06-tally.sh

# shellcheck source=lib-bmad.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib-bmad.sh"

passes=("$RESULTS_DIR")
for dir in "$RESULTS_DIR"/run-*; do [ -d "$dir" ] && passes+=("$dir"); done

# Every result of an Agent run in the cell scripts, in every pass.
mapfile -t runs < <(for dir in "${passes[@]}"; do
  grep -l '^# --- end of evidence ---$' "$dir"/0[2-4]-*/*.txt 2> /dev/null || true
done)
[ ${#runs[@]} -gt 0 ] || { log "no result of an Agent run under $RESULTS_DIR"; exit 1; }

# relative: result paths on stdin, as the report cites them.
relative() { sed "s#^$RESULTS_DIR/##"; }

# outcome <result-file>: the verdict, and the two things a verdict alone hides.
outcome() {
  local file="$1" verdict
  [ -f "$file" ] || { echo "-"; return; }
  verdict="$(cell_verdict "$file")"
  case "$verdict" in
    halted-for-input)
      if artifact_ok "$file"; then echo "halted-for-input (artifact present)"; else echo "halted-for-input (no artifact)"; fi
      ;;
    no-artifact)
      if halted_itself "$file"; then echo "no-artifact (the workflow halted itself: blocked)"; else echo no-artifact; fi
      ;;
    *) echo "$verdict" ;;
  esac
}

declare -A small strong spread
small_runs=0
strong_runs=0
cells_total=0

{
  echo "# Every pass, cell by cell"
  echo
  echo "Passes on the small model: ${#passes[@]} (\`results/\`$(for dir in "${passes[@]:1}"; do printf ', `results/%s/`' "$(basename "$dir")"; done)). A cell was repeated on the strong model in pass 1 only, when it did not complete there. Every pass also ran a short model probe per CLI and model (\`model-*.txt\`), the strong models included; the probes are no cells and are counted under the cost."
  for script in 02-run-workflows 03-which-copy 04-language; do
    echo
    echo "## $script"
    echo
    printf '| Cell |'
    for index in "${!passes[@]}"; do printf ' Small model, pass %s |' "$((index + 1))"; done
    echo " Completed | Strong model |"
    printf '| --- |'
    for index in "${!passes[@]}"; do printf ' --- |'; done
    echo " --- | --- |"
    cells="$(for dir in "${passes[@]}"; do
      for file in "$dir/$script"/*.txt; do
        [ -f "$file" ] && is_cell "$file" || continue
        name="$(basename "$file" .txt)"
        case "$name" in model-* | *-strong) continue ;; esac
        echo "$name"
      done
    done | LC_ALL=C sort -u)"
    while IFS= read -r name; do
      [ -n "$name" ] || continue
      completed=0
      ran=0
      row="| $name |"
      for dir in "${passes[@]}"; do
        file="$dir/$script/$name.txt"
        text="$(outcome "$file")"
        row+=" $text |"
        if [ -f "$file" ] && [ "$text" != not-run ]; then
          ran=$((ran + 1))
          small_runs=$((small_runs + 1))
          small["${text%% (*}"]=$((${small["${text%% (*}"]:-0} + 1))
          [ "$text" = completed ] && completed=$((completed + 1))
        fi
      done
      text="$(outcome "$RESULTS_DIR/$script/$name-strong.txt")"
      if [ "$text" != "-" ]; then
        strong_runs=$((strong_runs + 1))
        strong["${text%% (*}"]=$((${strong["${text%% (*}"]:-0} + 1))
      fi
      cells_total=$((cells_total + 1))
      spread["$completed of $ran"]=$((${spread["$completed of $ran"]:-0} + 1))
      echo "$row $completed of $ran | $text |"
    done <<< "$cells"
  done

  echo
  echo "## Totals"
  echo
  probes="$(printf '%s\n' "${runs[@]}" | grep -c '/model-[a-z]*-[a-z]*\.txt$' || true)"
  echo "- Cells: $cells_total. Workflow runs: $((small_runs + strong_runs)), of which $small_runs on the small model and $strong_runs repeats on the strong model."
  echo "- Agent runs recorded: ${#runs[@]}. The difference, $probes, is the model probes."
  echo "- Small model: $(for key in completed no-artifact halted-for-input timeout error; do [ -n "${small[$key]:-}" ] && printf '%s %s, ' "$key" "${small[$key]}"; done | sed 's/, $//')."
  echo "- Strong model: $(for key in completed no-artifact halted-for-input timeout error; do [ -n "${strong[$key]:-}" ] && printf '%s %s, ' "$key" "${strong[$key]}"; done | sed 's/, $//')."
  varied=0
  for key in "${!spread[@]}"; do
    case "$key" in "0 of "* ) ;; *) [ "${key%% of *}" = "${key##* of }" ] || varied=$((varied + ${spread[$key]})) ;; esac
  done
  echo "- Cells by how many of their small-model runs completed: $(for key in "${!spread[@]}"; do echo "$key: ${spread[$key]}"; done | LC_ALL=C sort -r | paste -sd ';' - | sed 's/;/; /g'). Completed in some runs and not in others: $varied."

  echo
  echo "## What the runs cost"
  echo
  echo "Codex CLI reports token counts and no cost. \"Workflow\" rows are the runs of cells; \"model probe\" rows are the one-line runs that name the model."
  echo
  echo "| CLI | Model | Kind | Runs | Seconds | Seconds per run | Cost reported | Cost per run | Tokens in | Tokens out | Tokens in per run |"
  echo "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"
  for file in "${runs[@]}"; do
    usage="$(section "$file" usage)"
    tokens="$(sed -n 's/^tokens: //p' <<< "$usage" | head -n 1)"
    case "$(basename "$file")" in *-claude.txt | *-claude-strong.txt | model-claude-*) cli="Claude Code" ;; *) cli="Codex CLI" ;; esac
    case "$(basename "$file")" in model-*) kind="model probe" ;; *) kind="workflow" ;; esac
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$cli" "$(sed -n 's/^model: //p' <<< "$usage" | head -n 1)" "$kind" \
      "$(cell_seconds "$file")" "$(sed -n 's/^cost_usd: //p' <<< "$usage" | head -n 1)" \
      "$(sed -n 's/^\([0-9]*\) in.*/\1/p' <<< "$tokens")" "$(sed -n 's/.* \([0-9]*\) out.*/\1/p' <<< "$tokens")"
  done | awk -F '\t' '
    { key = $1 "\t" $2 "\t" $3; n[key]++; seconds[key] += $4; if ($5 != "") { cost[key] += $5; priced[key] = 1 } tin[key] += $6; tout[key] += $7 }
    END {
      for (key in n) {
        split(key, part, "\t")
        printf "| %s | %s | %s | %d | %d | %.0f | %s | %s | %d | %d | %.0f |\n", part[1], part[2], part[3], n[key], seconds[key], seconds[key] / n[key], \
          (priced[key] ? sprintf("$%.2f", cost[key]) : "none"), (priced[key] ? sprintf("$%.3f", cost[key] / n[key]) : "none"), tin[key], tout[key], tin[key] / n[key]
      }
    }' | LC_ALL=C sort

  echo
  echo "## Readiness verdicts"
  echo
  echo "Every run of the readiness gate of \`bmad-sprint-planning\`, in every cell that ran it. A verdict is counted as verdicts.sh counts it."
  echo
  verdicts="$(for file in "${runs[@]}"; do
    checks="$(section "$file" "expected artifact")"
    grep -q '^expectation: readiness-verdict$' <<< "$checks" || continue
    word="none given"
    if artifact_ok "$file"; then word="$(sed -n 's/^ok: a readiness verdict in the final result (\([A-Z]*\),.*/\1/p' <<< "$checks" | head -n 1)"; fi
    printf '%s\t%s\n' "${word:-none given}" "$file"
  done)"
  # Each line of $verdicts is a verdict, a tab, and a file; the two greps below
  # match up to that tab.
  echo "| Verdict | Runs |"
  echo "| --- | --- |"
  for word in PASS CONCERNS FAIL "none given"; do
    echo "| $word | $(grep -c "^$word	" <<< "$verdicts" || true) |"
  done
  echo
  for word in FAIL "none given"; do
    echo "$word:"
    echo
    { grep "^$word	" <<< "$verdicts" || true; } | cut -f2 | relative | sed 's/^/- `/; s/$/`/'
    echo
  done

  echo "## Repository instruction files"
  echo
  named="$({ grep -l '^repository-instruction-file: [1-9]' "${runs[@]}" || true; } | wc -l | tr -d ' ')"
  echo "The sample repository's \`AGENTS.md\` and \`CLAUDE.md\` each carry a line that leaves a marker file when an Agent acts on it. Runs whose tool calls named one of the two files: $named of ${#runs[@]}. Runs that left a marker:"
  echo
  awk '
    FNR == 1 { inside = 0 }
    /^# --- instructions followed ---$/ { inside = 1; next }
    /^# --- end instructions followed ---$/ { inside = 0 }
    inside && /^instruction-/ { marks[FILENAME] = marks[FILENAME] " " $0 }
    END { for (file in marks) print file "\t" marks[file] }
  ' "${runs[@]}" | LC_ALL=C sort | relative | awk -F '\t' '{ printf "- `%s`:%s\n", $1, $2 }'

  echo
  echo "## Commits the runs added"
  echo
  commits="$(grep -h '^commit: ' "${runs[@]}" || true)"
  echo "Commits: $(grep -c . <<< "$commits" || true), in $({ grep -l '^commit: ' "${runs[@]}" || true; } | wc -l | tr -d ' ') runs. The identities they were made under:"
  echo
  sed -E 's/^commit: [0-9a-f]+ //; s/>.*/>/' <<< "$commits" | LC_ALL=C sort | uniq -c | sed -E 's/^ *([0-9]+) (.*)$/- `\2`: \1/'
  echo
  echo "Runs in an image with \`git\` that ended with uncommitted files and no commit of their own:"
  echo
  awk '
    FNR == 1 { inside = 0 }
    /^# --- repository after ---$/ { inside = 1; dirty = 0; next }
    /^# --- end repository after ---$/ { inside = 0 }
    inside && /^files that differ from the commit, untracked included: / { dirty = $NF }
    inside && /^commits the run added: 0$/ && dirty > 0 { print FILENAME }
  ' "${runs[@]}" | while IFS= read -r file; do
    echo "- \`$(relative <<< "$file")\` ($(outcome "$file"))"
  done

  echo
  echo "## Tools the Agents called"
  echo
  echo "| Tool | Runs that called it | Calls |"
  echo "| --- | --- | --- |"
  { grep -h -E '^(called|refused): [A-Za-z_]+ [0-9]+$' "${runs[@]}" || true; } | awk '
    { key = ($1 == "refused:" ? "refused: " : "") $2; n[key]++; calls[key] += $3 }
    END { for (key in n) printf "| %s | %d | %d |\n", key, n[key], calls[key] }' | LC_ALL=C sort
  echo
  echo "A row \"refused\" would be a call to a tool the CLI did not offer. Claude Code's tools are \`Bash\`, \`Edit\`, \`Read\`, and \`Write\`; Codex's are \`shell\` and \`file_change\`."

  echo
  echo "## Review runs of \`bmad-build-auto\` that changed the code under review"
  echo
  for dir in "${passes[@]}"; do
    for file in "$dir"/02-run-workflows/review-build-auto-inline-*.txt; do
      [ -f "$file" ] || continue
      if section "$file" "workspace changes" | grep -q '^changed shelf/inventory.py$'; then changed="changed \`shelf/inventory.py\`"; else changed="did not change it"; fi
      echo "- \`$(relative <<< "$file")\`: $changed ($(outcome "$file"))"
    done
  done
} > "$RESULTS_DIR/tally.md"
cat "$RESULTS_DIR/tally.md"
