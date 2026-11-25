#!/usr/bin/env bash
# Download the pinned Agent CLIs from the vendors' official channels, verify
# each against the checksum in agents.lock, and fill the agent binaries volume.
#
# This mirrors what the orchestrator does at startup (AD-8): the download runs
# in a container, nothing is installed on the host, and a file is moved into the
# volume only after its checksum matched.
#
# Usage: bash 01-fetch-agents.sh
# Overrides (for testing): SPIKE_LOCK_FILE, SPIKE_AGENTS_VOLUME

# shellcheck source=lib.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

arch="$(host_arch)"
result="$RESULTS_DIR/01-fetch/agents-volume.txt"
mkdir -p "$(dirname "$result")"

docker volume create --label "$SPIKE_LABEL" "$AGENTS_VOLUME" > /dev/null

# Only the builds for this host's architecture are fetched.
entries="$(grep -v '^[[:space:]]*#' "$LOCK_FILE" | awk -v a="linux-$arch" 'NF >= 6 && ($3 == a || $3 == a "-musl")')"
[ -n "$entries" ] || { log "no lock entry for linux-$arch"; exit 1; }

log "fetching into volume $AGENTS_VOLUME:"
printf '%s\n' "$entries" | awk '{ print "  " ($7 ? $7 : $1), $2, $3 }' >&2

code=0
printf '%s\n' "$entries" | docker run --rm --interactive --label "$SPIKE_LABEL" \
  --mount "type=volume,source=$AGENTS_VOLUME,target=/hephaestus/agents" \
  "$HELPER_IMAGE" sh -euc '
    tmp=/tmp/fetch
    while read -r agent version platform kind sha url file; do
      file="${file:-$agent}"
      rm -rf "$tmp"; mkdir -p "$tmp"
      dest="/hephaestus/agents/$agent/$platform"
      # Already there: only when the pin is the same and the file itself still
      # has the digest recorded when it was fetched.
      if [ -f "$dest/.$file.sha256" ] && [ "$(cat "$dest/.$file.sha256")" = "$sha" ] && [ -x "$dest/$file" ]; then
        recorded="$(cat "$dest/.$file.binary-sha256" 2> /dev/null || true)"
        if [ -n "$recorded" ] && [ "$(sha256sum "$dest/$file" | cut -d" " -f1)" = "$recorded" ]; then
          echo "present  $file $version $platform"
          continue
        fi
      fi
      wget -q -T 60 -O "$tmp/download" "$url"
      actual="$(sha256sum "$tmp/download" | cut -d" " -f1)"
      if [ "$actual" != "$sha" ]; then
        echo "checksum mismatch: $url" >&2
        echo "  expected $sha" >&2
        echo "  actual   $actual" >&2
        exit 1
      fi
      case "$kind" in
        binary) mv "$tmp/download" "$tmp/$file" ;;
        tar.gz)
          mkdir "$tmp/x"; tar -xzf "$tmp/download" -C "$tmp/x"
          [ "$(find "$tmp/x" -type f | wc -l)" = 1 ] || { echo "unexpected archive content: $url" >&2; exit 1; }
          mv "$(find "$tmp/x" -type f)" "$tmp/$file" ;;
        *) echo "unknown kind: $kind" >&2; exit 1 ;;
      esac
      mkdir -p "$dest"
      chmod 0555 "$tmp/$file"
      mv "$tmp/$file" "$dest/$file"
      printf "%s" "$sha" > "$dest/.$file.sha256"
      printf "%s" "$(sha256sum "$dest/$file" | cut -d" " -f1)" > "$dest/.$file.binary-sha256"
      printf "%s" "$version" > "$dest/.$file.version"
      echo "fetched  $file $version $platform"
    done
    # A CA bundle beside the binaries, for Project images that ship none.
    mkdir -p /hephaestus/agents/certs
    cp /etc/ssl/certs/ca-certificates.crt /hephaestus/agents/certs/ca-certificates.crt
    chown -R 0:0 /hephaestus/agents
    chmod -R a+rX,go-w /hephaestus/agents
  ' || code=$?

if [ "$code" -ne 0 ]; then
  log "fetch failed with exit $code"
  exit "$code"
fi

# Record what the volume holds.
docker run --rm --label "$SPIKE_LABEL" \
  --mount "type=volume,source=$AGENTS_VOLUME,target=/hephaestus/agents,readonly" \
  "$HELPER_IMAGE" sh -c '
    cd /hephaestus/agents
    for f in */*/claude */*/codex */*/codex-code-mode-host; do
      [ -f "$f" ] || continue
      printf "%s  version=%s  lock-sha256=%s  size=%s  binary-sha256=%s\n" \
        "$f" "$(cat "$(dirname "$f")/.$(basename "$f").version")" "$(cat "$(dirname "$f")/.$(basename "$f").sha256")" \
        "$(stat -c %s "$f")" "$(sha256sum "$f" | cut -d" " -f1)"
    done
    echo "--- CA bundle (from the helper image) ---"
    printf "certs/ca-certificates.crt  certificates=%s  sha256=%s\n" \
      "$(grep -c "BEGIN CERTIFICATE" certs/ca-certificates.crt)" \
      "$(sha256sum certs/ca-certificates.crt | cut -d" " -f1)"
    echo "--- permissions ---"
    ls -ln /hephaestus/agents/*/*
  ' | tr -d '\r' > "$result"

cat "$result"
