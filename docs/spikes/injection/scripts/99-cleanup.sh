#!/usr/bin/env bash
# Remove everything the injection spike created in Docker: containers, volumes,
# networks, and the images it built. Everything carries the label
# hephaestus.spike=injection.
#
# Usage: bash 99-cleanup.sh [--keep-agents] [--toolchain-images]
#   --keep-agents       keep the agent binaries volume (about 1.1 GB to fetch again)
#   --toolchain-images  also remove the stock toolchain images pulled for
#                       05-package-managers.sh (several GB)

# shellcheck source=lib.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

keep_agents=0
toolchain_images=0
for arg in "$@"; do
  case "$arg" in
    --keep-agents) keep_agents=1 ;;
    --toolchain-images) toolchain_images=1 ;;
    *) log "unknown option: $arg"; exit 2 ;;
  esac
done

remove() { # <kind> <id...>
  local kind="$1"
  shift
  [ $# -gt 0 ] || return 0
  case "$kind" in
    container) docker rm --force "$@" > /dev/null ;;
    volume) docker volume rm --force "$@" > /dev/null ;;
    network) docker network rm "$@" > /dev/null ;;
    image) docker image rm --force "$@" > /dev/null ;;
  esac
}

mapfile -t containers < <(docker ps --all --quiet --filter "label=$SPIKE_LABEL")
remove container "${containers[@]}"

# By label, and by name: a volume Docker created by itself carries no label.
mapfile -t volumes < <({ docker volume ls --quiet --filter "label=$SPIKE_LABEL"; docker volume ls --quiet --filter "name=^$PREFIX-"; } | sort -u)
if [ "$keep_agents" = 1 ]; then
  kept=()
  for volume in "${volumes[@]}"; do [ "$volume" = "$AGENTS_VOLUME" ] || kept+=("$volume"); done
  volumes=("${kept[@]}")
fi
remove volume "${volumes[@]}"

mapfile -t networks < <(docker network ls --quiet --filter "label=$SPIKE_LABEL")
remove network "${networks[@]}"

mapfile -t images < <(docker image ls --quiet --filter "label=$SPIKE_LABEL" | sort -u)
remove image "${images[@]}"

if [ "$toolchain_images" = 1 ]; then
  for image in node:24-slim mcr.microsoft.com/dotnet/sdk:10.0 python:3.14-slim rust:1-slim \
    golang:1.25 maven:3-eclipse-temurin-21 gradle:9-jdk21; do
    docker image rm "$image" > /dev/null 2>&1 || true
  done
fi

log "removed: ${#containers[@]} container(s), ${#volumes[@]} volume(s), ${#networks[@]} network(s), ${#images[@]} image(s)"
if [ -f "$ENV_FILE" ]; then
  log "The credentials are still in $ENV_FILE. Delete the file, and revoke the keys and the"
  log "setup-token if they were created for this spike."
fi
