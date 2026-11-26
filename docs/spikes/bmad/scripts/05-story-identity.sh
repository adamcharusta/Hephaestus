#!/usr/bin/env bash
# How BMAD itself identifies a Story, and what survives when Stories are
# renumbered or retitled (input to FR-66). No credential and no model: BMAD's
# own deterministic script, sprint_plan.py, derives the tracking key of every
# Story from the epics document, and reports what it can no longer match.
#
# The sample's epics document is changed three ways, and each time the script
# is asked what it sees against the tracking file generated before the change:
#
#   retitled    Story 1.2 gets another title
#   renumbered  a new Story is inserted before it, so it becomes Story 1.3
#   marked      each Story gets a line of its own under its heading that
#               carries an id, and nothing else changes
#
# sprint_plan.py declares a dependency, which `uv run` fetches through the proxy.
#
# Usage: bash 05-story-identity.sh

# shellcheck source=lib-bmad.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib-bmad.sh"

out="$RESULTS_DIR/05-story-identity"
mkdir -p "$out"
ensure_prepared
start_proxy
mapfile -t args < <(methodology_args; network_args; proxy_env_args)
ws="$(sample_workspace identity task/low-stock)"

run_cell "$out/story-keys.txt" "$BMAD_IMAGE" "$ws" "${args[@]}" -- bash -c '
  plan=/hephaestus/methodology/skills/bmad-sprint-planning/scripts/sprint_plan.py
  epics=/workspace/_bmad-output/planning-artifacts/epics.md
  status=/tmp/sprint-status.yaml
  generate() { # <epics-file> [option...]
    local file="$1"
    shift
    uv run --quiet "$plan" generate --epic-file "$file" --status-file "$status" \
      --stories-dir /workspace/_bmad-output/implementation-artifacts --project Shelf --date "11-26-2026 09:00" "$@"
  }
  show() { # the fields of the JSON report that answer the question
    python3 -c "
import json, sys
report = json.load(sys.stdin)
for field in (\"in_sync\", \"new_entries\", \"preserved\", \"dropped_orphans\", \"warnings\"):
    print(\"  %s: %s\" % (field, json.dumps(report.get(field))))"
  }

  echo "--- the tracking file BMAD generates, with Story 1.2 in progress ---"
  generate "$epics" > /dev/null
  generate "$epics" --set 1-2-report-items-that-are-low-on-stock=in-progress > /dev/null
  grep -E "^  [0-9a-z-]+: " "$status"

  echo "--- retitled: Story 1.2 is now \"List items to reorder\" ---"
  sed "s/^### Story 1.2: .*/### Story 1.2: List items to reorder/" "$epics" > /tmp/retitled.md
  generate /tmp/retitled.md --dry-run | show

  echo "--- renumbered: a new Story 1.2 is inserted, the old one becomes 1.3 ---"
  awk "/^### Story 1.2: / { print \"### Story 1.2: Count the items\"; print \"\"; print \"As a shop keeper, I want a count.\"; print \"\"; sub(/1\\.2/, \"1.3\") } { print }" "$epics" > /tmp/renumbered.md
  grep "^### Story" /tmp/renumbered.md
  generate /tmp/renumbered.md --dry-run | show

  echo "--- marked: an id on a line of its own under each Story heading ---"
  awk "{ print } /^### Story 1.1: / { print \"\"; print \"<!-- story-id: 0193f1c2-aaaa -->\" } /^### Story 1.2: / { print \"\"; print \"<!-- story-id: 0193f1c2-bbbb -->\" }" "$epics" > /tmp/marked.md
  grep -A2 "^### Story" /tmp/marked.md
  generate /tmp/marked.md --dry-run | show

  echo "--- marked in the heading itself ---"
  sed "s/^\(### Story 1.2: .*\)$/\1 <!-- story-id: 0193f1c2-bbbb -->/" "$epics" > /tmp/marked-heading.md
  grep "^### Story 1.2" /tmp/marked-heading.md
  generate /tmp/marked-heading.md --dry-run | show
' < /dev/null
drop_volumes "$ws"
sed -n '/^# --- stdout ---$/,$p' "$out/story-keys.txt"
