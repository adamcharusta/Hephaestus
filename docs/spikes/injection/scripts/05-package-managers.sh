#!/usr/bin/env bash
# Package managers behind the egress proxy (AD-7).
#
# npm, dotnet restore, pip, cargo, go, Maven, and Gradle each restore one small
# dependency in a hardened step container on the internal workspace network.
# First with the generic proxy variables alone, exactly as the runtime will set
# them. A manager that fails is run again with the setting it needs, which is
# then something the Project image has to carry.
#
# The images are stock toolchain images, standing in for Project images. They
# are pulled by the host's Docker, not through the proxy.
#
# Usage: bash 05-package-managers.sh [manager...]
# No credential is needed.

# shellcheck source=lib.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

out="$RESULTS_DIR/05-package-managers"
managers=("$@")
[ ${#managers[@]} -gt 0 ] || managers=(npm dotnet pip cargo go maven gradle)

start_proxy
mapfile -t net < <(network_args)
mapfile -t proxy_env < <(proxy_env_args)

# Restores download more than an Agent's HOME needs.
HOME_TMPFS_OPTS="rw,exec,nosuid,nodev,size=2g,uid=10001,gid=10001,mode=0700"
export HOME_TMPFS_OPTS

# The JVM does not read the generic variables. One variable, read by every JVM,
# carries the proxy as system properties.
proxy_host="${PROXY_URL#http://}"
jvm_proxy="-Dhttp.proxyHost=${proxy_host%:*} -Dhttp.proxyPort=${proxy_host#*:} -Dhttps.proxyHost=${proxy_host%:*} -Dhttps.proxyPort=${proxy_host#*:} -Dhttp.nonProxyHosts=localhost|127.*|[::1]"

image_of() {
  case "$1" in
    npm) echo node:24-slim ;;
    dotnet) echo mcr.microsoft.com/dotnet/sdk:10.0 ;;
    pip) echo python:3.14-slim ;;
    cargo) echo rust:1-slim ;;
    go) echo golang:1.25 ;;
    maven) echo maven:3-eclipse-temurin-21 ;;
    gradle) echo gradle:9-jdk21 ;;
  esac
}

# restore_script <manager>: what runs in the container, in /workspace.
restore_script() {
  case "$1" in
    npm) cat <<'EOF'
npm --version
printf '{"name":"spike","version":"1.0.0","private":true}\n' > package.json
npm install is-number@7.0.0 --no-audit --no-fund
test -f node_modules/is-number/package.json && echo RESTORED
EOF
      ;;
    dotnet) cat <<'EOF'
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet --version
cat > spike.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup>
</Project>
XML
dotnet restore --no-cache && ls "$HOME/.nuget/packages/newtonsoft.json" && echo RESTORED
EOF
      ;;
    pip) cat <<'EOF'
pip --version
pip install --no-cache-dir --disable-pip-version-check --target /workspace/site idna==3.10
test -d /workspace/site/idna && echo RESTORED
EOF
      ;;
    cargo) cat <<'EOF'
