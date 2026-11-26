#!/usr/bin/env bash
# Self-test of the BMAD spike's harness. It needs no credential and no model:
# a stand-in for an Agent CLI (container/fake-agent.sh) prints a stream and
# behaves as each scenario asks, in the same hardened container, through the
# same wrapper, and the harness judges it as it judges a real run.
#
# It covers every row of the story spec's I/O and edge-case matrix:
#
#   1. Credential absent: the cell scripts record their cells not-run, go on,
#      and exit 0; a credential exported in the calling shell is not used.
#   2. Workflow asks a question: a final message that is a question, or a menu,
#      is judged halted-for-input, and the question is recorded.
#   3. Run exceeds its deadline: judged timeout, and the partial stream is kept.
#   4. Workflow writes to the Methodology mount: the write fails, the path and
#      the error are recorded, and the mount is unchanged.
#   5. Workflow claims success and changed nothing: judged no-artifact.
#
# and what the verdicts rest on: a run that did the work is judged completed, a
# CLI that failed is judged error, a review is not credited with text that was
# in the repository already or with findings its final message does not make, a
# list of the possible readiness verdicts is not taken for a verdict, the cost
# and the token count are read from both stream shapes, the language measure
# tells Polish from English, and a credential a run prints never reaches a
# result.
#
# Usage: bash 00-self-test.sh   (run 01-prepare.sh first, or let this script do it)

# shellcheck source=lib-bmad.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib-bmad.sh"

out="$RESULTS_DIR/00-self-test"
work="$SPIKE_TMP/self-test"
mkdir -p "$out" "$work"
report="$out/self-test.txt"
printf 'harness: %s\n' "$(harness_id)" > "$report"
failures=0

check() { # <description> <command...>
  local description="$1"
  shift
  if "$@" > /dev/null 2>&1; then
    echo "PASS  $description" | tee -a "$report"
  else
    echo "FAIL  $description" | tee -a "$report"
    failures=$((failures + 1))
  fi
}
absent() { ! grep -qF -- "$1" "$2"; }   # <text> <file>
present() { grep -qF -- "$1" "$2"; }
verdict_is() { [ "$(cell_verdict "$2")" = "$1" ]; }   # <verdict> <file>
in_section() { section "$1" "$2" | grep -qE -- "$3"; }   # <file> <section> <pattern>

# recast <source> <destination> <expectation> <artifact-line> <final-message>
# A recorded result with another final message and another artifact record:
# what the wrapper writes in the case a rule of verdicts.sh is about, without a
# model to produce the case.
recast() {
  awk -v expectation="$3" -v check="$4" -v final="$5" '
    /^# --- final message ---$/ { print; print final; skip = 1; next }
    /^# --- expected artifact ---$/ { print; print "expectation: " expectation; print check; skip = 1; next }
    /^# --- end (final message|expected artifact) ---$/ { skip = 0 }
    !skip { print }
  ' "$1" > "$2"
}

ensure_prepared
unset "${CREDENTIAL_VARS[@]}" CODEX_API_KEY

# fake_cell <scenario> <branch> <expect> <deadline> [docker-run-arg...]
# One run of the stand-in, with no network at all.
fake_cell() {
  local scenario="$1" branch="$2" expect="$3" deadline="$4" ws ctx mounts=()
  shift 4
  ws="$(sample_workspace "selftest" "$branch")" || { log "cannot prepare a workspace"; exit 1; }
  ctx="$(new_context selftest "fake-agent.sh=$BMAD_DIR/container/fake-agent.sh")" || { log "cannot prepare the context"; exit 1; }
  mapfile -t mounts < <(methodology_args; context_args "$ctx")
  run_cell "$out/$scenario.txt" "$BMAD_IMAGE" "$ws" "${mounts[@]}" --network none \
    --env "SPIKE_TIMEOUT=$deadline" --env "SPIKE_EXPECT=$expect" "$@" \
    -- bash -s -- bash /hephaestus/context/fake-agent.sh "$scenario" < "$BMAD_DIR/container/run-workflow.sh"
  drop_volumes "$ws" "$ctx"
}

