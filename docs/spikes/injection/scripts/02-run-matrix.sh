#!/usr/bin/env bash
# Run matrix (AD-7, AD-8): both pinned CLIs in the hardened step container on a
# Debian-based, an Ubuntu-based, and an Alpine-based image.
#
# Without a credential, per image:
#   - what the container looks like from inside (user, capabilities, limits, mounts);
#   - that each CLI starts from the read-only mount;
#   - what happens when the Claude Code build for the other C library is used;
#   - that each CLI reaches its vendor and reports a refused dummy key in its
#     machine-readable stream, and what Codex does without a CA bundle.
# With a credential, per image and authentication mode:
#   - a model run that uses the shell tool and emits the stream.
# With a credential, on the Debian-based image only:
#   - schema-constrained output; the Codex reasoning-effort override; Codex
#     without its code-mode host; a HOME mounted noexec.
#
# The summary records for every model run whether the tool really ran, read
# from the stream. The script exits 1 when a model run on the three images
# completed without running it.
#
# Usage: bash 02-run-matrix.sh
# A cell whose credential is missing from .env is recorded not-run.

# shellcheck source=lib.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

out="$RESULTS_DIR/02-run-matrix"
agent="$SCRIPT_DIR/container/run-agent.sh"
SCHEMA='{"type":"object","properties":{"answer":{"type":"string"},"confidence":{"type":"integer"}},"required":["answer","confidence"],"additionalProperties":false}'
SCHEMA_PROMPT='What is the capital of Poland? Answer in the required JSON shape, with confidence from 0 to 100.'

require_agents_volume
load_credentials
pull_base_images
build_images
ws="$(new_workspace matrix)"

# The exact base images the test images were built from, and the host.
mkdir -p "$out"
for base in "${BASE_IMAGES[@]}"; do
  docker image inspect "$base" --format '{{index .RepoDigests 0}}'
done | tr -d '\r' > "$out/base-images.txt"
{
  docker version --format 'docker engine {{.Server.Version}} {{.Server.Os}}/{{.Server.Arch}}'
  docker info --format '{{.OperatingSystem}}, kernel {{.KernelVersion}}, security options {{.SecurityOptions}}'
} | tr -d '\r' > "$out/host.txt"

other_claude_platform() {
  if [ "$(image_libc "$1")" = musl ]; then echo "linux-$(host_arch)"; else echo "linux-$(host_arch)-musl"; fi
}

# --- Without a credential ----------------------------------------------------

# The flags and feature switches of the pinned versions, as the CLIs print them.
log "debian: help output and feature list"
run_cell "$out/debian-claude-help.txt" "$(image_name debian)" "$ws" -- "$(claude_bin debian)" --help < /dev/null
run_cell "$out/debian-codex-exec-help.txt" "$(image_name debian)" "$ws" -- "$(codex_bin)" exec --help < /dev/null
run_cell "$out/debian-codex-features.txt" "$(image_name debian)" "$ws" -- "$(codex_bin)" features list < /dev/null

