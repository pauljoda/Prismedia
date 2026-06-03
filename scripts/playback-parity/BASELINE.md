# Web player parity — baseline (2026-06-03)

Captured with `parity.py` against the `jf-chair` Jellyfin bench on the same Sample
Media library. The **same** browser `DeviceProfile` is sent to both engines, so every
difference is the decision engine, not the profile. Raw tables in `results/`.

## Headline

Prismedia does **not** under-serve — it **over-remuxes**. For HEVC-capable browsers it
copies the original stream where Jellyfin transcodes to a safe target, and it ignores
the constraints that say not to. That is the mechanism behind "plays on Jellyfin, not
on ours": the browser is handed a stream it cannot decode/render, with no fallback.

## Decision disagreements (identical profile to both)

| Browser | Disagreements | Cause(s) |
|---|---|---|
| chrome-mac | 4 | DOVI remux (range), HEVC + image subs |
| firefox | **0 (parity)** | HEVC not advertised → Prismedia transcodes, same as Jellyfin |
| safari | 6 | DOVI remux (range), `hvc1` tag |

All disagreements reduce to three Jellyfin `TranscodeReasons` that Prismedia ignores:

1. **`VideoRangeTypeNotSupported` (Dolby Vision)** — *the headline gap.* Prismedia remuxes
   raw DOVI HEVC to Chrome/Safari, which cannot render it. Jellyfin tonemaps to H.264.
2. **`SubtitleCodecNotSupported`** — image subs (PGS/VOBSUB). Prismedia copies video and
   drops the subtitle. Jellyfin burns it in.
3. **`VideoCodecTagNotSupported`** — Safari requires the `hvc1` tag. Prismedia force-retags
   in remux so this one may already be fine in-browser (needs verification).

## Performance (time-to-first-segment, 4K DOVI file)

| Case | Prismedia | Jellyfin | Note |
|---|---|---|---|
| chrome — DOVI | Remux **0.15s** | Transcode **4.56s** | PM "faster" but serves undecodable DOVI |
| firefox — DOVI (both transcode) | Transcode **10.98s** | Transcode **12.16s** | **PM transcode already at parity — marginally faster** |

**Conclusion:** Prismedia's *transcode* performance already matches/beats Jellyfin (the
hardware tonemap path works). The parity gap is entirely the **decision** (remuxing what
the browser can't decode) and the **lack of a fallback**, not transcode speed.

## Robustness finding (out of scope of the original ask, but real)

Interrupting a client mid-transcode (HTTP disconnect while the on-demand HLS session is
spinning up) **deadlocked the entire Prismedia API** — `/api/health` itself timed out at
0% CPU (thread-pool starvation). Jellyfin tolerates the same disconnect. Root-cause in
the HLS session machinery to be confirmed during Phase 1.

## Fix plan (this maps each gap to a phase)

- **Phase 1** — bulletproof client fallback: on any fatal decode/MSE error, re-request
  `PlaybackInfo` with `EnableDirectPlay=false` then `EnableDirectStream=false`, swapping
  the URL in place. Also fix the server disconnect-deadlock.
- **Phase 2** — honor `VideoRangeType` + codec constraints (`CodecProfiles`) so Prismedia
  stops remuxing DOVI/unsupported HEVC to browsers that can't render it.
- **Phase 3** — subtitles: burn-in on transcode + client ASS/PGS rendering.
- **Phase 4** — audio: independent branch (honor `CopyAudio`, multichannel).
- **Phase 5** — re-run this harness; prove disagreements → 0 and TTFF held.
