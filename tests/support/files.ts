import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";

export async function createTempDir(prefix: string) {
  return mkdtemp(path.join(os.tmpdir(), prefix));
}

export async function writeFixtureFile(
  dir: string,
  relativePath: string,
  content: string | Buffer,
) {
  const fullPath = path.join(dir, relativePath);
  await mkdir(path.dirname(fullPath), { recursive: true });
  await writeFile(fullPath, content);
  return fullPath;
}

export async function createSampleVideoFile(dir: string, name = "sample.mp4") {
  return writeFixtureFile(dir, name, "fake-video-bytes");
}

export async function createSampleSubtitleFile(
  dir: string,
  name = "sample.ass",
) {
  return writeFixtureFile(
    dir,
    name,
    `[Script Info]
Title: Sample

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,24,&H00FFFFFF,&H000000FF,&H00000000,&H66000000,0,0,0,0,100,100,0,0,1,2,0,2,20,20,20,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,Hello world
`,
  );
}

export async function createSampleNfoFile(dir: string, name = "sample.nfo") {
  return writeFixtureFile(
    dir,
    name,
    `<movie>
  <title>Fixture Title</title>
  <plot>Fixture plot</plot>
  <rating>4.5</rating>
</movie>`,
  );
}

export async function cleanupTempDir(dir: string) {
  await rm(dir, { recursive: true, force: true });
}
