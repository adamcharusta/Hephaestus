#!/usr/bin/env bash
# Shared definitions for the BMAD spike (Story 1.2). Sourced, never run.
#
# It sources the injection spike's library unchanged and takes from it the step
# container definition (hardened_args, run_cell), the hardened invocations
# (claude_args, codex_args), credential loading, masking, and the egress proxy.
# What it adds is specific to running BMAD workflows:
#
#   - the Methodology volume, mounted read-only at /hephaestus/methodology;
#   - the sample repository, generated in a volume, and a copy of it per cell;
#   - the context volume of a step run, mounted read-only at /hephaestus/context;
#   - the verdict of a cell, judged from its exit code, the stream's final
#     result, and the files the workflow left in /workspace;
#   - the cost or token count of a cell, and the summary table.
#
# Every Docker object is named "hephaestus-spike-*" and carries the injection
# spike's label, because the container definition is taken as it is;
# 99-cleanup.sh removes what this spike created, by name.
#
# Credentials stay where Story 1.1 put them: docs/spikes/injection/scripts/.env.

BMAD_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export SPIKE_RESULTS_DIR="${SPIKE_RESULTS_DIR:-$BMAD_DIR/../results}"

# shellcheck source=../../injection/scripts/lib.sh
. "$BMAD_DIR/../../injection/scripts/lib.sh"

REPO_ROOT="$(cd "$BMAD_DIR/../../../.." && pwd)"

# A result names both harnesses: the injection library it ran on, and this
# spike's: the files every script shares and the script that wrote the result.
# A change to one numbered script therefore leaves the others' results current.
BMAD_CALLER="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"
BMAD_HARNESS_ID="$(cd "$BMAD_DIR" && { find lib-bmad.sh container images context -type f | LC_ALL=C sort | xargs cat; cat "$BMAD_CALLER" 2> /dev/null || true; } | sha256sum | cut -c1-12)"
harness_id() { echo "$HARNESS_ID+bmad-$BMAD_HARNESS_ID"; }

METHODOLOGY_VOLUME="$PREFIX-bmad-methodology"
SAMPLE_VOLUME="$PREFIX-bmad-sample"
BMAD_IMAGE="$PREFIX-image-bmad"
BMAD_GIT_IMAGE="$PREFIX-image-bmad-git"

# The prompt of every cell unless a cell says otherwise: one sentence that
# points at a file. No slash command, no skill name a CLI has to resolve.
DEFAULT_PROMPT='Read /hephaestus/context/instruction.md and follow it.'

# Models (Owner, 2026-11-26): every cell runs on the cheapest model first. A
# cell that did not complete is repeated on a stronger one, so that "fails on
# the small model" can be told apart from "fails".
BMAD_SMALL_CLAUDE_MODEL="haiku"
BMAD_SMALL_CODEX_MODEL="gpt-5-mini"
BMAD_STRONG_CLAUDE_MODEL="${BMAD_STRONG_CLAUDE_MODEL:-sonnet}"
BMAD_STRONG_CODEX_MODEL="${BMAD_STRONG_CODEX_MODEL:-gpt-5}"

# use_models <small|strong>: the models claude_args and codex_args will name.
use_models() {
  case "$1" in
    small) SPIKE_CLAUDE_MODEL="$BMAD_SMALL_CLAUDE_MODEL"; SPIKE_CODEX_MODEL="$BMAD_SMALL_CODEX_MODEL" ;;
    strong) SPIKE_CLAUDE_MODEL="$BMAD_STRONG_CLAUDE_MODEL"; SPIKE_CODEX_MODEL="$BMAD_STRONG_CODEX_MODEL" ;;
  esac
}

# With SPIKE_STREAM set, a script uses a proxy and a workspace network of its
# own, so that two scripts can run side by side and each cell still gets the
# hosts its own run asked for.
if [ -n "${SPIKE_STREAM:-}" ]; then
  WORKSPACE_NET="$PREFIX-workspace-$SPIKE_STREAM"
  PROXY_NAME="$PREFIX-proxy-$SPIKE_STREAM"
  PROXY_URL="http://$PROXY_NAME:3128"
fi

# --- Volumes -------------------------------------------------------------------

