import { spawn } from "node:child_process";
import { writeFile } from "node:fs/promises";
import path from "node:path";
import { runProcess } from "./process";

const audiowaveformNativeFormats = new Set([".mp3", ".wav", ".flac", ".ogg", ".opus"]);

async function hasBinary(name: string): Promise<boolean> {
  try {
    await runProcess("which", [name]);
    return true;
  } catch {
    return false;
  }
}

async function generateWaveformWithFfmpeg(
  inputPath: string,
  outputPath: string,
  pixelsPerSecond: number,
): Promise<void> {
  const probeResult = await runProcess("ffprobe", [
    "-v",
    "error",
    "-show_entries",
    "format=duration",
    "-of",
    "json",
    inputPath,
  ]);
  const probeParsed = JSON.parse(probeResult.stdout) as { format?: { duration?: string } };
  const duration = Number(probeParsed.format?.duration ?? 0);
  if (duration <= 0) {
    await writeFile(outputPath, JSON.stringify({ data: [] }), "utf8");
    return;
  }

  const totalSamples = Math.ceil(duration * pixelsPerSecond);
  const sampleRate = 8000;
  const totalPcmSamples = Math.ceil(duration * sampleRate);
  const samplesPerBucket = Math.max(1, Math.floor(totalPcmSamples / totalSamples));

  const pcmResult = await new Promise<Buffer>((resolve, reject) => {
    const ffmpeg = spawn(
      "ffmpeg",
      ["-i", inputPath, "-f", "s16le", "-ac", "1", "-ar", String(sampleRate), "pipe:1"],
      { stdio: ["ignore", "pipe", "ignore"] },
    );

    const chunks: Buffer[] = [];
    ffmpeg.stdout.on("data", (chunk: Buffer) => chunks.push(chunk));
    ffmpeg.on("close", (code) => {
      if (code === 0) resolve(Buffer.concat(chunks));
      else reject(new Error(`ffmpeg waveform decode exited with code ${code}`));
    });
    ffmpeg.on("error", reject);
  });

  const data: number[] = [];
  const sampleCount = Math.floor(pcmResult.length / 2);

  for (let bucket = 0; bucket < totalSamples; bucket++) {
    const startSample = bucket * samplesPerBucket;
    const endSample = Math.min(startSample + samplesPerBucket, sampleCount);
    let min = 0;
    let max = 0;

    for (let i = startSample; i < endSample; i++) {
      const sample = pcmResult.readInt16LE(i * 2);
      if (sample < min) min = sample;
      if (sample > max) max = sample;
    }

    data.push(min, max);
  }

  await writeFile(outputPath, JSON.stringify({ data }), "utf8");
}

export async function generateAudioWaveform(
  inputPath: string,
  outputPath: string,
  pixelsPerSecond = 20,
): Promise<void> {
  const useAudiowaveform = await hasBinary("audiowaveform");

  if (!useAudiowaveform) {
    await generateWaveformWithFfmpeg(inputPath, outputPath, pixelsPerSecond);
    return;
  }

  const ext = path.extname(inputPath).toLowerCase();

  if (audiowaveformNativeFormats.has(ext)) {
    await runProcess("audiowaveform", [
      "-i",
      inputPath,
      "--pixels-per-second",
      String(pixelsPerSecond),
      "-b",
      "8",
      "-o",
      outputPath,
    ]);
  } else {
    await new Promise<void>((resolve, reject) => {
      const ffmpeg = spawn(
        "ffmpeg",
        ["-i", inputPath, "-f", "wav", "-ac", "1", "-ar", "16000", "pipe:1"],
        { stdio: ["ignore", "pipe", "ignore"] },
      );

      const aw = spawn(
        "audiowaveform",
        [
          "--input-format",
          "wav",
          "--pixels-per-second",
          String(pixelsPerSecond),
          "-b",
          "8",
          "-o",
          outputPath,
        ],
        { stdio: ["pipe", "ignore", "pipe"] },
      );

      ffmpeg.stdout.pipe(aw.stdin);

      let awStderr = "";
      aw.stderr.on("data", (chunk) => {
        awStderr += chunk.toString();
      });

      aw.on("close", (code) => {
        if (code === 0) resolve();
        else reject(new Error(`audiowaveform exited with code ${code}: ${awStderr}`));
      });

      ffmpeg.on("error", reject);
      aw.on("error", reject);
    });
  }
}
