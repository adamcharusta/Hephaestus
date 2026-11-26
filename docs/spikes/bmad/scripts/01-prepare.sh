#!/usr/bin/env bash
# Prepare what the cells of the BMAD spike run on. Nothing here needs a credential.
#
#   1. The agent binaries volume, with the injection spike's own fetch script
#      (the pinned CLIs, verified against agents.lock). Its record goes to this
#      spike's results, so docs/spikes/injection/ stays as it is. The host is
#      recorded beside it.
#   2. The test image: a stock base plus uv and Python; and the same with git.
#   3. The Methodology volume: this repository's installed BMAD, unchanged.
#   4. The sample repository, in a volume: a small tested project with BMAD
#      installed for both Agents, a Story to build, and a branch on which the
#      Story is implemented with two defects.
#
# Usage: bash 01-prepare.sh

# shellcheck source=lib-bmad.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib-bmad.sh"

out="$RESULTS_DIR/01-prepare"
mkdir -p "$out"

# The fetch script skips what the volume already holds, and writes what the
# volume holds to results/01-fetch/agents-volume.txt.
log "the pinned Agent CLIs"
bash "$SCRIPT_DIR/01-fetch-agents.sh" > /dev/null
require_agents_volume

{
  docker version --format 'docker engine {{.Server.Version}} {{.Server.Os}}/{{.Server.Arch}}'
  docker info --format '{{.OperatingSystem}}, kernel {{.KernelVersion}}, security options {{.SecurityOptions}}'
} | tr -d '\r' > "$out/host.txt"

docker pull --quiet debian:13-slim > /dev/null
docker pull --quiet astral/uv:0.13.9 > /dev/null
build_tools_image
build_bmad_images
{
  echo "# base images"
  docker image inspect debian:13-slim --format '{{index .RepoDigests 0}}'
  docker image inspect astral/uv:0.13.9 --format '{{index .RepoDigests 0}}'
  for image in "$BMAD_IMAGE" "$BMAD_GIT_IMAGE"; do
    echo "# $image"
    docker run --rm --label "$SPIKE_LABEL" "$image" sh -c '
      grep "^PRETTY_NAME=" /etc/os-release
      python3 --version
      uv --version
      echo "git: $(git --version 2> /dev/null || echo absent)"
      echo "CA bundle: $(ls /etc/ssl/certs/ca-certificates.crt 2> /dev/null || echo absent)"'
  done
} | tr -d '\r' > "$out/images.txt"
cat "$out/images.txt" >&2

log "filling the Methodology volume $METHODOLOGY_VOLUME"
fill_methodology
{
  echo "# source: the tracked files of this repository under _bmad/ and .claude/skills/"
  echo "# commit: $(cd "$REPO_ROOT" && git rev-parse HEAD)"
  echo "# BMAD: $(sed -n 's/^  version: //p' "$REPO_ROOT/_bmad/_config/manifest.yaml" | head -n 1), modules $(sed -n 's/^  - name: //p' "$REPO_ROOT/_bmad/_config/manifest.yaml" | paste -sd ' ' -)"
  if diff -r "$REPO_ROOT/.claude/skills" "$REPO_ROOT/.agents/skills" > /dev/null; then
    echo "# .claude/skills and .agents/skills: identical"
  else
    echo "# .claude/skills and .agents/skills: DIFFERENT"
  fi
  docker run --rm --label "$SPIKE_LABEL" \
    --mount "type=volume,source=$METHODOLOGY_VOLUME,target=/hephaestus/methodology,readonly" "$HELPER_IMAGE" sh -c '
      cd /hephaestus/methodology
      echo "# files: $(find . -type f | wc -l) ($(find skills -mindepth 1 -maxdepth 1 -type d | wc -l) skills)"
      echo "# owner and mode of the root: $(stat -c "%u:%g %a" .)"
      find _bmad -type f | sort
      find skills -mindepth 1 -maxdepth 1 -type d | sort'
} | tr -d '\r' > "$out/methodology.txt"
head -n 6 "$out/methodology.txt" >&2