# fresh_volume <name>: an empty labelled volume, replacing one of the same name.
fresh_volume() {
  local attempt
  for attempt in 1 2 3 4 5 6 7 8 9 10; do
    docker volume inspect "$1" > /dev/null 2>&1 || break
    docker volume rm "$1" > /dev/null 2>&1 || sleep 1
  done
  if docker volume inspect "$1" > /dev/null 2>&1; then
    log "cannot remove volume $1"
    return 1
  fi
  docker volume create --label "$SPIKE_LABEL" "$1" > /dev/null
}

# bmad_tar <path>...: a tar stream of this repository's tracked files under the
# paths. Only tracked files: personal BMAD configuration and rendered
# snapshots are git-ignored and stay out.
bmad_tar() {
  (cd "$REPO_ROOT" && git ls-files -z -- "$@" | tar --null --files-from=- --create --file=-)
}

# fill_methodology: this repository's installed BMAD, unchanged, as the
# Methodology of the sample Project: _bmad/ and the skills, which are the same
# 29 for both Agents.
fill_methodology() {
  fresh_volume "$METHODOLOGY_VOLUME" || return 1
  bmad_tar _bmad .claude/skills | docker run --rm --interactive --label "$SPIKE_LABEL" \
    --mount "type=volume,source=$METHODOLOGY_VOLUME,target=/methodology" "$HELPER_IMAGE" sh -euc '
      mkdir /tmp/in
      tar -x -f - -C /tmp/in
      cp -R /tmp/in/_bmad /methodology/_bmad
      cp -R /tmp/in/.claude/skills /methodology/skills
      chown -R 0:0 /methodology
      chmod -R a+rX,go-w /methodology'
}

# make_sample: the sample repository, with BMAD installed for both Agents.
make_sample() {
  fresh_volume "$SAMPLE_VOLUME" || return 1
  bmad_tar _bmad .claude/skills .agents/skills | docker run --rm --interactive --label "$SPIKE_LABEL" \
    --mount "type=volume,source=$SAMPLE_VOLUME,target=/workspace,volume-nocopy" \
    "$HELPER_IMAGE" tar -x -f - -C /workspace || return 1
  docker run --rm --interactive --label "$SPIKE_LABEL" \
    --mount "type=volume,source=$SAMPLE_VOLUME,target=/workspace,volume-nocopy" \
    "$TOOLS_IMAGE" sh -s < "$BMAD_DIR/container/make-sample.sh"
}

# build_bmad_images: the test image, and the same with git.
build_bmad_images() {
  log "building $BMAD_IMAGE and $BMAD_GIT_IMAGE"
  docker build --quiet --label "$SPIKE_LABEL" --tag "$BMAD_IMAGE" \
    - < "$BMAD_DIR/images/bmad.Dockerfile" > /dev/null
  docker build --quiet --label "$SPIKE_LABEL" --build-arg EXTRA_PACKAGES=git --tag "$BMAD_GIT_IMAGE" \
    - < "$BMAD_DIR/images/bmad.Dockerfile" > /dev/null
}

# ensure_prepared: the cell scripts need what 01-prepare.sh made.
ensure_prepared() {
  local missing=0 volume image
  for volume in "$AGENTS_VOLUME" "$METHODOLOGY_VOLUME" "$SAMPLE_VOLUME"; do
    docker volume inspect "$volume" > /dev/null 2>&1 || missing=1
  done
  for image in "$BMAD_IMAGE" "$BMAD_GIT_IMAGE" "$TOOLS_IMAGE"; do
    docker image inspect "$image" > /dev/null 2>&1 || missing=1
  done
  if [ "$missing" = 1 ]; then
    log "preparing: 01-prepare.sh"
    SPIKE_ONLY="" bash "$BMAD_DIR/01-prepare.sh" >&2 || { log "01-prepare.sh failed"; exit 1; }
  fi
}

# sample_workspace <name> <branch> [alteration...]: a fresh Task volume holding
# a copy of the sample repository on that branch. Prints the volume name.
sample_workspace() {
  local name="$1" branch="$2" volume
  shift 2
  volume="$(new_workspace "bmad-$name")" || return 1
  docker run --rm --interactive --label "$SPIKE_LABEL" --env "BRANCH=$branch" --env "ALTER=$*" \
    --mount "type=volume,source=$SAMPLE_VOLUME,target=/golden,readonly" \
    --mount "type=volume,source=$volume,target=/workspace,volume-nocopy" \
    "$TOOLS_IMAGE" sh -s < "$BMAD_DIR/container/checkout-sample.sh" >&2 || return 1
  echo "$volume"
}

