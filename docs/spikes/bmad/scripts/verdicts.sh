#!/usr/bin/env bash
# How a cell of the BMAD spike is judged. Sourced by lib-bmad.sh, never run.
#
# Everything here reads a result file that already exists. The harness digest
# in a result's first line does not cover this file, so that a rule can be
# corrected and every summary rewritten without running a cell again:
#
#   SPIKE_ONLY='^$' bash 02-run-workflows.sh    # no cell matches; only the summary is rewritten

# The verdict rests on what a run cannot fake: the exit code the wrapper
# recorded, the final result of the CLI's own stream, and the files found in
# /workspace afterwards. What the Agent says about its own success is not used.
#
#   not-run           the cell was not run (no credential)
#   timeout           the deadline stopped the run; the partial stream is kept
#   error             the CLI ended with a failure of its own, or the wrapper did not finish
#   halted-for-input  the run ended by asking a question or offering a menu
#   completed         exit 0, no question, and the expected artifact is there
#   no-artifact       exit 0, no question, and the expected artifact is missing, or the
#                     workflow halted itself with status `blocked`

agent_exit() { sed -n 's/^# --- agent exit: \([0-9]*\) ---$/\1/p' "$1" | tail -n 1; }

cell_seconds() { sed -n 's/^# --- seconds: \([0-9]*\) of [0-9]* ---$/\1/p' "$1" | tail -n 1; }

cell_deadline() { sed -n 's/^# --- seconds: [0-9]* of \([0-9]*\) ---$/\1/p' "$1" | tail -n 1; }

# final_question <result-file>: prints the question a run ended with; fails
# when the final message asks nothing. Among the last four lines of the final
# message, a line of prose in which a sentence ends in a question mark is a
# question; so is, among the last twelve, a request for an answer. A line of
# data (JSON, a quoted string) is not prose, and a statement that a decision is
# someone else's is not a question.
final_question() {
  local message line
  message="$(section "$1" "final message" | sed -E 's/[][[:space:]*_`>")]+$//' | grep -v '^[[:space:]]*$' || true)"
  line="$(printf '%s\n' "$message" | tail -n 4 | grep -vE '^[[:space:]]*[][{}"]' \
    | grep -oE '[^.?!:]*\?([[:space:]*_`")]|$)' | tail -n 1 || true)"
  if [ -z "$line" ]; then
    line="$(printf '%s\n' "$message" | tail -n 12 | grep -iE \
      "(waiting|awaiting) (for )?your|reply with|please (confirm|choose|select|provide|specify|tell me|let me know)|let me know (which|how|what|whether|if)|which (option|one) (do|would)|(do|would) you (want|like|prefer)|(how|what) would you like|(shall|should) I (proceed|continue)|select an option|choose (one|an option)|^[[:space:]*_-]*decision needed" \
      | tail -n 1 || true)"
  fi
  [ -n "$line" ] || return 1
  printf '%s\n' "$line" | sed -E 's/^[[:space:]*_:-]+//; s/[[:space:]*_`")]+$//' | cut -c1-220
}

# halted_itself <result-file>: a file the run wrote carries the status `blocked`.
halted_itself() {
  section "$1" "expected artifact" | grep -qE '^info: spec files written: .* is blocked(,|$)'
}

# The two defects seeded in the reviewed branch, as a review reports them. The
# same patterns as in container/run-workflow.sh, which looks for them in the
# final message and in the files the run wrote.
SEEDED_BOUNDARY='<=|quantity < threshold|strict(ly)? (less|comparison|inequality)|off[- ]by[- ]one|boundary|exclud\w+[^.]{0,120}(equal|exactly|at the threshold)|(equal to|equals|exactly at|at) the threshold[^.]{0,120}(exclud|omit|miss|left out|not (list|includ|return))'
SEEDED_NEGATIVE='negative[^.]{0,120}(accept|not (rais|reject|validat|check|implement|handl|guard)|no (check|validation|guard|error)|without|silently|returns? |missing|never|unvalidated|unguarded|does ?n.t|is ?n.t|fails? to)|(no|missing|lacks?|without|never|not|does ?n.t)\b[^.]{0,120}(validat|check|rais|ValueError|guard|reject)[^.]{0,120}negative'

# findings_in_final <result-file>: the final message itself reports a seeded
# defect. The wrapper also credits the files a run wrote, and a run that
# copies the diff under review into a file is then credited with findings it
# never produced; a review's findings are what its final message says.
findings_in_final() {
  section "$1" "final message" | grep -qiE -- "$SEEDED_BOUNDARY|$SEEDED_NEGATIVE"
}

# verdict_stated <result-file>: the line of the final message that states the
# readiness verdict names exactly one of PASS, CONCERNS, and FAIL. A line that
# names more than one is a list of what a verdict could be, not a verdict.
verdict_stated() {
  local line
  line="$(section "$1" "final message" \
    | grep -m 1 -E '([Vv][Ee][Rr][Dd][Ii][Cc][Tt]|[Gg][Aa][Tt][Ee]).{0,40}\b(PASS|CONCERNS|FAIL)\b' || true)"
  [ "$(grep -oE '\b(PASS|CONCERNS|FAIL)\b' <<< "$line" | sort -u | wc -l)" -eq 1 ]
}

# artifact_refused <result-file>: prints why an artifact the wrapper recorded
# as present is not counted; fails when nothing speaks against it.
artifact_refused() {
  local checks
  checks="$(section "$1" "expected artifact")"
  case "$(sed -n 's/^expectation: //p' <<< "$checks" | head -n 1)" in
    review-findings)
      findings_in_final "$1" || { echo "the final message reports no seeded defect"; return 0; }
      ;;
    readiness-verdict)
      if grep -q '^ok: a readiness verdict .*stated in a sentence' <<< "$checks" && ! verdict_stated "$1"; then
        echo "the line taken for the verdict names more than one of PASS, CONCERNS, FAIL"
        return 0
      fi
      ;;
  esac
  return 1
}