# --- 1. Credential absent ----------------------------------------------------------
# The real cell scripts, with no .env, and with a key exported in the calling shell.
for script in 02-run-workflows 03-which-copy 04-language; do
  code=0
  ANTHROPIC_API_KEY="ambient-key-that-must-not-be-used" OPENAI_API_KEY="ambient-key-that-must-not-be-used" \
    SPIKE_ENV_FILE="$work/no-such.env" SPIKE_RESULTS_DIR="$work/absent" SPIKE_ONLY="" SPIKE_STREAM="" \
    bash "$BMAD_DIR/$script.sh" > "$work/$script.out" 2>&1 || code=$?
  cells="$(find "$work/absent/$script" -name '*.txt' 2> /dev/null | wc -l | tr -d ' ')"
  not_run_cells="$(grep -l '^# not-run:' "$work/absent/$script"/*.txt 2> /dev/null | wc -l | tr -d ' ')"
  { echo "exit: $code; cells: $cells; not-run: $not_run_cells"; tr -d '\r' < "$work/$script.out"; } > "$out/credential-absent-$script.txt"
  check "credential absent: $script.sh goes on and exits 0" test "$code" = 0
  check "credential absent: $script.sh records every cell not-run" test "$cells" -gt 0 -a "$cells" = "$not_run_cells"
  check "credential absent: $script.sh does not use a key from the calling shell" absent "ambient-key" "$out/credential-absent-$script.txt"
  check "credential absent: $script.sh says so in its summary" grep -q 'not-run' "$work/absent/$script/summary.md"
done

# --- 2. Workflow asks a question ------------------------------------------------------
fake_cell asks-question task/low-stock implementation 60
check "question: a final message that is a question is judged halted-for-input" verdict_is halted-for-input "$out/asks-question.txt"
check "question: the question is recorded" test "$(final_question "$out/asks-question.txt")" = "Which one do you want?"
fake_cell asks-menu task/low-stock implementation 60
check "question: a menu that waits for a choice is judged halted-for-input" verdict_is halted-for-input "$out/asks-menu.txt"
check "question: the menu's request is recorded" grep -q 'waiting for your numbered choice' <<< "$(final_question "$out/asks-menu.txt")"

fake_cell asks-in-a-paragraph task/low-stock-implemented review-findings 60
check "question: a question inside the last paragraph is judged halted-for-input" verdict_is halted-for-input "$out/asks-in-a-paragraph.txt"
check "question: that question is recorded" grep -q '^should the workflow continue past the checkpoint in this unattended run?$' <<< "$(final_question "$out/asks-in-a-paragraph.txt")"
fake_cell ends-with-data task/low-stock none 60
check "question: a question mark inside data is not judged a question" verdict_is completed "$out/ends-with-data.txt"
fake_cell states-a-block task/low-stock ready-spec 60
check "question: a halt that states what is missing, and asks nothing, is not judged halted-for-input" verdict_is no-artifact "$out/states-a-block.txt"

# --- 3. Run exceeds its deadline --------------------------------------------------------
fake_cell deadline task/low-stock implementation 3
check "deadline: a run stopped at its deadline is judged timeout" verdict_is timeout "$out/deadline.txt"
check "deadline: the partial stream is kept" present '"subtype":"init"' "$out/deadline.txt"
check "deadline: the stopped run did not reach its end" absent "never reached" "$out/deadline.txt"

# --- 4. Workflow writes to the Methodology mount -----------------------------------------
fake_cell writes-to-methodology task/low-stock ready-spec 60
check "methodology: the write fails and the run is not judged completed" verdict_is no-artifact "$out/writes-to-methodology.txt"
check "methodology: the refused path and the error are recorded" in_section "$out/writes-to-methodology.txt" methodology \
  "^refused: mkdir: cannot create directory '/hephaestus/methodology/_bmad/render/bmad-build-auto': Read-only file system\$"
check "methodology: the refusal is counted" in_section "$out/writes-to-methodology.txt" methodology '^writes refused as read-only, in the output of the tools: 1$'
check "methodology: the mount is unchanged" in_section "$out/writes-to-methodology.txt" methodology '^mount: unchanged by the run$'
# The real renderer, as 01-prepare.sh recorded it.
check "methodology: BMAD's renderer is refused when its project root is the mount" \
  grep -q "^HALT: .*Read-only file system: '/hephaestus/methodology/_bmad/render" "$RESULTS_DIR/01-prepare/scripts-render-project-root-methodology.txt"

# --- 5. Workflow claims success and changed nothing ---------------------------------------
fake_cell claims-success task/low-stock implementation 60
check "no artifact: a run that claims success and changed nothing is judged no-artifact" verdict_is no-artifact "$out/claims-success.txt"
check "no artifact: the missing artifact is named" in_section "$out/claims-success.txt" "expected artifact" '^missing: the acceptance probe of Story 1.2 passes'
check "no artifact: no change in the workspace is recorded" test -z "$(section "$out/claims-success.txt" "workspace changes")"

