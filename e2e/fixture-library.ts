import { expect, type APIRequestContext } from "@playwright/test";
import { access, readdir } from "node:fs/promises";
import path from "node:path";

const apiBase = process.env.PRISMEDIA_E2E_API_URL ?? "http://127.0.0.1:8008/api";
const configuredFixtureRoot = process.env.PRISMEDIA_E2E_LIBRARY_ROOT?.trim() || null;
const repoFixtureRoot = path.resolve(process.cwd(), "tests/fixtures/media/videos");

export interface LibraryRoot {
  id: string;
  path: string;
}

interface LibraryRootsResponse {
  roots: Array<
    LibraryRoot & {
      enabled?: boolean;
      scanVideos?: boolean;
    }
  >;
}

async function pathExists(targetPath: string): Promise<boolean> {
  try {
    await access(targetPath);
    return true;
  } catch {
    return false;
  }
}

async function hasVideoFile(targetPath: string): Promise<boolean> {
  const supportedExtensions = new Set([".mp4", ".mkv", ".mov", ".webm", ".avi"]);
  const pending = [targetPath];

  while (pending.length > 0) {
    const current = pending.pop()!;
    let entries;
    try {
      entries = await readdir(current, { withFileTypes: true });
    } catch {
      continue;
    }

    for (const entry of entries) {
      const child = path.join(current, entry.name);
      if (entry.isDirectory()) {
        pending.push(child);
      } else if (entry.isFile() && supportedExtensions.has(path.extname(entry.name).toLowerCase())) {
        return true;
      }
    }
  }

  return false;
}

async function listVideoRoots(request: APIRequestContext): Promise<LibraryRoot[]> {
  const response = await request.get(`${apiBase}/libraries?scanVideos=true&enabled=true`);
  expect(response.ok()).toBeTruthy();

  const body = (await response.json()) as LibraryRootsResponse;
  return body.roots.map((root) => ({ id: root.id, path: root.path }));
}

async function createLibraryRoot(request: APIRequestContext, fixtureRoot: string): Promise<LibraryRoot> {
  const created = await request.post(`${apiBase}/libraries`, {
    data: {
      path: fixtureRoot,
      label: "Fixture Videos",
      enabled: true,
      recursive: true,
      scanVideos: true,
      scanImages: false,
      scanAudio: false,
    },
  });

  expect(created.ok()).toBeTruthy();
  return (await created.json()) as LibraryRoot;
}

export async function ensureFixtureLibraryRoot(request: APIRequestContext): Promise<LibraryRoot> {
  const existingRoots = await listVideoRoots(request);

  if (configuredFixtureRoot) {
    const configuredMatch = existingRoots.find((root) => root.path === configuredFixtureRoot);
    if (configuredMatch) {
      return configuredMatch;
    }

    if (!(await pathExists(configuredFixtureRoot))) {
      throw new Error(
        `PRISMEDIA_E2E_LIBRARY_ROOT does not exist: ${configuredFixtureRoot}. ` +
          "Point it at a readable local video fixture directory.",
      );
    }

    return createLibraryRoot(request, configuredFixtureRoot);
  }

  const repoMatch = existingRoots.find((root) => root.path === repoFixtureRoot);
  if (repoMatch) {
    return repoMatch;
  }

  if (await pathExists(repoFixtureRoot) && await hasVideoFile(repoFixtureRoot)) {
    return createLibraryRoot(request, repoFixtureRoot);
  }

  const reusableRoot = existingRoots[0];
  if (reusableRoot) {
    return reusableRoot;
  }

  throw new Error(
    "No enabled video library root is available for E2E tests. " +
      "Set PRISMEDIA_E2E_LIBRARY_ROOT to a readable local fixture directory.",
  );
}