# artifact_ok <result-file>: every check of the expected artifact passed, and
# nothing in the final message speaks against counting it.
artifact_ok() {
  local checks
  checks="$(section "$1" "expected artifact")"
  grep -q '^ok: ' <<< "$checks" && ! grep -q '^missing: ' <<< "$checks" || return 1
  ! artifact_refused "$1" > /dev/null
}

cell_verdict() {
  local file="$1" code
  if grep -q '^# not-run:' "$file"; then echo not-run; return; fi
  code="$(agent_exit "$file")"
  if [ -z "$code" ] || ! grep -q '^# --- end of evidence ---$' "$file"; then echo error; return; fi
  case "$code" in
    124 | 137)
      # 137 is also what a process killed for another reason exits with.
      if [ "$(cell_seconds "$file")" -ge "$(cell_deadline "$file")" ]; then echo timeout; else echo error; fi
      return
      ;;
    0) ;;
    *) echo error; return ;;
  esac
  if final_question "$file" > /dev/null; then echo halted-for-input; return; fi
  # A workflow that halted itself did not complete, whatever its last message
  # says in passing: BMAD's build workflows record the halt as status `blocked`
  # in the spec or result file they write.
  if halted_itself "$file"; then echo no-artifact; return; fi
  if artifact_ok "$file"; then echo completed; else echo no-artifact; fi
}

# cell_cost <result-file>: the cost Claude Code reports, or the token count.
cell_cost() {
  local usage cost tokens
  usage="$(section "$1" usage)"
  cost="$(sed -n 's/^cost_usd: //p' <<< "$usage" | head -n 1)"
  tokens="$(sed -n 's/^tokens: //p' <<< "$usage" | head -n 1)"
  if [ -n "$cost" ]; then echo "\$$cost; $tokens"; else echo "${tokens:-not recorded}"; fi
}

cell_model() { section "$1" usage | sed -n 's/^model: //p' | head -n 1; }

# cell_artifact <result-file>: the artifact checks, on one line, and why an
# artifact the wrapper recorded is not counted.
cell_artifact() {
  local checks refused
  checks="$(section "$1" "expected artifact")"
  refused=""
  if grep -q '^ok: ' <<< "$checks" && ! grep -q '^missing: ' <<< "$checks"; then refused="$(artifact_refused "$1" || true)"; fi
  { grep -E '^(ok|missing|info): ' <<< "$checks"; [ -z "$refused" ] || echo "not counted: $refused"; } | paste -sd ';' - | sed 's/;/; /g'
}

# cell_tools <result-file>: the tools the Agent called, and refused calls.
cell_tools() {
  section "$1" tools | grep -E '^(called|refused): ' | sed -E 's/^called: //; s/^refused: (.*)/refused \1/' | paste -sd ',' - | sed 's/,/, /g'
}

# cell_says <result-file>: the question the run ended with, else the first line
# of its final message.
cell_says() {
  local line
  line="$(final_question "$1" || true)"
  [ -n "$line" ] || line="$(section "$1" "final message" | grep -v '^[[:space:]]*$' | head -n 1 | cut -c1-220 || true)"
  printf '%s' "$line" | sed 's/|/\\|/g'
}

# write_summary <out-dir>: one row per cell.
write_summary() {
  local out="$1" result name verdict
  {
    echo "| Cell | Verdict | Model | Seconds | Cost or tokens | Expected artifact | Tools called | The run ended with |"
    echo "| --- | --- | --- | --- | --- | --- | --- | --- |"
    for result in "$out"/*.txt; do
      [ -f "$result" ] && is_cell "$result" || continue
      name="$(basename "$result" .txt)"
      verdict="$(cell_verdict "$result")"
      if [ "$verdict" = not-run ]; then
        echo "| $name | not-run | | | | $(sed -n 's/^# not-run: //p' "$result") | | |"
        continue
      fi
      if ! grep -q '^# --- agent exit: ' "$result"; then
        echo "| $name | no Agent in this cell: exit $(cell_exit "$result") | | | | | | |"
        continue
      fi
      echo "| $name | $verdict | $(cell_model "$result") | $(cell_seconds "$result") | $(cell_cost "$result") | $(cell_artifact "$result" | sed 's/|/\\|/g') | $(cell_tools "$result") | $(cell_says "$result") |"
    done
  } > "$out/summary.md"
  cat "$out/summary.md"
}
