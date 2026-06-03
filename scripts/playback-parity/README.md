# Playback parity harness

Concrete, re-runnable evidence for "does a file that plays lag-free on Jellyfin
also play on Prismedia?" It A/Bs the two **stream-decision engines** against the
same Sample Media library, sending an **identical browser `DeviceProfile`** to both
so any difference is the engine, not the profile.

## What it measures

For every shared file it:

1. **ffprobes** ground truth — container, video codec/profile/level/bit-depth, video
   range (SDR/HDR10/HLG/DOVI), resolution, fps, audio codec/channels.
2. POSTs the same `DeviceProfile` to **both** servers' `PlaybackInfo` and records each
   verdict: **DirectPlay** (serve raw) / **Remux** (copy video, change container) /
   **Transcode** (re-encode video), plus chosen audio codec and — for Jellyfin — the
   `TranscodeReasons` that explain *why* it refused to copy.
3. With `--ttff`, times **time-to-first-segment** (request the produced playlist, then
   the first segment's first byte) — remux ≈ instant, transcode pays cold-start.

A **disagreement** (Prismedia and Jellyfin choose differently for the same browser) is
a parity gap. The headline ones: Prismedia copies raw **DOVI/HEVC-10** to browsers that
can't render it, where Jellyfin tonemaps to H.264; and Prismedia drops image subtitles
where Jellyfin burns them in.

## Profiles

`profiles.json` holds three canonical browser profiles (`chrome-mac`, `firefox`,
`safari`) in Jellyfin `DeviceProfile` shape, modeled on
`jellyfin-web/src/scripts/browserDeviceProfile.js`. They encode the **CodecProfile
constraints** (HEVC Main10/level/range, H.264 level/range, Safari `hvc1` tag + fps≤60)
that Jellyfin honors and Prismedia currently ignores — which is exactly the gap under test.

## Usage

```bash
# 1. Bring up the throwaway Jellyfin bench on the SAME media (idempotent).
./provision-jellyfin.sh

# 2. Prismedia API must be on :8008 and the dev postgres reachable.

# 3. Run the comparison.
python3 parity.py                                   # all profiles, decision-only
python3 parity.py --profiles chrome-mac,firefox     # subset
python3 parity.py --ttff --out results              # also time first-segment; save md+json
```

Output is a Markdown table (stdout) and, with `--out`, timestamped `.md` + `.json`
under `results/` for before/after comparison as the fixes land.

## Environment overrides

`PRISMEDIA_URL`, `PRISMEDIA_KEY`, `JF_URL`, `JF_USER`, `JF_PASS`, `FFPROBE`,
`PG_CONTAINER` — all default to the local dev stack.

## Notes

- The Jellyfin bench config lives in `/tmp/jf-bench-chair` (wiped on reboot), so
  `provision-jellyfin.sh` re-runs the startup wizard and re-creates the library as needed.
- The Prismedia id→path map is read from the dev postgres (`entity_files.role='source'`);
  Jellyfin ids come from its `/Items` API. Files are matched by basename.
