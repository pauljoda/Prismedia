import type { JellyfinDeviceProfile, JellyfinDirectPlayProfile } from "$lib/api/playback";

/**
 * Builds a Jellyfin-style device profile describing what this browser's `<video>` element can play
 * directly, so the server can decide DirectPlay vs. transcode for the actual client instead of
 * guessing from the file extension. Probing is done with `HTMLMediaElement.canPlayType`, which is
 * the only capability signal a browser exposes.
 *
 * Only containers a browser can natively demux are advertised (mp4 and webm); notably MKV is never
 * listed, so Matroska sources always fall back to transcoding. Codecs are probed per container and
 * only the supported ones are sent, which lets the server distinguish, for example, an H.264 mp4
 * (direct play) from an HEVC or AV1 mp4 on a browser that cannot decode it (transcode).
 */

type CanPlayType = (mime: string) => string;

interface CodecProbe {
  /** Server-facing codec token (Jellyfin convention). */
  readonly codec: string;
  /** Candidate MIME strings; the codec counts as supported if any returns "probably"/"maybe". */
  readonly mimes: readonly string[];
}

const Mp4VideoCodecs: readonly CodecProbe[] = [
  { codec: "h264", mimes: ['video/mp4; codecs="avc1.640029"', 'video/mp4; codecs="avc1.42E01E"'] },
  {
    codec: "hevc",
    mimes: [
      'video/mp4; codecs="hvc1.1.6.L93.B0"',
      'video/mp4; codecs="hev1.1.6.L93.B0"',
      'video/mp4; codecs="hvc1"',
      'video/mp4; codecs="hev1"',
    ],
  },
  { codec: "av1", mimes: ['video/mp4; codecs="av01.0.05M.08"'] },
];

const Mp4AudioCodecs: readonly CodecProbe[] = [
  { codec: "aac", mimes: ['audio/mp4; codecs="mp4a.40.2"', 'audio/mp4; codecs="mp4a.40.5"'] },
  { codec: "ac3", mimes: ['audio/mp4; codecs="ac-3"'] },
  { codec: "eac3", mimes: ['audio/mp4; codecs="ec-3"'] },
  { codec: "flac", mimes: ['audio/mp4; codecs="flac"'] },
  { codec: "opus", mimes: ['audio/mp4; codecs="opus"', 'audio/mp4; codecs="Opus"'] },
  { codec: "mp3", mimes: ['audio/mp4; codecs="mp4a.69"', "audio/mpeg"] },
];

const WebmVideoCodecs: readonly CodecProbe[] = [
  { codec: "vp9", mimes: ['video/webm; codecs="vp09.00.10.08"', 'video/webm; codecs="vp9"'] },
  { codec: "vp8", mimes: ['video/webm; codecs="vp8"'] },
  { codec: "av1", mimes: ['video/webm; codecs="av01.0.05M.08"'] },
];

const WebmAudioCodecs: readonly CodecProbe[] = [
  { codec: "opus", mimes: ['audio/webm; codecs="opus"'] },
  { codec: "vorbis", mimes: ['audio/webm; codecs="vorbis"'] },
];

function supportedCodecs(canPlayType: CanPlayType, probes: readonly CodecProbe[]): string[] {
  return probes
    .filter((probe) =>
      probe.mimes.some((mime) => {
        const result = canPlayType(mime);
        return result === "probably" || result === "maybe";
      }),
    )
    .map((probe) => probe.codec);
}

/**
 * Builds a device profile from an explicit `canPlayType` probe. Exposed for testing; app code
 * should call {@link getBrowserDeviceProfile}.
 */
export function buildBrowserDeviceProfile(canPlayType: CanPlayType): JellyfinDeviceProfile {
  const directPlayProfiles: JellyfinDirectPlayProfile[] = [];

  const mp4Video = supportedCodecs(canPlayType, Mp4VideoCodecs);
  if (mp4Video.length > 0) {
    directPlayProfiles.push({
      Type: "Video",
      Container: "mp4",
      VideoCodec: mp4Video.join(","),
      AudioCodec: supportedCodecs(canPlayType, Mp4AudioCodecs).join(","),
    });
  }

  const webmVideo = supportedCodecs(canPlayType, WebmVideoCodecs);
  if (webmVideo.length > 0) {
    directPlayProfiles.push({
      Type: "Video",
      Container: "webm",
      VideoCodec: webmVideo.join(","),
      AudioCodec: supportedCodecs(canPlayType, WebmAudioCodecs).join(","),
    });
  }

  return { DirectPlayProfiles: directPlayProfiles };
}

let cachedProfile: JellyfinDeviceProfile | null = null;

/**
 * Returns this browser's device profile, probing once and caching the result. Returns undefined
 * during SSR (no document), so callers can spread it into a request without sending an empty profile.
 */
export function getBrowserDeviceProfile(): JellyfinDeviceProfile | undefined {
  if (typeof document === "undefined") {
    return undefined;
  }

  if (cachedProfile) {
    return cachedProfile;
  }

  const probe = document.createElement("video");
  cachedProfile = buildBrowserDeviceProfile((mime) => probe.canPlayType(mime));
  return cachedProfile;
}