log "generating the sample repository in $SAMPLE_VOLUME"
make_sample | tr -d '\r' > "$out/sample-branches.txt"
cat "$out/sample-branches.txt" >&2

# The sample as a step container sees it: its files, its tests, and the
# acceptance probe of the Story, which must fail before the Story is built.
for branch in task/low-stock task/low-stock-implemented; do
  ws="$(sample_workspace "prepare" "$branch")"
  mapfile -t mount < <(methodology_args)
  run_cell "$out/sample-${branch#task/}.txt" "$BMAD_IMAGE" "$ws" "${mount[@]}" --env SPIKE_EXPECT=implementation \
    -- bash -s -- bash -c '
      echo "--- files outside BMAD ---"
      find . -path ./.git -prune -o -path ./_bmad -prune -o -path ./.claude -prune -o -path ./.agents -prune -o -type f -print | sort
      echo "--- BMAD in the repository ---"
      for d in _bmad .claude/skills .agents/skills; do echo "$d: $(find "$d" -type f | wc -l) files"; done
      echo "--- tests ---"
      python3 -m unittest discover -s tests -t . 2>&1 | tail -n 3
    ' < "$BMAD_DIR/container/run-workflow.sh"
  drop_volumes "$ws"
done

# --- BMAD's own scripts in the step container, with no Agent and no network ----
# Every step-file workflow starts by running render_skill.py with `uv run`; the
# others start with resolve_customization.py and resolve_config.py. These cells
# show what those scripts need and where they write, before any model is asked.

log "BMAD's scripts in the step container"
mapfile -t mount < <(methodology_args)
M=/hephaestus/methodology
probe() { # <result-name> <image> <script>
  local ws
  ws="$(sample_workspace "prepare" task/low-stock)"
  run_cell "$out/$1.txt" "$2" "$ws" "${mount[@]}" --network none -- bash -c "$3" < /dev/null
  drop_volumes "$ws"
}

# The renderer, with the project in /workspace and BMAD's files in the mount.
probe scripts-render-project-root-workspace "$BMAD_GIT_IMAGE" "
  uv run --no-cache $M/_bmad/scripts/render_skill.py --project-root /workspace --skill $M/skills/bmad-build-auto; echo \"exit: \$?\"
  echo '--- what it wrote ---'
  find /workspace/_bmad/render -type f | sed -E 's#/[0-9a-f]{20}/#/<generation>/#' | sort
  echo '--- where the rendered workflow sends the Agent next ---'
  grep -h -o 'Read fully and follow: .*' /workspace/_bmad/render/bmad-build-auto/*/*/workflow.md
  echo '--- where a rendered review layer finds its instructions ---'
  grep -h -o 'Read \`[^\`]*edge-case-hunter.md\`' /workspace/_bmad/render/bmad-build-auto/*/*/step-04-review.md
  echo '--- the working tree afterwards (git status --porcelain) ---'
  git status --porcelain; echo \"lines: \$(git status --porcelain | wc -l)\"
"

# The renderer told that BMAD's project root is the read-only mount.
probe scripts-render-project-root-methodology "$BMAD_IMAGE" "
  uv run --no-cache $M/_bmad/scripts/render_skill.py --project-root $M --skill $M/skills/bmad-build-auto; echo \"exit: \$?\"
  echo '--- a direct write to the mount ---'
  touch $M/_bmad/render/probe; echo \"exit: \$?\"
  mkdir -p $M/_bmad/render/bmad-build-auto; echo \"exit: \$?\"
"

# The renderer with a project that has no _bmad/ of its own.
probe scripts-render-project-without-bmad "$BMAD_IMAGE" "
  mkdir /tmp/project
  uv run --no-cache $M/_bmad/scripts/render_skill.py --project-root /tmp/project --skill $M/skills/bmad-build-auto; echo \"exit: \$?\"
