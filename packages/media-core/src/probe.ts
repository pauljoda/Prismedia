import path from "node:path";
import { CorruptMediaError, isCorruptMediaError, runProcess } from "./process";

interface FfprobeStream {
  codec_name?: string;
  codec_type?: string;
  width?: number;
  height?: number;
  avg_frame_rate?: string;
  sample_rate?: string;
  channels?: number;
}

interface FfprobeFormat {
  duration?: string;
  size?: string;
  bit_rate?: string;
  format_name?: string;
}

interface FfprobeResult {
  streams?: FfprobeStream[];
  format?: FfprobeFormat;
}

export interface ProbeAudioMetadata {
  codec: string | null;
  sampleRate: number | null;
  channels: number | null;
}

export interface ProbeVideoMetadata {
  filePath: string;
  fileName: string;
  duration: number | null;
  fileSize: number | null;
  bitRate: number | null;
  width: number | null;
  height: number | null;
  frameRate: number | null;
  codec: string | null;
  container: string | null;
  audio: ProbeAudioMetadata | null;
}

export function parseFrameRate(value?: string): number | null {
  if (!value) return null;
  const [numerator, denominator] = value.split("/").map(Number);
  if (!Number.isFinite(numerator) || !Number.isFinite(denominator) || denominator === 0) {
    return null;
  }

  return Number((numerator / denominator).toFixed(3));
}

export async function probeVideoFile(filePath: string): Promise<ProbeVideoMetadata> {
  let stdout: string;
  try {
    ({ stdout } = await runProcess("ffprobe", [
      "-v",
      "error",
      "-show_entries",
      "format=duration,size,bit_rate,format_name:stream=index,codec_type,codec_name,width,height,avg_frame_rate,sample_rate,channels",
      "-of",
      "json",
      filePath,
    ]));
  } catch (err) {
    if (isCorruptMediaError(err)) {
      throw new CorruptMediaError(filePath, err instanceof Error ? err : undefined);
    }
    throw err;
  }

  const parsed = JSON.parse(stdout) as FfprobeResult;
  const videoStream = parsed.streams?.find((stream) => stream.codec_type === "video");
  const audioStream = parsed.streams?.find((stream) => stream.codec_type === "audio");
  const formatName = parsed.format?.format_name?.split(",")[0] ?? null;
  const extContainer = path.extname(filePath).replace(".", "");
  const container = formatName ?? (extContainer || null);

  return {
    filePath,
    fileName: path.basename(filePath),
    duration: parsed.format?.duration ? Number(parsed.format.duration) : null,
    fileSize: parsed.format?.size ? Number(parsed.format.size) : null,
    bitRate: parsed.format?.bit_rate ? Number(parsed.format.bit_rate) : null,
    width: videoStream?.width ?? null,
    height: videoStream?.height ?? null,
    frameRate: parseFrameRate(videoStream?.avg_frame_rate),
    codec: videoStream?.codec_name ?? null,
    container,
    audio: audioStream
      ? {
          codec: audioStream.codec_name ?? null,
          sampleRate: audioStream.sample_rate ? Number(audioStream.sample_rate) : null,
          channels: audioStream.channels ?? null,
        }
      : null,
  };
}

export async function probeImageFile(filePath: string): Promise<{
  width: number | null;
  height: number | null;
  format: string | null;
}> {
  try {
    const { stdout } = await runProcess("ffprobe", [
      "-v",
      "error",
      "-select_streams",
      "v:0",
      "-show_entries",
      "stream=width,height,codec_name",
      "-of",
      "json",
      filePath,
    ]);

    const parsed = JSON.parse(stdout) as {
      streams?: Array<{ width?: number; height?: number; codec_name?: string }>;
    };
    const stream = parsed.streams?.[0];

    return {
      width: stream?.width ?? null,
      height: stream?.height ?? null,
      format: stream?.codec_name ?? null,
    };
  } catch {
    return { width: null, height: null, format: null };
  }
}

interface FfprobeFormatTags {
  artist?: string;
  album?: string;
  title?: string;
  track?: string;
  ARTIST?: string;
  ALBUM?: string;
  TITLE?: string;
  TRACK?: string;
}

interface FfprobeAudioResult {
  streams?: FfprobeStream[];
  format?: FfprobeFormat & { tags?: FfprobeFormatTags };
}

export interface ProbeAudioFileMetadata {
  filePath: string;
  fileName: string;
  duration: number | null;
  fileSize: number | null;
  bitRate: number | null;
  sampleRate: number | null;
  channels: number | null;
  codec: string | null;
  container: string | null;
  embeddedArtist: string | null;
  embeddedAlbum: string | null;
  embeddedTitle: string | null;
  trackNumber: number | null;
}

export async function probeAudioFile(filePath: string): Promise<ProbeAudioFileMetadata> {
  const { stdout } = await runProcess("ffprobe", [
    "-v",
    "error",
    "-show_entries",
    "format=duration,size,bit_rate,format_name:format_tags=artist,album,title,track:stream=codec_name,sample_rate,channels",
    "-of",
    "json",
    filePath,
  ]);

  const parsed = JSON.parse(stdout) as FfprobeAudioResult;
  const audioStream = parsed.streams?.find((s) => s.codec_name);
  const tags = parsed.format?.tags;
  const formatName = parsed.format?.format_name?.split(",")[0] ?? null;
  const extContainer = path.extname(filePath).replace(".", "").toLowerCase();
  const artist = tags?.artist ?? tags?.ARTIST ?? null;
  const album = tags?.album ?? tags?.ALBUM ?? null;
  const title = tags?.title ?? tags?.TITLE ?? null;
  const trackStr = tags?.track ?? tags?.TRACK ?? null;
  const trackNumber = trackStr ? parseInt(trackStr, 10) : null;

  return {
    filePath,
    fileName: path.basename(filePath),
    duration: parsed.format?.duration ? Number(parsed.format.duration) : null,
    fileSize: parsed.format?.size ? Number(parsed.format.size) : null,
    bitRate: parsed.format?.bit_rate ? Number(parsed.format.bit_rate) : null,
    sampleRate: audioStream?.sample_rate ? Number(audioStream.sample_rate) : null,
    channels: audioStream?.channels ?? null,
    codec: audioStream?.codec_name ?? null,
    container: formatName ?? (extContainer || null),
    embeddedArtist: artist || null,
    embeddedAlbum: album || null,
    embeddedTitle: title || null,
    trackNumber: Number.isFinite(trackNumber) ? trackNumber : null,
  };
}