# new_context <name> <name-in-volume>=<host-file>...: the context files of one
# step run, in a volume of their own. Prints the volume name.
new_context() {
  local volume="$PREFIX-bmad-ctx-$1" dir pair
  shift
  fresh_volume "$volume" || return 1
  dir="$(mktemp -d -p "$SPIKE_TMP")"
  for pair in "$@"; do cp "${pair#*=}" "$dir/${pair%%=*}" || return 1; done
  tar -C "$dir" --create --file=- . | docker run --rm --interactive --label "$SPIKE_LABEL" \
    --mount "type=volume,source=$volume,target=/context" "$HELPER_IMAGE" sh -euc '
      tar -x -f - -C /context
      chown -R 0:0 /context
      chmod -R a+rX,go-w /context' || return 1
  rm -rf "$dir"
  echo "$volume"
}

# drop_volumes <volume>...: remove the volumes of a finished cell; what a cell
# showed is in its result file.
drop_volumes() {
  [ "${BMAD_KEEP_VOLUMES:-0}" = 1 ] && return 0
  docker volume rm "$@" > /dev/null 2>&1 || true
}

methodology_args() {
  printf '%s\n' --mount "type=volume,source=$METHODOLOGY_VOLUME,target=/hephaestus/methodology,readonly"
}

context_args() {
  printf '%s\n' --mount "type=volume,source=$1,target=/hephaestus/context,readonly"
}

# --- Context files ---------------------------------------------------------------

# instruction <variant> <skill> <intent>: the instruction file of a step run, on
# stdout. It is the whole of how a workflow is started: hardened Claude Code has
# no Skill tool, and neither Agent is given the repository's instruction file.
#   split        BMAD's skills and scripts are taken from the read-only mount;
#                everything else of the project is under /workspace
#   bare         only the path of the skill file and the intent
#   mount-root   as split, but BMAD's scripts are also given the mount as their
#                project root, which is where they keep rendered snapshots
#   named        the workflow is named and no path is given
#   workspace:<dir>
#                the entry file is the repository's own copy, under
#                /workspace/<dir>/skills
instruction() {
  local variant="$1" skill="$2" intent="$3"
  local unattended='You are running unattended in a container. Nobody can answer a question, so never ask one and never wait for a reply. Where a workflow would ask, take what it documents for headless or unattended runs; where it documents nothing, stop and say what is undecided. Your final message is a report, not a turn in a conversation: end it without a question and without an offer of further work.'
  case "$variant" in
    bare)
      printf 'Read `/hephaestus/methodology/skills/%s/SKILL.md` and follow it.\n\n%s\n' "$skill" "$intent"
      return
      ;;
    named)
      printf '%s\n\nRun the BMAD workflow `%s`.\n\n%s\n' "$unattended" "$skill" "$intent"
      return
      ;;
    workspace:*)
      printf '%s\n\nRun the BMAD workflow `%s`.\n\n' "$unattended" "$skill"
      printf -- '- The entry file of the workflow is `/workspace/%s/skills/%s/SKILL.md`. Read it and follow it.\n' "${variant#workspace:}" "$skill"
      printf -- '- `{skill-root}` is `/workspace/%s/skills/%s`.\n' "${variant#workspace:}" "$skill"
      printf -- '- `{project-root}` is `/workspace`.\n\n%s\n' "$intent"
      return
      ;;
  esac
  cat <<MD
$unattended