# --- What the verdicts rest on ---------------------------------------------------------------
fake_cell does-the-work task/low-stock implementation 60
check "completed: a run that did the work is judged completed" verdict_is completed "$out/does-the-work.txt"
check "completed: the changed files are recorded" in_section "$out/does-the-work.txt" "workspace changes" '^changed shelf/inventory.py$'
# The same result, had the workflow recorded its own halt in the spec it wrote.
sed 's/^\(info: spec files written: .*\) is done$/\1 is blocked/' "$out/does-the-work.txt" > "$work/halted-itself.txt"
check "blocked: a run whose workflow recorded its own halt is not judged completed" verdict_is no-artifact "$work/halted-itself.txt"
check "cost: the token count of a Codex stream is read" test "$(cell_cost "$out/does-the-work.txt")" = "4000 in (1000 cached), 70 out"
check "cost: the cost and the token count of a Claude Code stream are read" test "$(cell_cost "$out/claims-success.txt")" = '$0.1234; 1500 in, 50 out'

fake_cell fails task/low-stock implementation 60
check "error: a CLI that exits non-zero is judged error" verdict_is error "$out/fails.txt"
check "error: the failure the stream reports is recorded" in_section "$out/fails.txt" usage '^stream error: result error_during_execution'

fake_cell touches-the-spec task/low-stock-implemented review-findings 60
check "review: text that was in the repository before the run is not counted as a finding" verdict_is no-artifact "$out/touches-the-spec.txt"

# Two rules of verdicts.sh about an artifact the wrapper recorded as present,
# each in both directions, on a completed result recast as the case in question.
seeded="ok: findings that report a seeded defect (the threshold boundary, the negative threshold)"
recast "$out/does-the-work.txt" "$work/review-with-findings.txt" review-findings "$seeded" \
  'F1. low_stock compares with < where the spec requires <=, so an item exactly at the threshold is left out.'
check "review: findings made in the final message are counted" verdict_is completed "$work/review-with-findings.txt"
recast "$out/does-the-work.txt" "$work/review-without-findings.txt" review-findings "$seeded" \
  'Code review did not run to findings. I wrote the four reviewer prompts, each with the diff, to files. No findings were produced.'
check "review: a seeded defect found only in the files a run wrote is not counted" verdict_is no-artifact "$work/review-without-findings.txt"
check "review: the summary says why it is not counted" grep -q 'not counted: the final message reports no seeded defect' <<< "$(cell_artifact "$work/review-without-findings.txt")"

recast "$out/does-the-work.txt" "$work/readiness-stated.txt" readiness-verdict \
  "ok: a readiness verdict in the final result (CONCERNS, stated in a sentence, not in the documented JSON)" \
  'Readiness gate verdict: CONCERNS. Story 1.2 does not say whether a threshold of zero is valid.'
check "readiness: a verdict stated in a sentence is counted" verdict_is completed "$work/readiness-stated.txt"
recast "$out/does-the-work.txt" "$work/readiness-menu.txt" readiness-verdict \
  "ok: a readiness verdict in the final result (FAIL, stated in a sentence, not in the documented JSON)" \
  '- readiness: run the gate and report PASS/CONCERNS/FAIL. Pick one of the intents and I will proceed.'
check "readiness: a line that lists the possible verdicts is not counted as one" verdict_is no-artifact "$work/readiness-menu.txt"
recast "$out/does-the-work.txt" "$work/readiness-json.txt" readiness-verdict \
  "ok: a readiness verdict in the final result (PASS, in the JSON the workflow documents)" \
  'The gate can answer PASS, CONCERNS, or FAIL. {"status": "complete", "intent": "readiness", "gate": "PASS"}'
check "readiness: a verdict in the documented JSON is counted whatever else the message lists" verdict_is completed "$work/readiness-json.txt"
check "review: a report that names the code and no defect is not counted as a finding" in_section "$out/asks-in-a-paragraph.txt" "expected artifact" '^missing: findings that report a seeded defect'

fake_cell writes-polish task/low-stock none 60
polish="$(section "$out/writes-polish.txt" language | sed -n 's/^final message: [0-9]* letters, \([0-9]*\) Polish.*/\1/p')"
check "language: a Polish final message is measured as Polish" test "${polish:-0}" -ge 15
english="$(section "$out/asks-question.txt" language | sed -n 's/^final message: [0-9]* letters, \([0-9]*\) Polish.*/\1/p')"
check "language: an English final message is measured as not Polish" test "${english:-1}" = 0

# A value that is not shaped like a key, so that only removal by value can hide it.
opaque="opaque.selftest.KEY.0123456789abcdef"
(
  export ANTHROPIC_API_KEY="$opaque"
  fake_cell leaks-a-credential task/low-stock none 60 --env ANTHROPIC_API_KEY
)
check "masking: a credential a run prints does not reach the result" absent "$opaque" "$out/leaks-a-credential.txt"
check "masking: no part of it is left where a long line was cut" absent "opaque.selftest" "$out/leaks-a-credential.txt"
check "masking: the run itself is recorded" present "The key is" "$out/leaks-a-credential.txt"

echo "---" >> "$report"
echo "failures: $failures" | tee -a "$report"
[ "$failures" -eq 0 ]