"

# The two resolvers every workflow starts with. They only read.
probe scripts-resolvers "$BMAD_IMAGE" "
  for root in /workspace $M /tmp/project; do
    mkdir -p /tmp/project
    echo \"--- resolve_config.py --project-root \$root ---\"
    uv run $M/_bmad/scripts/resolve_config.py --project-root \$root --key core.communication_language --key core.document_output_language --key modules.bmm.implementation_artifacts 2>&1; echo \"exit: \$?\"
    echo \"--- resolve_customization.py --project-root \$root ---\"
    uv run $M/_bmad/scripts/resolve_customization.py --skill $M/skills/bmad-sprint-planning --project-root \$root --key workflow 2>&1 | head -c 400; code=\${PIPESTATUS[0]}; echo; echo \"exit: \$code\"
  done
  echo '--- what uv left behind ---'
  echo \"in the workspace: \$(cd /workspace && find . -path ./.git -prune -o -newer /workspace/README.md -type f -print | wc -l) new file(s)\"
  echo \"in HOME: \$(du -sh \$HOME 2> /dev/null | cut -f1)\"
"

# Without uv, and without Python: the stock image of the injection spike.
build_images debian
probe scripts-without-uv "$(image_name debian)" "
  echo \"uv: \$(command -v uv || echo absent)  python3: \$(command -v python3 || echo absent)\"
  uv run --no-cache $M/_bmad/scripts/render_skill.py --project-root /workspace --skill $M/skills/bmad-build-auto; echo \"exit: \$?\"
"

# With Python and without uv: the scripts use the standard library only.
probe scripts-python-without-uv "$BMAD_IMAGE" "
  python3 $M/_bmad/scripts/render_skill.py --project-root /workspace --skill $M/skills/bmad-build-auto; echo \"exit: \$?\"
  python3 $M/skills/bmad-sprint-planning/scripts/sprint_plan.py generate --epic-file /workspace/_bmad-output/planning-artifacts/epics.md --status-file /tmp/sprint-status.yaml --stories-dir /workspace/_bmad-output/implementation-artifacts --project Shelf --date '11-26-2026 09:00' --dry-run | head -c 600; code=\${PIPESTATUS[0]}; echo; echo \"exit: \$code\"
"

# Two helper scripts declare a dependency (ruamel.yaml), which `uv run` fetches
# from the package index: once without a network, once behind the proxy.
plan="uv run $M/skills/bmad-sprint-planning/scripts/sprint_plan.py generate --epic-file /workspace/_bmad-output/planning-artifacts/epics.md --status-file /tmp/sprint-status.yaml --stories-dir /workspace/_bmad-output/implementation-artifacts --project Shelf --date '11-26-2026 09:00' --dry-run"
probe scripts-dependency-without-network "$BMAD_IMAGE" "
  $plan 2>&1 | head -c 800; code=\${PIPESTATUS[0]}; echo; echo \"exit: \$code\"
"
start_proxy
mapfile -t net < <(network_args; proxy_env_args)
ws="$(sample_workspace "prepare" task/low-stock)"
since="$(proxy_now)"
run_cell "$out/scripts-dependency-behind-proxy.txt" "$BMAD_IMAGE" "$ws" "${mount[@]}" "${net[@]}" -- bash -c "
  $plan 2>&1 | head -c 800; code=\${PIPESTATUS[0]}; echo; echo \"exit: \$code\"
  echo \"uv cache in HOME: \$(du -sh \$HOME/.cache/uv 2> /dev/null | cut -f1)\"
" < /dev/null
sleep 1
{
  printf '\n# --- proxy hosts ---\n'
  proxy_hosts_since "$since"
  printf '# --- end proxy hosts ---\n'
} >> "$out/scripts-dependency-behind-proxy.txt"
drop_volumes "$ws"

log "prepared: results in $out"
