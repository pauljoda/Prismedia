import { readdir } from "node:fs/promises";
import path from "node:path";
import {
  isAudioFile,
  isImageFile,
  isVideoFile,
  supportedZipExtensions,
} from "./file-classification";

export async function discoverVideoFiles(rootPath: string, recursive = true): Promise<string[]> {
  const entries = await readdir(rootPath, { withFileTypes: true });
  const files: string[] = [];

  for (const entry of entries) {
    if (entry.name.startsWith(".")) continue;

    const entryPath = path.join(rootPath, entry.name);

    if (entry.isDirectory()) {
      if (recursive) {
        files.push(...(await discoverVideoFiles(entryPath, recursive)));
      }
      continue;
    }

    if (!entry.isFile() || !isVideoFile(entryPath)) {
      continue;
    }

    files.push(entryPath);
  }

  return files.sort((left, right) => left.localeCompare(right));
}

export interface ImageDiscoveryResult {
  dirs: string[];
  imageFiles: string[];
  zipFiles: string[];
}

export async function discoverImageFilesAndDirs(
  rootPath: string,
  recursive = true,
): Promise<ImageDiscoveryResult> {
  const dirs: Set<string> = new Set();
  const imageFiles: string[] = [];
  const zipFiles: string[] = [];

  async function walk(dirPath: string) {
    const entries = await readdir(dirPath, { withFileTypes: true });

    for (const entry of entries) {
      if (entry.name.startsWith(".")) continue;

      const entryPath = path.join(dirPath, entry.name);

      if (entry.isDirectory()) {
        if (recursive) {
          await walk(entryPath);
        }
        continue;
      }

      if (!entry.isFile()) continue;

      const ext = path.extname(entry.name).toLowerCase();

      if (supportedZipExtensions.has(ext)) {
        zipFiles.push(entryPath);
        continue;
      }

      if (isImageFile(entryPath)) {
        imageFiles.push(entryPath);
        dirs.add(dirPath);
      }
    }
  }

  await walk(rootPath);

  return {
    dirs: [...dirs].sort(),
    imageFiles: imageFiles.sort(),
    zipFiles: zipFiles.sort(),
  };
}

export interface AudioDiscoveryResult {
  dirs: string[];
  audioFiles: string[];
}

export async function discoverAudioFilesAndDirs(
  rootPath: string,
  recursive = true,
): Promise<AudioDiscoveryResult> {
  const dirs: Set<string> = new Set();
  const audioFiles: string[] = [];

  async function walk(dirPath: string) {
    const entries = await readdir(dirPath, { withFileTypes: true });

    for (const entry of entries) {
      if (entry.name.startsWith(".")) continue;

      const entryPath = path.join(dirPath, entry.name);

      if (entry.isDirectory()) {
        if (recursive) {
          await walk(entryPath);
        }
        continue;
      }

      if (!entry.isFile()) continue;

      if (isAudioFile(entryPath)) {
        audioFiles.push(entryPath);
        dirs.add(dirPath);
      }
    }
  }

  await walk(rootPath);

  return {
    dirs: [...dirs].sort(),
    audioFiles: audioFiles.sort(),
  };
}