Run the BMAD workflow \`$skill\`. BMAD's files are read-only at \`/hephaestus/methodology\`:

- The entry file of the workflow is \`/hephaestus/methodology/skills/$skill/SKILL.md\`. Read it and follow it.
- \`{skill-root}\` is \`/hephaestus/methodology/skills/$skill\`.
- BMAD's scripts are in \`/hephaestus/methodology/_bmad/scripts/\`. Run them from there wherever a workflow names \`{project-root}/_bmad/scripts/\`.
MD
  if [ "$variant" = mount-root ]; then
    cat <<MD
- BMAD's own project root is \`/hephaestus/methodology\`: pass it wherever a BMAD script takes \`--project-root\`.
- Every other \`{project-root}\` path is under \`/workspace\`, where the project is.
MD
  else
    cat <<MD
- \`{project-root}\` is \`/workspace\`, where the project is.
MD
  fi
  printf '\n%s\n' "$intent"
}

# task_json <step-kind> <branch> <language>: the Task's facts, on stdout.
task_json() {
  printf '{\n  "stepKind": "%s",\n  "branch": "%s",\n  "baseBranch": "main",\n  "round": 1,\n  "language": "%s"\n}\n' "$1" "$2" "$3"
}

# --- Running a cell --------------------------------------------------------------

# agent_cell <result> <cli> <image> <workspace> <context|-> <expect> <deadline> <prompt>
# One Agent run in the hardened container, on the workspace network behind the
# proxy, with the Methodology mounted read-only, wrapped by run-workflow.sh.
# The hosts the proxy was asked for are appended to the result.
agent_cell() {
  local result="$1" cli="$2" image="$3" ws="$4" ctx="$5" expect="$6" deadline="$7" prompt="$8"
  local args=() env=() base=() bin since
  cell_selected "$result" || return 0
  mapfile -t base < <(methodology_args; [ "$ctx" = - ] || context_args "$ctx"; network_args; proxy_env_args)
  if [ "$cli" = claude ]; then
    mapfile -t args < <(claude_args hardened api-key "$prompt")
    mapfile -t env < <(claude_credential_env api-key; claude_variant_env hardened)
    bin="$(claude_bin debian)"
  else
    prepare_codex_credential api-key
    # The hardened invocation, with the repository's AGENTS.md kept from the Agent.
    mapfile -t args < <(codex_args hardened-no-project-doc "$prompt")
    mapfile -t env < <(codex_credential_env api-key; codex_ca_env)
    bin="$(codex_bin)"
  fi
  since="$(proxy_now)"
  run_cell "$result" "$image" "$ws" "${base[@]}" "${env[@]}" \
    --env "SPIKE_TIMEOUT=$deadline" --env "SPIKE_EXPECT=$expect" \
    -- bash -s -- "$bin" "${args[@]}" < "$BMAD_DIR/container/run-workflow.sh" || return 1
  sleep 1
  {
    printf '\n# --- proxy hosts ---\n'
    proxy_hosts_since "$since"
    printf '# --- end proxy hosts ---\n'
  } >> "$result"
}

# workflow_cell <out-dir> <name> <cli> <image> <branch> <alterations> <expect> <deadline> <prompt> <context-pair>...
# A cell of the matrix: on the small model, and again on the strong one when it
# did not complete. A CLI without its credential is recorded not-run.
workflow_cell() {
  local out="$1" name="$2" cli="$3" image="$4" branch="$5" alter="$6" expect="$7" deadline="$8" prompt="$9"
  shift 9
  # Volumes carry the stream's name, so that two scripts running side by side
  # never share one.
  local tier result small="$out/$name-$cli.txt" ws ctx credential stream="${SPIKE_STREAM:+-$SPIKE_STREAM}"
  credential="$("${cli}_credential_var" api-key)"
  if ! have_credential "$credential"; then
    not_run "$small" "no credential ($credential)"
    return 0
  fi
  for tier in small strong; do
    result="$small"
    if [ "$tier" = strong ]; then
      result="$out/$name-$cli-strong.txt"
      [ "${BMAD_STRONG:-1}" = 1 ] || continue
      [ -f "$small" ] && [ "$(cell_verdict "$small")" != completed ] || continue
    fi
    cell_selected "$result" || continue
    use_models "$tier"
    # shellcheck disable=SC2086  # alterations are a space-separated list
    ws="$(sample_workspace "$name-$cli-$tier$stream" "$branch" $alter)" || { log "cannot prepare the workspace for $(basename "$result")"; exit 1; }
    ctx="$(new_context "$name-$cli-$tier$stream" "$@")" || { log "cannot prepare the context for $(basename "$result")"; exit 1; }
    agent_cell "$result" "$cli" "$image" "$ws" "$ctx" "$expect" "$deadline" "$prompt" || exit 1
    log "    $(cell_verdict "$result") ($(cell_cost "$result"))"
    drop_volumes "$ws" "$ctx"
  done
}

# --- Judging a cell --------------------------------------------------------------
# The rules that read a verdict from a result file are in verdicts.sh. They are
# applied to result files after the fact and are not part of the harness digest
# above: a result is what a run printed, whatever is later concluded from it.

# shellcheck source=verdicts.sh
. "$BMAD_DIR/verdicts.sh"
