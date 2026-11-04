# Build context is the repository root (db/ is embedded into Codex.Application):
#   docker build -f src/Dockerfile -t codex .

# Stage 1: build the SPA straight into the API project's wwwroot.
FROM node:24-alpine AS frontend
# pnpm at the version package.json's packageManager pins, through Node's own corepack.
RUN corepack enable
# The pnpm workspace is src/: the SPA and the platform-neutral core it imports (src/Codex.Core.Client, #35).
WORKDIR /source/src
COPY src/package.json src/pnpm-lock.yaml src/pnpm-workspace.yaml ./
COPY src/Codex.Client/package.json Codex.Client/
COPY src/Codex.Core.Client/package.json Codex.Core.Client/
# The mobile app (src/Codex.Mobile) is in the workspace too: its package.json keeps the lockfile check whole, and the
# filter installs only the SPA and what it imports, not Expo.
COPY src/Codex.Mobile/package.json Codex.Mobile/
RUN --mount=type=cache,id=pnpm,target=/root/.local/share/pnpm/store pnpm install --frozen-lockfile --filter codex-client...
COPY src/Codex.Core.Client/ Codex.Core.Client/
COPY src/Codex.Client/ Codex.Client/
WORKDIR /source/src/Codex.Client
# Bundle only: CI's build job typechecks (tsc also covers tests, which import fixtures from outside the client
# folder and are not in this stage).
RUN pnpm exec vite build

# Stage 2: publish the API with the SPA already inside wwwroot, so the
# published output is the complete app — no copy step in the runtime stage.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Stamped into AssemblyInformationalVersion ("1.0.0+<sha>") so /health names the
# running build. docker-publish.yml passes the commit; a local build says "unknown".
ARG SOURCE_COMMIT=unknown
WORKDIR /source
# Project files first: the restore layer only changes when they do.
COPY src/Codex.slnx src/Directory.Build.props src/
COPY src/Codex.WebApi/Codex.WebApi.csproj src/Codex.WebApi/
COPY src/Codex.Application/Codex.Application.csproj src/Codex.Application/
COPY src/Codex.Core/Codex.Core.csproj src/Codex.Core/
COPY src/Codex.Contracts/Codex.Contracts.csproj src/Codex.Contracts/
# The packages stay in this layer, not a cache mount: CI's layer cache keeps layers but not mounts, so a cached restore
# would leave the publish below with no packages.
RUN dotnet restore src/Codex.WebApi -r linux-x64
COPY src/ ./src/
COPY db/ ./db/
COPY prompts/ ./prompts/
COPY --from=frontend /source/src/Codex.WebApi/wwwroot/ ./src/Codex.WebApi/wwwroot/
# Analyzers off here: CI already builds every project with them on and warnings as errors.
RUN dotnet publish src/Codex.WebApi -c Release -r linux-x64 --self-contained false --no-restore -o /app -p:SourceRevisionId=$SOURCE_COMMIT -p:RunAnalyzers=false

# codex-sandbox (src/sandbox/codex-sandbox.c): Landlock and rlimits around every pandoc run, as in Dockerfile.worker.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sandbox
RUN apt-get update && apt-get install -y --no-install-recommends gcc libc6-dev && rm -rf /var/lib/apt/lists/*
COPY src/sandbox/codex-sandbox.c /src/codex-sandbox.c
RUN gcc -O2 -static -Wall -Wextra -o /codex-sandbox /src/codex-sandbox.c

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl: the HEALTHCHECK below calls GET /health with it.
# pandoc and codex-sandbox: Quick preview (L7.3) renders HTML in the web process. Tectonic (PDF) stays in the worker.
# PANDOC_VERSION matches Dockerfile.worker and scripts/install-print-tools.sh.
ARG PANDOC_VERSION=3.12
# TECTONIC_VERSION is not installed here: it only names the worker's pin in CODEX_TOOLCHAIN, so web and worker hash alike.
ARG TECTONIC_VERSION=0.15.0
ARG PANDOC_SHA256=67d7d011fed8c8543306022b985b9b2499ab9b74818df91d8727c7e9ebc5ba06
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && curl -fsSL -o /tmp/pandoc.tgz "https://github.com/jgm/pandoc/releases/download/${PANDOC_VERSION}/pandoc-${PANDOC_VERSION}-linux-amd64.tar.gz" \
    && echo "${PANDOC_SHA256}  /tmp/pandoc.tgz" | sha256sum -c - \
    && tar -xzf /tmp/pandoc.tgz -C /tmp \
    && install -m 0755 "/tmp/pandoc-${PANDOC_VERSION}/bin/pandoc" /usr/local/bin/pandoc \
    && rm -rf /tmp/pandoc.tgz "/tmp/pandoc-${PANDOC_VERSION}" /var/lib/apt/lists/*
COPY --from=sandbox /codex-sandbox /usr/local/bin/codex-sandbox
# The toolchain string the content hash covers; Dockerfile.worker sets the same value from the same pins (a test compares them).
ENV CODEX_TOOLCHAIN="pandoc ${PANDOC_VERSION}|Tectonic ${TECTONIC_VERSION}"

WORKDIR /app
COPY --from=build /app .
# Built-in layout template packages (CODEX_TEMPLATES_ROOT's default resolves to /templates).
COPY templates/ /templates/
# CODEX_BLOB_ROOT and CODEX_DATA_PROTECTION_DIR in compose: owned by the app user, so the named volumes mounted here start writable.
RUN mkdir -p /data/blobs /data/keys && chown $APP_UID /data/blobs /data/keys

USER $APP_UID
# UTC everywhere (.claude/skills/utc-everywhere): the process's local zone, and every Postgres session's TimeZone.
ENV TZ=UTC PGTZ=UTC
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=60s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "Codex.WebApi.dll"]
