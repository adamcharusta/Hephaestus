#!/bin/bash
# A stand-in for an Agent CLI, for the self-test only. It prints a stream in the
# shape of `claude -p --output-format stream-json` or of `codex exec --json`
# and behaves as one scenario asks, so that the harness can be checked with no
# credential and no model.
#
# Usage: bash fake-agent.sh <scenario>
set -u

json() { python3 -c 'import json, sys; print(json.dumps(json.loads(sys.argv[1]) | {sys.argv[2]: sys.argv[3]}))' "$@"; }

claude_init() {
  echo '{"type":"system","subtype":"init","cwd":"/workspace","tools":["Bash","Edit","Read","Write"],"model":"fake-model"}'
}

# claude_result <text>: the final event of a Claude Code stream.
claude_result() {
  json '{"type":"result","subtype":"success","is_error":false,"num_turns":3,"total_cost_usd":0.1234,"usage":{"input_tokens":1000,"cache_creation_input_tokens":300,"cache_read_input_tokens":200,"output_tokens":50}}' result "$1"
}

# claude_tool <command>: a Bash tool call and its result, really executed.
claude_tool() {
  local output
  python3 -c 'import json, sys; print(json.dumps({"type": "assistant", "message": {"id": "msg_1", "content": [{"type": "tool_use", "id": "toolu_1", "name": "Bash", "input": {"command": sys.argv[1]}}]}}))' "$1"
  output="$(bash -c "$1" 2>&1)"
  python3 -c 'import json, sys; print(json.dumps({"type": "user", "message": {"role": "user", "content": [{"type": "tool_result", "tool_use_id": "toolu_1", "is_error": False, "content": sys.argv[1]}]}}))' "$output"
}

codex_start() { echo '{"type":"thread.started","thread_id":"fake"}'; echo '{"type":"turn.started"}'; }

# codex_end <text>: the final message and the usage of a Codex stream.
codex_end() {
  python3 -c 'import json, sys; print(json.dumps({"type": "item.completed", "item": {"id": "item_9", "type": "agent_message", "text": sys.argv[1]}}))' "$1"
  echo '{"type":"turn.completed","usage":{"input_tokens":4000,"cached_input_tokens":1000,"output_tokens":70}}'
}

case "${1:-}" in
  asks-question)
    claude_init
    claude_result "$(printf 'I read the Task. Two readings are possible.\n\n- A: items at the threshold are listed.\n- B: only items below it.\n\nWhich one do you want?')"
    ;;
  asks-in-a-paragraph)
    # A workflow's checkpoint, reported by an Agent that was told not to ask:
    # the question is inside the last paragraph, not at its end.
    claude_init
    claude_result "$(printf 'The review is stopped at the checkpoint of step 1. The diff is ready, 3 files changed (`shelf/inventory.py`, `tests/test_inventory.py`, and the spec file). No code was changed.\n\n**Decision needed:** should the workflow continue past the checkpoint in this unattended run? If yes, the next step launches the reviewers. Confirm that, and I will run the review.')"
    ;;
  ends-with-data)
    # Findings as JSON, one of which contains a question mark: data, not a question.
    codex_start
    codex_end "$(printf '[\n  {"lens": "edge-case-hunter", "location": "shelf/inventory.py", "trigger_condition": "what if the threshold is negative?"}\n]')"
    ;;
  asks-menu)
    codex_start
    codex_end "$(printf 'Review complete. 2 patch findings.\n\n1. Apply every patch\n2. Leave them as action items\n\n**HALT** — I am waiting for your numbered choice. Reply with only the number.')"
    ;;
  deadline)
    claude_init
    sleep 60
    claude_result "never reached"
    ;;
  writes-to-methodology)
    claude_init
    claude_tool 'mkdir -p /hephaestus/methodology/_bmad/render/bmad-build-auto && echo written'
    claude_result 'Status: blocked. Blocking condition: the snapshot directory cannot be created.'
    ;;
  states-a-block)
    # A workflow's own halt: a statement of what is missing, and no question.
    claude_init
    claude_result "$(printf 'Status: blocked. Blocking condition: no subagents.\n\nTo unblock: run where subagents can be launched. Whether to do that is your call.')"
    ;;
  claims-success)
    claude_init
    claude_result 'Story 1.2 is implemented and reviewed. All tests pass. Status: done.'
    ;;
  does-the-work)
    codex_start
    python3 - <<'PY'
import pathlib

path = pathlib.Path("/workspace/shelf/inventory.py")
method = '''    def low_stock(self, threshold):
        """The names of the items whose quantity is at or below `threshold`."""
        if threshold < 0:
            raise ValueError("threshold must not be negative")
        return sorted(name for name, quantity in self._items.items() if quantity <= threshold)

'''
path.write_text(path.read_text().replace("    def names(self):", method + "    def names(self):", 1))
spec = pathlib.Path("/workspace/_bmad-output/implementation-artifacts/spec-1-2-low-stock.md")
spec.parent.mkdir(parents=True, exist_ok=True)
spec.write_text("---\ntitle: 'Story 1.2'\nstatus: 'done'\n---\n\n## Auto Run Result\n\nStatus: done\n")
PY
    codex_end 'Status: done.'
    ;;
  touches-the-spec)
    # A reviewer that changes the story spec without one word about the code.
    claude_init
    sed -i "s/^status: 'in-review'/status: 'done'/" /workspace/_bmad-output/implementation-artifacts/spec-1-2-low-stock.md
    claude_result 'Review complete. No findings. Status: done.'
    ;;
  leaks-a-credential)
    claude_init
    # Lines longer than the cut, with the key at a different place in each, so
    # that it lies across the cut in at least one; and the key in the result.
    for pad in $(seq 250 5 420); do
      claude_tool "printf '%0${pad}d' 0; printf '%s' \"\$ANTHROPIC_API_KEY\"; printf '%0900d' 0"
    done
    claude_result "The key is $ANTHROPIC_API_KEY."
    ;;
  writes-polish)
    codex_start
    # A Polish pangram, written with escapes so that this file stays ASCII.
    codex_end "$(printf 'Za\xc5\xbc\xc3\xb3\xc5\x82\xc4\x87 g\xc4\x99\xc5\x9bl\xc4\x85 ja\xc5\xba\xc5\x84.')"
    ;;
  fails)
    claude_init
    echo '{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":1,"result":"the vendor refused the request"}'
    exit 3
    ;;
  *)
    echo "unknown scenario: ${1:-}" >&2
    exit 2
    ;;
esac
