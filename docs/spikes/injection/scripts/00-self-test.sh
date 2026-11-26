#!/usr/bin/env bash
# Self-test of the spike harness. It needs no credential and checks that the
# harness itself behaves as the report assumes:
#
#   1. a binary whose checksum differs from the pin is refused and nothing
#      reaches the agents volume;
#   2. a script run without credentials records its cells not-run, goes on, and
#      exits 0, and a credential exported in the calling shell is not used;
#   3. credential values never reach a result file, whatever their shape;
#   4. planted configuration that executes is detected; the hardened invocation
#      leaves no canary in a cell that could have shown one; and a cell that
#      could not show anything is not mistaken for "nothing executed";
#   5. a request that would leave without the proxy is detected as failing;
#   6. a command that fails in a cell is recorded with its exit code and the
#      harness goes on.
#
# Usage: bash 00-self-test.sh   (run 01-fetch-agents.sh first)

# shellcheck source=lib.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

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

require_agents_volume
build_images debian alpine
build_tools_image
image="$(image_name debian)"
agent="$SCRIPT_DIR/container/run-agent.sh"

# --- 1. Checksum mismatch -------------------------------------------------------
bad_volume="$PREFIX-agents-selftest"
docker volume rm --force "$bad_volume" > /dev/null 2>&1 || true
grep '^codex' "$LOCK_FILE" | sed -E 's/ [0-9a-f]{8}([0-9a-f]{56}) / 00000000\1 /' > "$work/bad.lock"
fetch_code=0
SPIKE_LOCK_FILE="$work/bad.lock" SPIKE_AGENTS_VOLUME="$bad_volume" SPIKE_RESULTS_DIR="$work/fetch-results" \
  bash "$SCRIPT_DIR/01-fetch-agents.sh" > "$work/fetch.out" 2>&1 || fetch_code=$?
tr -d '\r' < "$work/fetch.out" > "$out/checksum-mismatch.txt"
check "checksum mismatch: fetch exits non-zero" test "$fetch_code" -ne 0
check "checksum mismatch: the file is named" grep -q '^checksum mismatch: https://' "$out/checksum-mismatch.txt"
files="$(docker run --rm --mount "type=volume,source=$bad_volume,target=/a" "$HELPER_IMAGE" find /a -type f | wc -l | tr -d ' \r')"
check "checksum mismatch: nothing was placed in the agents volume" test "$files" = 0
docker volume rm --force "$bad_volume" > /dev/null 2>&1 || true

# --- 2. Credential absent --------------------------------------------------------
# The real script, with no .env, and with a key exported in the calling shell.
absent_code=0
ANTHROPIC_API_KEY="ambient-key-that-must-not-be-used" SPIKE_ENV_FILE="$work/no-such.env" \
  SPIKE_RESULTS_DIR="$work/absent-results" SPIKE_ONLY="" \
  bash "$SCRIPT_DIR/03-planted-config.sh" claude > "$work/absent.out" 2>&1 || absent_code=$?
cells="$(find "$work/absent-results/03-planted-config" -name '*.txt' 2> /dev/null | wc -l | tr -d ' ')"
not_run_cells="$(grep -l '^# not-run:' "$work/absent-results/03-planted-config"/*.txt 2> /dev/null | wc -l | tr -d ' ')"
{ echo "exit: $absent_code; cells: $cells; not-run: $not_run_cells"; tr -d '\r' < "$work/absent.out"; } > "$out/credential-absent.txt"
check "credential absent: the script goes on and exits 0" test "$absent_code" = 0
check "credential absent: every cell is recorded not-run" test "$cells" -gt 0 -a "$cells" = "$not_run_cells"
check "credential absent: a key from the calling shell is not used" absent "ambient-key" "$out/credential-absent.txt"

# --- 3. Masking -------------------------------------------------------------------
# Values that are not shaped like a key or a token, so that only masking by
# value can remove them; written by run_cell, as every result is.
opaque_key="opaque.selftest.KEY.0123456789abcdef"
opaque_token="rt.selftest.OPAQUE.0123456789abcdef"
ws="$(new_workspace selftest)"
(
  export ANTHROPIC_API_KEY="$opaque_key"
  CODEX_AUTH_JSON_B64="$(printf '{"tokens":{"refresh_token":"%s"}}' "$opaque_token" | base64 | tr -d '\r\n')"
  export CODEX_AUTH_JSON_B64
  run_cell "$out/masking.txt" "$image" "$ws" --env ANTHROPIC_API_KEY --env CODEX_AUTH_JSON_B64 -- bash -c '
    echo "key=$ANTHROPIC_API_KEY"
    echo "file=$(printf "%s" "$CODEX_AUTH_JSON_B64" | base64 -d)"
    echo "quoted by a vendor: sk-proj-****************abcd"
    u=37; echo "{\"utilization\":0.$u}"
    echo "ordinary text: disk-full-read-access"
  ' < /dev/null
)
check "masking: a value from .env does not reach a result" absent "$opaque_key" "$out/masking.txt"
check "masking: a value from a credential file does not reach a result" absent "$opaque_token" "$out/masking.txt"
check "masking: a key quoted in part by a vendor is removed" absent "sk-proj-" "$out/masking.txt"
check "masking: the utilization figure is removed" absent "0.37" "$out/masking.txt"
check "masking: ordinary text is left alone" present "disk-full-read-access" "$out/masking.txt"

