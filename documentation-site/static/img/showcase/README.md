# Product screenshots

The `*-live.webp` images are captures of the running Prismedia web and native apps.
Use actual product screens when refreshing the homepage, and retain their original
resolution when encoding the source captures as WebP.

| Asset | Resolution | View |
| --- | --- | --- |
| `web-movies-live.webp` | 2880 × 1800 | Web movie library |
| `web-playback-live.webp` | 2338 × 1314 | Web player with a paused film, subtitles, and playback controls |
| `tvos-movies-live.webp` | 3840 × 2160 | Native Apple TV movie library |
| `ios-book-cover-live.webp` | 1206 × 2622 | Native book detail |
| `ios-book-live.webp` | 1206 × 2622 | Native reading and listening progress |
| `ios-reader-settings-live.webp` | 1206 × 2622 | Native reader appearance and typography |
| `ios-music-live.webp` | 1206 × 2622 | Native music player |

Capture with NSFW disabled. Keep account information, server addresses, file paths,
and personal media out of published images. Let artwork finish loading before
capturing, and inspect the complete image before publishing it. Use the app's
controls to demonstrate the feature; do not fabricate interface states or metadata.

The hero links directly to each image so visitors can inspect the full-size view.

The `-480`, `-960`, `-1440`, and `-1920` WebP variants are delivery sizes for
`ProductScreenshot`. Generate only sizes smaller than the original, preserving
aspect ratio. Refresh these whenever the original changes; for each applicable
width, use FFmpeg's `scale=WIDTH:-1:flags=lanczos` filter and `libwebp` at quality
88. The `sizes` attribute should reflect the displayed width of the component.
