import { env } from "$env/dynamic/public";
import { problemMessage } from "$lib/api/generated-response";

export const API_BASE = env.PUBLIC_API_URL || "/api";

/** API failure carrying the HTTP status so callers can branch without message matching. */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    /** Stable generated problem code when the API returned Prismedia's typed problem shape. */
    public readonly problemCode?: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/**
 * Request options accepted by the fetch layer. `on401` controls whether an
 * unauthenticated response performs the global redirect to the login page
 * ("redirect", default) or is surfaced to the caller ("ignore" — used by the auth
 * bootstrap and login flows themselves).
 */
export type AppRequestInit = RequestInit & { on401?: "redirect" | "ignore" };

/**
 * Options for auth bootstrap calls that must not trigger the global login redirect.
 * Typed as RequestInit so it slots into the generated client's options parameter.
 */
export const IGNORE_401 = { on401: "ignore" } satisfies AppRequestInit as RequestInit;

let unauthorizedHandled = false;

/**
 * Exactly-once global 401 handling: hard-navigate to the login page, preserving the
 * current location. A full reload (rather than goto) resets all module state, which is
 * what makes the exactly-once flag safe.
 */
function handleUnauthorized(): void {
  if (unauthorizedHandled || typeof window === "undefined") return;
  const path = window.location.pathname;
  if (path === "/login" || path.startsWith("/setup")) return;
  unauthorizedHandled = true;
  const returnTo = encodeURIComponent(window.location.pathname + window.location.search);
  window.location.replace(`/login?returnTo=${returnTo}&expired=1`);
}

function splitInit(init?: AppRequestInit): { on401: "redirect" | "ignore"; rest: RequestInit | undefined } {
  if (!init) return { on401: "redirect", rest: undefined };
  const { on401, ...rest } = init;
  return { on401: on401 ?? "redirect", rest };
}

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

export async function fetchApi<T>(path: string, init?: AppRequestInit): Promise<T> {
  const { on401, rest } = splitInit(init);
  const headers = new Headers(rest?.headers);

  if (rest?.body && !headers.has("Content-Type") && !(rest.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(apiPath(path), {
    ...rest,
    credentials: rest?.credentials ?? "same-origin",
    headers,
  });

  if (!response.ok) {
    if (response.status === 401 && on401 === "redirect") {
      handleUnauthorized();
    }

    const problem = await responseError(response);
    throw new ApiError(problem.message, response.status, problem.code);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export async function uploadFile<T>(
  path: string,
  file: File,
  extraFields?: Record<string, string>,
  init?: RequestInit,
): Promise<T> {
  const form = new FormData();
  if (extraFields) {
    for (const [key, value] of Object.entries(extraFields)) {
      form.append(key, value);
    }
  }
  form.append("file", file);

  return fetchApi<T>(path, {
    ...init,
    method: "POST",
    body: form,
  });
}

export async function orvalFetch<TData>(
  url: string,
  init?: AppRequestInit,
): Promise<TData> {
  const path = url.startsWith("/api/") ? url.slice(4) : url;
  const { on401, rest } = splitInit(init);
  const headers = new Headers(rest?.headers);

  if (rest?.body && !headers.has("Content-Type") && !(rest.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(apiPath(path), {
    ...rest,
    credentials: rest?.credentials ?? "same-origin",
    headers,
  });

  if (!response.ok) {
    if (response.status === 401 && on401 === "redirect") {
      handleUnauthorized();
    }

    const problem = await responseError(response);
    throw new ApiError(problem.message, response.status, problem.code);
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

async function responseError(response: Response): Promise<{ message: string; code?: string }> {
  const text = await response.text();
  if (!text) {
    return { message: `API ${response.status}: ${response.statusText}` };
  }

  try {
    const data: unknown = JSON.parse(text);
    const code = problemCode(data);
    return { message: problemMessage(data) ?? text, ...(code ? { code } : {}) };
  } catch {
    return { message: problemMessage(text) ?? `API ${response.status}: ${response.statusText}` };
  }
}

function problemCode(data: unknown): string | undefined {
  if (!data || typeof data !== "object") return undefined;
  const value = (data as Record<string, unknown>).code;
  return typeof value === "string" && value.trim() ? value : undefined;
}