cargo --version
mkdir -p src && echo 'fn main() {}' > src/main.rs
printf '[package]\nname = "spike"\nversion = "0.1.0"\nedition = "2021"\n\n[dependencies]\nitoa = "1.0.11"\n' > Cargo.toml
cargo fetch && ls "${CARGO_HOME:-$HOME/.cargo}"/registry/cache/*/ | grep itoa && echo RESTORED
EOF
      ;;
    go) cat <<'EOF'
go version
go mod init example.invalid/spike
go get github.com/google/uuid@v1.6.0 && grep uuid go.sum > /dev/null && echo RESTORED
EOF
      ;;
    maven) cat <<'EOF'
mvn --version | head -n 1
if [ -n "${SPIKE_MAVEN_SETTINGS:-}" ]; then
  # Maven reads its proxy from settings.xml only. Here the file is derived from
  # the generic variable; a Project image would carry it in $MAVEN_HOME/conf.
  hostport="${HTTPS_PROXY#http://}"
  mkdir -p "$HOME/.m2"
  cat > "$HOME/.m2/settings.xml" <<SETTINGS
<settings>
  <proxies>
    <proxy><id>egress-https</id><active>true</active><protocol>https</protocol><host>${hostport%:*}</host><port>${hostport#*:}</port><nonProxyHosts>localhost|127.*|[::1]</nonProxyHosts></proxy>
    <proxy><id>egress-http</id><active>true</active><protocol>http</protocol><host>${hostport%:*}</host><port>${hostport#*:}</port><nonProxyHosts>localhost|127.*|[::1]</nonProxyHosts></proxy>
  </proxies>
</settings>
SETTINGS
fi
cat > pom.xml <<'XML'
<project xmlns="http://maven.apache.org/POM/4.0.0">
  <modelVersion>4.0.0</modelVersion>
  <groupId>invalid.example</groupId><artifactId>spike</artifactId><version>1.0.0</version>
  <dependencies>
    <dependency><groupId>org.apache.commons</groupId><artifactId>commons-lang3</artifactId><version>3.17.0</version></dependency>
  </dependencies>
</project>
XML
mvn --batch-mode --no-transfer-progress dependency:resolve \
  && ls "$HOME/.m2/repository/org/apache/commons/commons-lang3/3.17.0/"*.jar && echo RESTORED
EOF
      ;;
    gradle) cat <<'EOF'
gradle --version | grep -E '^Gradle'
echo 'rootProject.name = "spike"' > settings.gradle
cat > build.gradle <<'GROOVY'
plugins { id 'java' }
repositories { mavenCentral() }
dependencies { implementation 'org.apache.commons:commons-lang3:3.17.0' }
tasks.register('resolve') { doLast { println configurations.runtimeClasspath.files } }
GROOVY
gradle --no-daemon --console=plain resolve | grep commons-lang3 && echo RESTORED
EOF
      ;;
  esac
}

# restore <manager> <label> [docker-env...]: one attempt in a fresh workspace.
restore() {
  local manager="$1" label="$2"
  shift 2
  local ws since result="$out/$manager-$label.txt"
  cell_selected "$result" || { grep -q '^RESTORED$' "$result" 2> /dev/null; return; }
  ws="$(new_workspace "pm-$manager")" || { log "cannot prepare a workspace for $manager"; exit 1; }
  since="$(proxy_now)"
  run_cell "$result" "$(image_of "$manager")" "$ws" "${net[@]}" "${proxy_env[@]}" "$@" \
    -- bash -c "set -e; cd /workspace; timeout 420 bash -s" < <(restore_script "$manager")
  sleep 1
  proxy_hosts_since "$since" > "$out/$manager-$label.hosts.txt"
  grep -q '^RESTORED$' "$result"
}

for manager in "${managers[@]}"; do
  image="$(image_of "$manager")"
  [ -n "$image" ] || { log "unknown manager: $manager"; exit 2; }
  log "$manager ($image)"
  docker pull --quiet "$image" > /dev/null
  docker image inspect "$image" --format '{{index .RepoDigests 0}}' > "$out.tmp" 2> /dev/null || true
  mkdir -p "$out"
  mv "$out.tmp" "$out/$manager.image.txt"
  # Attempts left by an earlier run must not be read as this run's.
  [ -n "${SPIKE_ONLY:-}" ] || rm -f "$out/$manager"-*.txt

  if restore "$manager" generic-variables; then
    log "  works with the generic proxy variables alone"
    continue
  fi
  case "$manager" in
    maven | gradle)
      if restore "$manager" jvm-proxy-properties --env "JAVA_TOOL_OPTIONS=$jvm_proxy"; then
        log "  works with JAVA_TOOL_OPTIONS carrying the proxy"
      elif [ "$manager" = maven ] && restore maven settings-xml --env SPIKE_MAVEN_SETTINGS=1; then
        log "  works with a proxy element in settings.xml"
      else
        log "  FAILS with every setting tried"
      fi
      ;;
    *) log "  FAILS with the generic proxy variables; no further setting tried" ;;
  esac
done

# Summary.
outcome() { # <result-file>
  if [ ! -f "$1" ]; then echo "not tried"; elif grep -q '^RESTORED$' "$1"; then echo works; else echo fails; fi
}
{
  echo "| Manager | Image | Generic variables alone | JAVA_TOOL_OPTIONS | settings.xml proxy | Hosts asked of the proxy |"
  echo "| --- | --- | --- | --- | --- | --- |"
  for manager in npm dotnet pip cargo go maven gradle; do
    [ -f "$out/$manager-generic-variables.txt" ] || continue
    hosts="$(cat "$out/$manager"-*.hosts.txt 2> /dev/null | sort -u | paste -sd ' ' -)"
    echo "| $manager | $(cat "$out/$manager.image.txt" 2> /dev/null) | $(outcome "$out/$manager-generic-variables.txt") | $(outcome "$out/$manager-jvm-proxy-properties.txt") | $(outcome "$out/$manager-settings-xml.txt") | ${hosts:-none} |"
  done
} > "$out/summary.md"
cat "$out/summary.md"
