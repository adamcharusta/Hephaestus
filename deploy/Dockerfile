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
RUN case "$TARGETARCH" in amd64) echo x64 ;; arm64) echo arm64 ;; *) echo "unsupported architecture $TARGETARCH" >&2; exit 1 ;; esac > /arch \
 && dotnet restore src/Hephaestus.Host/Hephaestus.Host.csproj -a "$(cat /arch)"

COPY src/ src/
RUN dotnet publish src/Hephaestus.Host/Hephaestus.Host.csproj \
      --no-restore -c Release -a "$(cat /arch)" --no-self-contained -o /app \
 && mv /app/Hephaestus.Host /app/hephaestus

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# `docker exec hephaestus hephaestus ...` is the command line; it finds the control socket in the same place the host made it.
# The aspnet image sets ASPNETCORE_HTTP_PORTS; the host listens on a Unix socket only, so no port is named here.
ENV PATH="/app:${PATH}" \
    HEPHAESTUS_CONTROL_SOCKET=/run/hephaestus/control.sock \
    ASPNETCORE_HTTP_PORTS=
RUN mkdir -p /run/hephaestus \
 && chown "$APP_UID" /run/hephaestus \
 && chmod 700 /run/hephaestus

USER $APP_UID
ENTRYPOINT ["hephaestus", "serve"]
