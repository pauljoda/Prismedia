---
sidebar_position: 1
title: pHash Contribution
description: How Prismedia generates Stash-compatible perceptual hashes and submits them back to the community index.
---

# pHash Contribution

Perceptual hashes (pHash) let you identify a video against the StashBox community index by content rather than by name. Prismedia's pHash pipeline intentionally matches Stash's so that the values cluster with the existing community fingerprint database.

## Why bit-for-bit compatibility matters

The whole point of a pHash is that two encodings of the same content produce the same hash. The community index has millions of values computed by Stash's pipeline; if Prismedia computed slightly-different values they wouldn't cluster, and identify-by-fingerprint would fail.

So the implementation matches Stash exactly:

- Frame selection (which frames, when)
- ffmpeg seek strategy (`-ss` before `-i`)
- Frame scale (width `160`)
- Montage layout (5×5 grid, preserve source aspect)
- Hash function (`goimagehash.PerceptionHash`)
- Hash format (lowercase 16-character hex)

Changing any of these produces values that no longer cluster. The implementation lives in `infra/phash/main.go`; treat it as a contract, not a starting point.

## Generation summary

1. **Sample 25 frames** evenly from 5 % through 91.4 % of the source duration.
2. **Use ffmpeg input seek** (`-ss` before `-i`) and scale each frame to width `160`, height computed to preserve aspect ratio.
3. **Compose a 5×5 montage** of the frames in capture order.
4. **Run `goimagehash.PerceptionHash`** on the montage.
5. **Store** the lowercase 16-character hex string in `video_episodes.phash` or `video_movies.phash`.

The helper binary is built from `infra/phash/main.go` and copied into the unified Docker image as `/usr/local/bin/prismedia-phash`. The worker shells out to it via the `PRISMEDIA_PHASH_BIN` env var (defaulting to `prismedia-phash` on `PATH`).

## When pHash is computed

- **On scan**, if `library_settings.generate_phash` is true (default).
- **On rebuild preview**, when the user clicks **Rebuild preview** on a video detail page.
- **On backfill**, via the **Backfill pHashes** diagnostic in **Settings → Generated Storage**.

If the helper binary isn't available — which can happen on a dev box without `prismedia-phash` on `PATH` — the worker logs a warning and skips the hash. The video gets `phash = NULL`; the rest of the pipeline still works.

## Contribution flow

```text
Identify  →  Accept (StashBox match)  →  Auto-link  →  Submit fingerprint
```

When you accept a StashBox-origin match in the cascade drawer:

1. The remote scene link is recorded in `stash_ids` (`entity_type = video_episode|video_movie`, `stashbox_endpoint_id`, `stash_id`).
2. The auto-submit job runs: every fingerprint Prismedia has for that video (MD5, OSHASH, PHASH) is submitted to the StashBox endpoint.
3. Each submission attempt is logged to `fingerprint_submissions` with status (`success`/`error`) and any error message.

Submitting back closes the loop — your hashes contribute to the community index, helping the next person identify the same content faster.

## Building the helper locally

If you're iterating on `prismedia-phash` outside Docker:

```bash
cd infra/phash
go mod tidy
go build -o prismedia-phash .
```

Put the resulting binary on `PATH` or point Prismedia at it:

```bash
export PRISMEDIA_PHASH_BIN=/path/to/prismedia-phash
```

The helper takes a video file path as its only argument and prints the hash to stdout:

```bash
./prismedia-phash /path/to/video.mkv
# → 8f3a2b1c4d5e6f70
```

## Troubleshooting

**"pHash generation skipped"** — the binary isn't on `PATH` or at `PRISMEDIA_PHASH_BIN`. The worker logs a warning per file. Fix the path; future scans will compute the hash.

**Hashes don't match the community index** — the most likely culprit is a different ffmpeg version with different default scaling behavior. The unified Docker image pins ffmpeg specifically; using a different ffmpeg locally can drift the values. Use the unified image when contributing back.

**Slow generation** — pHash for a 4K source can take 30+ seconds because the helper has to seek 25 times and scale each frame. The pHash queue is set to concurrency 1 globally to avoid thrashing the disk; you can raise concurrency in **Settings → Watched Libraries → Background worker concurrency** at the cost of more I/O contention.

**Submission failures** — check the `fingerprint_submissions` table for the error string. Common cases:

- Endpoint API key invalid → re-check the endpoint config in **Plugins → StashBox**.
- Endpoint rate-limited → submissions retry on the next identify pass.
- Endpoint doesn't accept your hash → some endpoints only accept specific algorithms.

## Reading the source

If you're going deeper:

- `infra/phash/main.go` — the helper.
- `packages/media-core/src/index.ts` — `computeFingerprint('video-phash', filePath)` glue.
- `apps/backend` — when pHash is computed during a scan.
- `packages/stash-compat/src/stashbox/client.ts` — submission GraphQL.
- `apps/backend/src/Prismedia.Infrastructure/Persistence` — fingerprint submission and Stash ID persistence.
