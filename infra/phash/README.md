# prismedia-phash

Small Go helper that computes a video perceptual hash byte-compatible with
[Stash](https://github.com/stashapp/stash)'s `pkg/hash/videophash` pipeline.
This exists because Prismedia identifies scenes against StashDB / ThePornDB,
which share a single phash index — divergence from Stash's exact pipeline
would produce hashes that do not cluster with existing community fingerprints.

## Pipeline

Matches `stashapp/stash/pkg/hash/videophash/phash.go` step-for-step:

1. For `i` in `[0, 25)`, compute `t = 0.05*duration + i * (0.9*duration/25)`.
2. Extract a single frame per offset via ffmpeg, input-seek (`-ss` BEFORE `-i`),
   scaled to `width=160, height=-2` and encoded as BMP to stdout.
3. Paste the 25 frames into a 5×5 NRGBA montage using
   `github.com/disintegration/imaging`. Canvas size is `(W*5) × (H*5)` from
   the first decoded frame (aspect-ratio preserving — a 16:9 source yields
   a `800×450` montage, not `800×800`).
4. `goimagehash.PerceptionHash(montage)` → `uint64`.
5. Print as a lowercase 16-character hex string.

## Usage

```sh
prismedia-phash -file /path/to/video.mp4 -duration 123.45
```

Prints the hash on stdout. Exits non-zero with a human-readable error on stderr
for any failure (missing file, ffmpeg error, decode error).

## Build

```sh
cd infra/phash
go mod tidy
CGO_ENABLED=0 go build -ldflags="-s -w" -o prismedia-phash .
```

The unified Docker image builds this binary automatically in a dedicated
builder stage (`golang:1.23-alpine`) and copies it to `/usr/local/bin/prismedia-phash`.

## Dev fallback

The worker's `computePhash` helper in `@prismedia/media-core` probes for the
binary at `$PRISMEDIA_PHASH_BIN` (defaulting to `prismedia-phash` on `$PATH`) and
logs a warning + skips phash generation when it is missing, so dev machines
without the binary still run the rest of the fingerprint pipeline.

## DO NOT change these

- Seek order: input seek (`-ss` BEFORE `-i`). Output seek decodes different
  frames and breaks compatibility with StashDB's index.
- Frame count: exactly 25 (`columns*rows`).
- Step size: `0.9 * duration / 25` (not `0.9 * duration / 24`). Frame 24 lands
  at 91.4%, not 95%.
- Scale filter: `scale=160:-2` (width 160, height preserves aspect, divisible
  by 2). Do not force a square.
- Montage library: `disintegration/imaging` with an `NRGBA` background.
- Hash library: `github.com/corona10/goimagehash` `PerceptionHash`.
