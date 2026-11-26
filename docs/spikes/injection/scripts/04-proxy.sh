#!/usr/bin/env bash
# Egress proxy (AD-7, input to AD-10).
#
# Step containers are attached only to a Docker internal network. Its single
# exit is an unprivileged HTTP proxy that is also attached to a network with
# outbound access. Each CLI makes a request with the proxy variables set, and
# again without them: the first must reach the vendor, the second must not.
# The hosts each CLI asked the proxy for are taken from the proxy's log.
#
# Without a credential the request carries a dummy key: a 401 from the vendor
# proves the request went through the proxy. With a credential the request is a
# real model run, per authentication mode.
#
# Usage: bash 04-proxy.sh

# shellcheck source=lib.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

out="$RESULTS_DIR/04-proxy"
agent="$SCRIPT_DIR/container/run-agent.sh"
flavour="debian"

require_agents_volume
load_credentials
build_images "$flavour" alpine
start_proxy
image="$(image_name "$flavour")"
ws="$(new_workspace proxy)"
mapfile -t net < <(network_args)
mapfile -t proxy_env < <(proxy_env_args)
mkdir -p "$out"

# --- The network and the proxy container, as Docker reports them ---------------

{
  echo "# workspace network"
  docker network inspect "$WORKSPACE_NET" --format 'name={{.Name}} internal={{.Internal}} driver={{.Driver}}'
  echo "# egress network"
  docker network inspect "$EGRESS_NET" --format 'name={{.Name}} internal={{.Internal}} driver={{.Driver}}'
  echo "# proxy container"
  docker inspect "$PROXY_NAME" --format 'user={{.Config.User}} cap-drop={{.HostConfig.CapDrop}} security-opt={{.HostConfig.SecurityOpt}} privileged={{.HostConfig.Privileged}} mounts={{len .Mounts}} networks={{range $k, $v := .NetworkSettings.Networks}}{{$k}} {{end}}'
  docker exec "$PROXY_NAME" sh -c 'echo "proxy runs as: $(id)"; echo "docker socket: $(ls /var/run/docker.sock 2>/dev/null || echo absent)"'
} 2>&1 | tr -d '\r' > "$out/topology.txt"
cat "$out/topology.txt" >&2

# --- Isolation, seen from a step container without any Agent -------------------
# The Alpine-based image is used because its busybox has wget and nslookup.

run_cell "$out/isolation-no-proxy-variables.txt" "$(image_name alpine)" "$ws" "${net[@]}" -- sh -c '
  echo "--- name resolution of a public host ---"
  nslookup -timeout=5 api.anthropic.com 2>&1 | tail -n 4
  echo "--- direct HTTPS to a public host ---"
  wget -T 8 -q -O /dev/null https://api.anthropic.com/ 2>&1; echo "wget exit: $?"
  echo "--- direct HTTP to a public address ---"
  wget -T 8 -q -O /dev/null http://1.1.1.1/ 2>&1; echo "wget exit: $?"
  echo "--- the network gateway (the Docker host side of the bridge) ---"
  gw="$(ip route 2>/dev/null | grep "^default" | cut -d" " -f3)"
  echo "default route: ${gw:-none}"
  first="$(ip -4 addr show eth0 | grep -o "inet [0-9.]*" | cut -d" " -f2 | cut -d. -f1-3).1"
  echo "first address of the subnet: $first"
  wget -T 5 -q -O /dev/null "http://$first/" 2>&1; echo "wget exit: $?"
  echo "--- a link-local address (cloud metadata) ---"
  wget -T 5 -q -O /dev/null http://169.254.169.254/ 2>&1; echo "wget exit: $?"
  echo "--- the proxy is reachable by name ---"
  nslookup -timeout=5 '"$PROXY_NAME"' 2>&1 | tail -n 3
' < /dev/null

run_cell "$out/isolation-with-proxy-variables.txt" "$(image_name alpine)" "$ws" "${net[@]}" "${proxy_env[@]}" -- sh -c '
  echo "--- plain HTTP through the proxy ---"
  wget -T 15 -q -O /dev/null http://example.com/ 2>&1; echo "wget exit: $?"
' < /dev/null

# --- Each CLI, with and without the proxy variables ----------------------------

