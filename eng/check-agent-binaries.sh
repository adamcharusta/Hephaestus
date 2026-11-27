#!/usr/bin/env bash
# Starts each pinned Agent executable of this machine's architecture in a hardened container, from the agent binaries volume mounted
# read-only, and checks that it prints its version (Story 3.1, AD-8). Story 1.1 ran linux/amd64 only; CI runs this on both architectures,
# each on a runner of its own architecture. The volume is the one the host filled at its start: `docker compose -f deploy/compose.yaml up`.
#
# Usage: bash eng/check-agent-binaries.sh [volume]   (default: hephaestus-agents)
#
# The container is a step container as the runtime will make it: an unprivileged user with no passwd entry, every capability dropped,
# no new privileges, no network, process and memory limits, a throwaway HOME, and the executables read-only. It runs on a glibc image
# (debian:13-slim) and on a musl image (alpine:3.23); Claude Code starts only from the build that matches the image's C library.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

volume="${1:-hephaestus-agents}"
case "$(uname -m)" in
  x86_64) arch=x64 ;;
  aarch64 | arm64) arch=arm64 ;;
  *) echo "unsupported architecture $(uname -m)" >&2; exit 1 ;;
esac

version_of() {
  local agent="$1"
  awk -v agent="$agent" '$1 == agent { print $2; exit }' deploy/agents.lock
}
claude_version="$(version_of claude-code)"
codex_version="$(version_of codex-cli)"

# Pulled first, so that the output of a check is the executable's own and not Docker's progress.
docker pull --quiet debian:13-slim > /dev/null
docker pull --quiet alpine:3.23 > /dev/null

failed=0

# check <image> <expected text> <executable> <argument>: the executable runs and prints the expected text.
check() {
  local image="$1" expected="$2" executable="$3" argument="$4" output
  if output="$(docker run --rm --user 10001:10001 --cap-drop ALL --security-opt no-new-privileges \
      --pids-limit 256 --memory 2g --network none \
      --mount "type=volume,source=$volume,target=/hephaestus/agents,readonly" \
      --tmpfs "/hephaestus/home:rw,exec,nosuid,nodev,size=256m,uid=10001,gid=10001,mode=0700" \
      --env HOME=/hephaestus/home --env DISABLE_AUTOUPDATER=1 --env DISABLE_UPDATES=1 \
      "$image" "/hephaestus/agents/$executable" "$argument" 2>&1)" && grep -qF -- "$expected" <<< "$output"; then
    printf 'ok      %-14s %s -> %s\n' "$image" "$executable" "$(head -n 1 <<< "$output")"
  else
    printf 'FAILED  %-14s %s %s\n%s\n' "$image" "$executable" "$argument" "$output" >&2
    failed=1
  fi
}

check debian:13-slim "$claude_version" "claude-code/$arch-glibc/claude" --version
check alpine:3.23 "$claude_version" "claude-code/$arch-musl/claude" --version
for image in debian:13-slim alpine:3.23; do
  check "$image" "$codex_version" "codex-cli/$arch-static/codex" --version
  check "$image" "Usage" "codex-cli/$arch-static/codex-code-mode-host" --help
done

exit "$failed"
