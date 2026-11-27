# The hephaestus image: the one executable, `hephaestus`, as the host (`hephaestus serve`) and as the command line.
# Build from the repository root:  docker build -f deploy/Dockerfile -t hephaestus .
# Builds for linux/amd64 and linux/arm64; the SDK stage always runs on the build machine and cross-compiles.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first, so a change to source code does not repeat it.
COPY global.json NuGet.config .editorconfig Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY src/Hephaestus.Core/Hephaestus.Core.csproj src/Hephaestus.Core/
COPY src/Hephaestus.Agents/Hephaestus.Agents.csproj src/Hephaestus.Agents/
COPY src/Hephaestus.GitHub/Hephaestus.GitHub.csproj src/Hephaestus.GitHub/
COPY src/Hephaestus.Workspaces/Hephaestus.Workspaces.csproj src/Hephaestus.Workspaces/
COPY src/Hephaestus.Persistence/Hephaestus.Persistence.csproj src/Hephaestus.Persistence/
COPY src/Hephaestus.Host/Hephaestus.Host.csproj src/Hephaestus.Host/
RUN case "$TARGETARCH" in \
      amd64) echo x64 ;; \
      arm64) echo arm64 ;; \
      *) echo "unsupported architecture $TARGETARCH" >&2; exit 1 ;; \
    esac > /arch \
 && dotnet restore src/Hephaestus.Host/Hephaestus.Host.csproj -a "$(cat /arch)"

COPY src/ src/
# The Methodologies the image ships; the publish step places them beside the executable.
COPY methodologies/ methodologies/
# The lock of the Agent CLIs this release was tested with (AD-8). The image holds the lock and no Agent binary: the host fetches the
# pinned executables from each vendor into the `hephaestus-agents` volume at start, because Claude Code may not be redistributed.
COPY deploy/agents.lock deploy/agents.lock
RUN dotnet publish src/Hephaestus.Host/Hephaestus.Host.csproj \
      --no-restore -c Release -a "$(cat /arch)" --no-self-contained -o /app \
 && mv /app/Hephaestus.Host /app/hephaestus

# Ubuntu 26.04: the `git` it carries is 2.53, and the checkout and the bare mirrors need 2.48 or later, which an earlier base does not have (AR-3, ADR-0010, Story 3.5).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-resolute AS runtime
WORKDIR /app
COPY --from=build /app ./

# git is the gateway's, for the bare mirrors of the Projects and the bundles made from them (AD-9). Without recommended packages there is no SSH client, so a repository is reached over HTTPS only.
# The build stops when the git that was installed is older than 2.48, the version the architecture requires of both images (AR-3): it is the first to check the objects of a bundle it fetches, which the import of a bundle relies on (ADR-0010, Story 3.7).
RUN apt-get update \
 && apt-get install --no-install-recommends --yes git ca-certificates \
 && rm -rf /var/lib/apt/lists/* \
 && git --version | awk '{ split($3, v, "."); exit !(v[1] > 2 || (v[1] == 2 && v[2] >= 48)) }'

# `docker exec hephaestus hephaestus ...` is the command line; it finds the control socket in the same place the host made it.
# The aspnet image sets ASPNETCORE_HTTP_PORTS; the host listens on a Unix socket only, so no port is named here.
# The data and secrets directories are the mount points of the `hephaestus-data` and `hephaestus-secrets` volumes. A named
# volume copies their owner and mode from the image on first use, so the process user owns both and only it can enter them.
# The mirrors directory is the mount point of `hephaestus-mirrors`, one bare repository per Project, which only the gateway writes and no step container mounts (Story 3.5).
# The agents directory is the mount point of `hephaestus-agents`, which the host fills at start and every step container mounts
# read-only; it is readable by every user because a step container runs as a user of its own, and writable by the process user alone.
ENV PATH="/app:${PATH}" \
    HEPHAESTUS_CONTROL_SOCKET=/run/hephaestus/control.sock \
    HEPHAESTUS_DATA_DIR=/var/lib/hephaestus/data \
    HEPHAESTUS_SECRETS_DIR=/var/lib/hephaestus/secrets \
    HEPHAESTUS_AGENTS_DIR=/var/lib/hephaestus/agents \
    HEPHAESTUS_MIRRORS_DIR=/var/lib/hephaestus/mirrors \
    ASPNETCORE_HTTP_PORTS=
RUN mkdir -p /run/hephaestus /var/lib/hephaestus/data /var/lib/hephaestus/secrets /var/lib/hephaestus/agents /var/lib/hephaestus/mirrors \
 && chown "$APP_UID" /run/hephaestus /var/lib/hephaestus/data /var/lib/hephaestus/secrets /var/lib/hephaestus/agents /var/lib/hephaestus/mirrors \
 && chmod 700 /run/hephaestus /var/lib/hephaestus/data /var/lib/hephaestus/secrets /var/lib/hephaestus/mirrors \
 && chmod 755 /var/lib/hephaestus/agents

USER $APP_UID
ENTRYPOINT ["hephaestus", "serve"]
