# ── Stage 1: Install dependencies ─────────────────────────────────
FROM node:22-alpine3.20 AS deps

RUN corepack enable && corepack prepare pnpm@10.30.3 --activate

WORKDIR /app

COPY pnpm-lock.yaml pnpm-workspace.yaml package.json turbo.json ./
COPY apps/web-svelte/package.json apps/web-svelte/package.json
COPY packages/ui-svelte/package.json packages/ui-svelte/package.json
COPY packages/contracts/package.json packages/contracts/package.json
COPY packages/media-core/package.json packages/media-core/package.json
COPY packages/stash-compat/package.json packages/stash-compat/package.json
COPY packages/plugins/package.json packages/plugins/package.json

RUN pnpm install --frozen-lockfile

# ── Stage 2: Build all services ──────────────────────────────────
FROM node:22-alpine3.20 AS builder

RUN corepack enable && corepack prepare pnpm@10.30.3 --activate

WORKDIR /app

# Copy entire deps output — preserves pnpm's symlink structure
COPY --from=deps /app ./
COPY . .

RUN pnpm release:check && pnpm --filter @prismedia/web-svelte build

# ── Stage 3a: Build prismedia-phash (Stash-compatible video pHash) ──
FROM golang:1.23-alpine AS phash-builder

RUN apk add --no-cache git

WORKDIR /src/phash
COPY infra/phash/go.mod infra/phash/go.sum ./
RUN go mod download

COPY infra/phash/ ./
RUN CGO_ENABLED=0 go build -ldflags="-s -w" -o /out/prismedia-phash .

# ── Stage 3: Build audiowaveform from source ────────────────────
FROM ubuntu:noble AS audiowaveform-builder

RUN apt-get update \
  && apt-get install -y --no-install-recommends \
    ca-certificates \
    cmake \
    git \
    g++ \
    make \
    libboost-filesystem-dev \
    libboost-program-options-dev \
    libboost-regex-dev \
    libgd-dev \
    libid3tag0-dev \
    libmad0-dev \
    libsndfile1-dev \
  && rm -rf /var/lib/apt/lists/*

RUN git clone --depth 1 https://github.com/bbc/audiowaveform.git /build/audiowaveform \
  && cd /build/audiowaveform \
  && mkdir build && cd build \
  && cmake -DCMAKE_BUILD_TYPE=Release -DENABLE_TESTS=0 .. \
  && make -j"$(nproc)" \
  && make install

# ── Stage 4: Publish .NET API and worker ─────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS dotnet-builder

WORKDIR /src
COPY . .
COPY --from=builder /app/apps/web-svelte/build ./apps/web-svelte/build
RUN dotnet publish apps/backend/src/Prismedia.Api/Prismedia.Api.csproj -c Release -o /out/api \
  && dotnet publish apps/backend/src/Prismedia.Worker/Prismedia.Worker.csproj -c Release -o /out/worker

# ── Stage 5: Unified production image ────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runner

# Install runtime dependencies, PostgreSQL 16, and Jellyfin FFmpeg.
ARG TARGETARCH
ARG JELLYFIN_FFMPEG_VERSION=7.1.3-6
RUN set -eux; \
  apt-get update; \
  apt-get install -y --no-install-recommends ca-certificates curl; \
  case "$TARGETARCH" in \
    amd64) jellyfin_arch=amd64 ;; \
    arm64) jellyfin_arch=arm64 ;; \
    *) echo "Unsupported Jellyfin FFmpeg architecture: $TARGETARCH" >&2; exit 1 ;; \
  esac; \
  curl -fsSL \
    "https://repo.jellyfin.org/files/ffmpeg/ubuntu/latest-7.x/${jellyfin_arch}/jellyfin-ffmpeg7_${JELLYFIN_FFMPEG_VERSION}-noble_${jellyfin_arch}.deb" \
    -o /tmp/jellyfin-ffmpeg.deb; \
  apt-get install -y --no-install-recommends \
    /tmp/jellyfin-ffmpeg.deb \
    gosu \
    libboost-filesystem1.83.0 \
    libboost-program-options1.83.0 \
    libboost-regex1.83.0 \
    libgd3 \
    libheif1 \
    libid3tag0 \
    libmad0 \
    libsndfile1 \
    postgresql-16 \
    postgresql-client-16 \
    postgresql-contrib-16; \
  rm -rf /var/lib/apt/lists/* /tmp/jellyfin-ffmpeg.deb; \
  mkdir -p /data/postgres /data/cache /media /run/postgresql; \
  chown -R postgres:postgres /data/postgres /run/postgresql

# Copy audiowaveform binary from builder
COPY --from=audiowaveform-builder /usr/local/bin/audiowaveform /usr/local/bin/audiowaveform

# Copy prismedia-phash binary (Stash-compatible video perceptual hash)
COPY --from=phash-builder /out/prismedia-phash /usr/local/bin/prismedia-phash
ENV PRISMEDIA_PHASH_BIN=/usr/local/bin/prismedia-phash

WORKDIR /app

# Explicit path so the changelog API route never has to guess
ENV CHANGELOG_PATH=/app/CHANGELOG.md
ENV PUBLIC_APP_URL=http://localhost:8008
ENV PUBLIC_API_URL=/api
ENV ASPNETCORE_URLS=http://0.0.0.0:8008
ENV PRISMEDIA_STATIC_WEB_ROOT=/app/wwwroot
ENV PRISMEDIA_FFMPEG_PATH=/usr/lib/jellyfin-ffmpeg/ffmpeg
ENV PRISMEDIA_FFPROBE_PATH=/usr/lib/jellyfin-ffmpeg/ffprobe
ENV PATH="/usr/lib/postgresql/16/bin:/usr/lib/jellyfin-ffmpeg:${PATH}"

COPY --from=dotnet-builder /out/api ./api
COPY --from=dotnet-builder /out/worker ./worker
COPY --from=builder /app/apps/web-svelte/build ./wwwroot
COPY CHANGELOG.md ./CHANGELOG.md
COPY infra/docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

VOLUME ["/data", "/media"]

EXPOSE 8008

ENTRYPOINT ["/entrypoint.sh"]
