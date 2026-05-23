import { env } from "$env/dynamic/public";

export const API_BASE = env.PUBLIC_API_URL || "/api";

export function apiPath(path: string): string {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  return `${API_BASE}${normalizedPath.startsWith("/api/") ? normalizedPath.slice(4) : normalizedPath}`;
}

export function jellyfinApiPath(path: string): string {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  const rootBase = API_BASE.endsWith("/api")
    ? API_BASE.slice(0, -4)
    : API_BASE === "/api"
      ? ""
      : API_BASE;
  return `${rootBase}${normalizedPath}`;
}

export function assetUrl(assetPath: string | null | undefined): string {
  if (!assetPath) return "";
  const normalized = assetPath.startsWith("/") ? assetPath : `/${assetPath}`;
  return normalized;
}

export function apiAssetUrl(assetPath: string | null | undefined, cacheBust?: string): string | undefined {
  if (!assetPath) return undefined;

  if (assetPath.startsWith("http://") || assetPath.startsWith("https://")) {
    return cacheBust ? `${assetPath}?v=${encodeURIComponent(cacheBust)}` : assetPath;
  }

  const normalized = assetPath.startsWith("/") ? assetPath : `/${assetPath}`;
  const url = `${API_BASE}${normalized}`;
  return cacheBust ? `${url}?v=${encodeURIComponent(cacheBust)}` : url;
}

export async function fetchApi<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);

  if (init?.body && !headers.has("Content-Type") && !(init.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(apiPath(path), {
    ...init,
    headers,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `API ${response.status}: ${response.statusText}`);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export async function uploadFile<T>(
  path: string,
  file: File,
  extraFields?: Record<string, string>,
): Promise<T> {
  const form = new FormData();
  if (extraFields) {
    for (const [key, value] of Object.entries(extraFields)) {
      form.append(key, value);
    }
  }
  form.append("file", file);

  return fetchApi<T>(path, {
    method: "POST",
    body: form,
  });
}

export async function orvalFetch<TData>(
  url: string,
  init?: RequestInit,
): Promise<TData> {
  const path = url.startsWith("/api/") ? url.slice(4) : url;
  const headers = new Headers(init?.headers);

  if (init?.body && !headers.has("Content-Type") && !(init.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(apiPath(path), {
    ...init,
    headers,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `API ${response.status}: ${response.statusText}`);
  }

  const text = await response.text();
  const contentType = response.headers.get("content-type") ?? "";
  const data =
    text.length === 0
      ? undefined
      : contentType.includes("application/json")
        ? JSON.parse(text)
        : text;

  return {
    data,
    status: response.status,
    headers: response.headers,
  } as TData;
}