for flavour in "${IMAGE_FLAVOURS[@]}"; do
  image="$(image_name "$flavour")"
  log "$flavour: container, start-up, dummy credentials"

  run_cell "$out/$flavour-container.txt" "$image" "$ws" -- bash -c '
    echo "id: $(id)"
    echo "passwd entry for 10001: $(grep -c "^[^:]*:[^:]*:10001:" /etc/passwd)"
    echo "HOME=$HOME"
    grep -E "^(CapInh|CapPrm|CapEff|CapBnd|NoNewPrivs|Seccomp):" /proc/self/status
    echo "--- limits (cgroup) ---"
    for f in pids.max memory.max cpu.max; do echo "$f: $(cat /sys/fs/cgroup/$f 2>&1)"; done
    echo "--- mounts ---"
    grep -E " /(workspace|hephaestus/[a-z]+) " /proc/self/mounts
    echo "--- write to the workspace ---"
    ls -ldn /workspace
    touch /workspace/.write-probe && rm /workspace/.write-probe && echo "workspace is writable"
    echo "--- write to the agent binaries mount ---"
    touch /hephaestus/agents/probe 2>&1 || true
    echo "--- C library ---"
    ls /lib/ld-musl-* 2>/dev/null || echo "no musl loader: glibc"
    echo "--- image ---"
    grep -E "^(PRETTY_NAME)=" /etc/os-release
    echo "bash: $(command -v bash)  git: $(command -v git || echo absent)  CA bundle: $(ls /etc/ssl/certs/ca-certificates.crt 2>/dev/null || echo absent)"
  ' < /dev/null

  run_cell "$out/$flavour-claude-version.txt" "$image" "$ws" -- "$(claude_bin "$flavour")" --version < /dev/null
  run_cell "$out/$flavour-codex-version.txt" "$image" "$ws" -- "$(codex_bin)" --version < /dev/null
  run_cell "$out/$flavour-claude-other-libc-build.txt" "$image" "$ws" \
    -- "/hephaestus/agents/claude/$(other_claude_platform "$flavour")/claude" --version < /dev/null

  mapfile -t args < <(claude_args hardened api-key "$PONG_PROMPT")
  mapfile -t env < <(claude_variant_env hardened)
  run_cell "$out/$flavour-claude-dummy-key.txt" "$image" "$ws" \
    --env "ANTHROPIC_API_KEY=$DUMMY_ANTHROPIC_API_KEY" "${env[@]}" \
    -- bash -s -- "$(claude_bin "$flavour")" "${args[@]}" < "$agent"

  mapfile -t args < <(codex_args hardened "$PONG_PROMPT")
  mapfile -t env < <(codex_ca_env)
  run_cell "$out/$flavour-codex-dummy-key.txt" "$image" "$ws" \
    --env "CODEX_API_KEY=$DUMMY_OPENAI_API_KEY" "${env[@]}" \
    -- bash -s -- "$(codex_bin)" "${args[@]}" < "$agent"
  run_cell "$out/$flavour-codex-dummy-key-no-ca-bundle.txt" "$image" "$ws" \
    --env "CODEX_API_KEY=$DUMMY_OPENAI_API_KEY" --env SPIKE_TIMEOUT=40 \
    -- bash -s -- "$(codex_bin)" "${args[@]}" < "$agent"
done

# The other variable that points Codex at a CA bundle. It is read by most TLS
# libraries, not by Codex alone.
log "debian: codex with SSL_CERT_FILE"
mapfile -t args < <(codex_args hardened "$PONG_PROMPT")
run_cell "$out/debian-codex-dummy-key-ssl-cert-file.txt" "$(image_name debian)" "$ws" \
  --env "CODEX_API_KEY=$DUMMY_OPENAI_API_KEY" --env "SSL_CERT_FILE=$AGENTS_CA_BUNDLE" --env SPIKE_TIMEOUT=60 \
  -- bash -s -- "$(codex_bin)" "${args[@]}" < "$agent"

# What happens to an empty Task volume mounted without volume-nocopy: this one
# container is started by hand, with the mount option left out.
log "debian: workspace volume without volume-nocopy"
result="$out/debian-workspace-without-volume-nocopy.txt"
if cell_selected "$result"; then
  plain="$(new_workspace nocopy-probe)"
  mapfile -t args < <(hardened_args)
  code=0
  docker run --rm "${args[@]}" --mount "type=volume,source=$plain,target=/workspace" \
    "$(image_name debian)" bash -c 'ls -ldn /workspace; touch /workspace/.write-probe && echo "workspace is writable"' \
    > "$SPIKE_TMP/out" 2>&1 || code=$?
  {
    printf '# harness: %s\n# image: %s\n' "$(harness_id)" "$(image_name debian)"
    printf '# container: the hardened definition, with the Task volume mounted without volume-nocopy\n'
    printf '# exit: %s\n# --- stdout and stderr ---\n' "$code"
    cat "$SPIKE_TMP/out"
  } | tr -d '\r' > "$result"
  log "  $(basename "$result"): exit $code"
fi

# --- With a credential ---------------------------------------------------------

claude_cell() { # <result> <flavour> <mode> <prompt> [extra-flag...]
  local result="$1" flavour="$2" mode="$3" prompt="$4"
  shift 4
  local args=() env=()
  if ! have_credential "$(claude_credential_var "$mode")"; then
    not_run "$result" "no credential ($(claude_credential_var "$mode"))"
    return
  fi
  mapfile -t args < <(claude_args hardened "$mode" "$prompt" "$@")
  mapfile -t env < <(claude_credential_env "$mode"; claude_variant_env hardened)
  run_cell "$result" "$(image_name "$flavour")" "$ws" "${env[@]}" \
    -- bash -s -- "$(claude_bin "$flavour")" "${args[@]}" < "$agent"
}