# --- 4. Planted configuration ------------------------------------------------------
ws="$(PLANT_APIKEYHELPER=1 plant_workspace selftest-claude claude)"
mapfile -t args < <(claude_args control api-key "$TOOL_PROMPT")
run_cell "$out/claude-control.txt" "$image" "$ws" --env "ANTHROPIC_API_KEY=$DUMMY_ANTHROPIC_API_KEY" --env MCP_TIMEOUT=5000 \
  -- bash -s -- "$(claude_bin debian)" "${args[@]}" < "$agent"
check "planted configuration: Claude Code control run leaves canaries" test -n "$(cell_canaries "$out/claude-control.txt")"

ws="$(PLANT_APIKEYHELPER=1 plant_workspace selftest-claude claude)"
mapfile -t args < <(claude_args hardened api-key "$TOOL_PROMPT")
mapfile -t env < <(claude_variant_env hardened)
run_cell "$out/claude-hardened.txt" "$image" "$ws" --env "ANTHROPIC_API_KEY=$DUMMY_ANTHROPIC_API_KEY" "${env[@]}" \
  -- bash -s -- "$(claude_bin debian)" "${args[@]}" < "$agent"
check "planted configuration: Claude Code hardened cell could have shown a canary" test "$(cell_evidence "$out/claude-hardened.txt" planted)" = complete
check "planted configuration: Claude Code hardened run leaves none" test -z "$(cell_canaries "$out/claude-hardened.txt")"
check "tool detection: a run that executed nothing is not read as having run the tool" absent "tool-42" "$out/claude-hardened.txt"

mapfile -t env < <(codex_ca_env)
ws="$(plant_workspace selftest-codex codex)"
mapfile -t args < <(codex_args control-trusted "$PONG_PROMPT")
run_cell "$out/codex-control-trusted.txt" "$image" "$ws" --env "CODEX_API_KEY=$DUMMY_OPENAI_API_KEY" "${env[@]}" --env SPIKE_TIMEOUT=90 \
  -- bash -s -- "$(codex_bin)" "${args[@]}" < "$agent"
check "planted configuration: Codex trusted control run leaves canaries" test -n "$(cell_canaries "$out/codex-control-trusted.txt")"

ws="$(plant_workspace selftest-codex codex)"
mapfile -t args < <(codex_args hardened "$PONG_PROMPT")
run_cell "$out/codex-hardened.txt" "$image" "$ws" --env "CODEX_API_KEY=$DUMMY_OPENAI_API_KEY" "${env[@]}" --env SPIKE_TIMEOUT=90 \
  -- bash -s -- "$(codex_bin)" "${args[@]}" < "$agent"
check "planted configuration: Codex hardened cell could have shown a canary" test "$(cell_evidence "$out/codex-hardened.txt" planted)" = complete
check "planted configuration: Codex hardened run leaves none" test -z "$(cell_canaries "$out/codex-hardened.txt")"

# A cell in which the CLI never ran has no canaries either. It must not count.
ws="$(new_workspace selftest)"
run_cell "$out/cli-never-started.txt" "$image" "$ws" -- bash -s -- /hephaestus/agents/claude/no-such-build/claude -p hello < "$agent"
check "evidence: a cell whose CLI never started is not complete" test "$(cell_evidence "$out/cli-never-started.txt" planted)" != complete
check "evidence: a hardened dummy-key cell cannot vouch for tool-time hooks" test "$(cell_evidence "$out/claude-hardened.txt" planted probe)" != complete

# --- 5. A request without the proxy ---------------------------------------------------
start_proxy
mapfile -t net < <(network_args)
run_cell "$out/no-proxy.txt" "$(image_name alpine)" "$ws" "${net[@]}" \
  -- sh -c 'wget -T 8 -q -O /dev/null https://api.anthropic.com/; echo "direct: $?"' < /dev/null
# The failure must be a network failure, not an HTTP answer from the vendor.
check "no proxy: a direct request from the workspace network cannot connect" \
  grep -Eq 'bad address|Network unreachable|timed out' "$out/no-proxy.txt"

# --- 6. A command that fails ---------------------------------------------------------
run_cell "$out/failing-command.txt" "$image" "$ws" -- bash -c 'exit 3' < /dev/null
check "failing command: its exit code is recorded" test "$(cell_exit "$out/failing-command.txt")" = 3
run_cell "$out/failing-cli.txt" "$image" "$ws" -- /hephaestus/agents/claude/no-such-build/claude --version < /dev/null
code="$(cell_exit "$out/failing-cli.txt")"
check "failing CLI: a non-zero exit code is recorded" test -n "$code" -a "$code" != 0

echo "---" >> "$report"
echo "failures: $failures" | tee -a "$report"
[ "$failures" -eq 0 ]