# proxied_cell <name> <with|without> <docker-env...> -- <binary> <args...>
# Runs one cell on the workspace network and writes the hosts the proxy saw.
proxied_cell() {
  local name="$1" proxy="$2"
  cell_selected "$out/$name-$proxy-proxy.txt" || return 0
  shift 2
  local extra=() since
  while [ "$1" != "--" ]; do extra+=("$1"); shift; done
  shift
  since="$(proxy_now)"
  if [ "$proxy" = with ]; then
    run_cell "$out/$name-with-proxy.txt" "$image" "$ws" "${net[@]}" "${proxy_env[@]}" "${extra[@]}" \
      -- bash -s -- "$@" < "$agent"
  else
    # Without the variables the CLI retries until its deadline: 60 seconds is enough to show it.
    run_cell "$out/$name-without-proxy.txt" "$image" "$ws" "${net[@]}" "${extra[@]}" --env SPIKE_TIMEOUT=60 \
      -- bash -s -- "$@" < "$agent"
  fi
  sleep 1
  proxy_hosts_since "$since" > "$out/$name-$proxy-proxy.hosts.txt"
  log "    hosts: $(paste -sd ' ' "$out/$name-$proxy-proxy.hosts.txt")"
}

log "dummy credentials"
mapfile -t claude_env < <(claude_variant_env hardened)
mapfile -t claude_dummy < <(claude_args hardened api-key "$PONG_PROMPT")
mapfile -t codex_env < <(codex_ca_env)
mapfile -t codex_dummy < <(codex_args hardened "$PONG_PROMPT")
for proxy in with without; do
  proxied_cell claude-dummy-key "$proxy" --env "ANTHROPIC_API_KEY=$DUMMY_ANTHROPIC_API_KEY" "${claude_env[@]}" \
    -- "$(claude_bin "$flavour")" "${claude_dummy[@]}"
  proxied_cell codex-dummy-key "$proxy" --env "CODEX_API_KEY=$DUMMY_OPENAI_API_KEY" "${codex_env[@]}" \
    -- "$(codex_bin)" "${codex_dummy[@]}"
done

for mode in api-key subscription; do
  log "$mode"
  for proxy in with without; do
    if have_credential "$(claude_credential_var "$mode")"; then
      mapfile -t args < <(claude_args hardened "$mode" "$TOOL_PROMPT")
      mapfile -t env < <(claude_credential_env "$mode"; claude_variant_env hardened)
      proxied_cell "claude-$mode" "$proxy" "${env[@]}" -- "$(claude_bin "$flavour")" "${args[@]}"
    else
      not_run "$out/claude-$mode-$proxy-proxy.txt" "no credential ($(claude_credential_var "$mode"))"
    fi
    if have_credential "$(codex_credential_var "$mode")"; then
      prepare_codex_credential "$mode"
      mapfile -t args < <(codex_args hardened "$TOOL_PROMPT")
      mapfile -t env < <(codex_credential_env "$mode"; codex_ca_env)
      proxied_cell "codex-$mode" "$proxy" "${env[@]}" -- "$(codex_bin)" "${args[@]}"
    else
      not_run "$out/codex-$mode-$proxy-proxy.txt" "no credential ($(codex_credential_var "$mode"))"
    fi
  done
  # What a run asks for when nothing is switched off, for comparison (Claude Code only:
  # the hardened variant sets variables that stop optional traffic).
  if have_credential "$(claude_credential_var "$mode")"; then
    mapfile -t args < <(claude_args control "$mode" "$TOOL_PROMPT")
    mapfile -t env < <(claude_credential_env "$mode")
    proxied_cell "claude-$mode-control" with "${env[@]}" -- "$(claude_bin "$flavour")" "${args[@]}"
  else
    not_run "$out/claude-$mode-control-with-proxy.txt" "no credential ($(claude_credential_var "$mode"))"
  fi
done

# Summary.
{
  echo "| Cell | Exit | Hosts asked of the proxy |"
  echo "| --- | --- | --- |"
  for result in "$out"/*-proxy.txt; do
    name="$(basename "$result" .txt)"
    if grep -q '^# not-run:' "$result"; then
      echo "| $name | not-run | $(sed -n 's/^# not-run: //p' "$result") |"
    else
      hosts="$(paste -sd ' ' "$out/$name.hosts.txt" 2> /dev/null || true)"
      echo "| $name | $(cell_exit "$result") | ${hosts:-none} |"
    fi
  done
} > "$out/summary.md"
cat "$out/summary.md"