codex_cell() { # <result> <flavour> <mode> <prompt> [extra-flag...]
  local result="$1" flavour="$2" mode="$3" prompt="$4"
  shift 4
  local args=() env=()
  if ! have_credential "$(codex_credential_var "$mode")"; then
    not_run "$result" "no credential ($(codex_credential_var "$mode"))"
    return
  fi
  prepare_codex_credential "$mode"
  mapfile -t args < <(codex_args hardened "$prompt" "$@")
  mapfile -t env < <(codex_credential_env "$mode"; codex_ca_env)
  run_cell "$result" "$(image_name "$flavour")" "$ws" "${env[@]}" --env "SPIKE_SCHEMA_JSON=$SCHEMA" \
    -- bash -s -- "$(codex_bin)" "${args[@]}" < "$agent"
}

for mode in api-key subscription; do
  for flavour in "${IMAGE_FLAVOURS[@]}"; do
    log "$flavour, $mode: model run with the shell tool"
    claude_cell "$out/$flavour-claude-$mode-stream.txt" "$flavour" "$mode" "$TOOL_PROMPT"
    codex_cell "$out/$flavour-codex-$mode-stream.txt" "$flavour" "$mode" "$TOOL_PROMPT"
  done

  log "debian, $mode: schema-constrained output"
  # Claude Code: --json-schema, read from the result's structured_output field.
  claude_cell "$out/debian-claude-$mode-schema.txt" debian "$mode" "$SCHEMA_PROMPT" --json-schema "$SCHEMA"
  # Codex: --output-schema takes a file; the final message is also written with -o.
  codex_cell "$out/debian-codex-$mode-schema.txt" debian "$mode" "$SCHEMA_PROMPT" \
    --output-schema /hephaestus/home/schema.json --output-last-message /hephaestus/home/last-message.txt

  log "debian, $mode: codex reasoning-effort override"
  # "bogus" shows whether an unknown value is refused, and by whom.
  for effort in low high bogus; do
    codex_cell "$out/debian-codex-$mode-effort-$effort.txt" debian "$mode" "$PONG_PROMPT" \
      -c "model_reasoning_effort=\"$effort\""
  done
done

# Codex runs tool calls of some models through codex-code-mode-host, a separate
# release asset. With the helper switched off, such a model cannot run a command.
log "debian: codex without the code-mode host"
for mode in api-key subscription; do
  codex_cell "$out/debian-codex-$mode-without-code-mode-host.txt" debian "$mode" "$TOOL_PROMPT" --disable code_mode_host
done

# The models Codex accepts differ by authentication mode: the model set for the
# API key, asked for under subscription sign-in.
if [ -n "${SPIKE_CODEX_MODEL:-}" ]; then
  log "debian: codex subscription with the API key's model"
  codex_cell "$out/debian-codex-subscription-with-api-key-model.txt" debian subscription "$PONG_PROMPT" \
    --model "$SPIKE_CODEX_MODEL"
fi

# A throwaway HOME is usually a tmpfs, which Docker mounts noexec by default.
log "debian, api-key: HOME mounted noexec"
HOME_TMPFS_OPTS="rw,noexec,nosuid,nodev,size=512m,uid=10001,gid=10001,mode=0700"
export HOME_TMPFS_OPTS
claude_cell "$out/debian-claude-api-key-home-noexec.txt" debian api-key "$TOOL_PROMPT"
codex_cell "$out/debian-codex-api-key-home-noexec.txt" debian api-key "$TOOL_PROMPT"
unset HOME_TMPFS_OPTS

# --- Judgement and summary ---------------------------------------------------

failures=0
{
  echo "| Cell | Exit | Tool ran |"
  echo "| --- | --- | --- |"
  for result in "$out"/*.txt; do
    is_cell "$result" || continue
    name="$(basename "$result" .txt)"
    if grep -q '^# not-run:' "$result"; then
      echo "| $name | not-run: $(sed -n 's/^# not-run: //p' "$result") | |"
      continue
    fi
    tool=""
    case "$name" in
      *-stream | *-home-noexec | *-without-code-mode-host)
        # The prompt asks for `echo tool-$((6*7))`; only an executed command prints tool-42.
        if cell_body "$result" | grep -q 'tool-42'; then tool=yes; else tool=NO; fi
        ;;
    esac
    echo "| $name | $(cell_exit "$result") | $tool |"
    case "$name" in
      *-stream | *-home-noexec)
        if [ "$(cell_exit "$result")" != 0 ] || [ "$tool" != yes ]; then
          log "FAIL: $name: exit $(cell_exit "$result"), tool ran: $tool"
          failures=$((failures + 1))
        fi
        ;;
    esac
  done
} > "$out/summary.md"
cat "$out/summary.md"

if [ "$failures" -ne 0 ]; then
  log "$failures model run(s) did not complete with the tool executed"
  exit 1
fi
