import { writeCookie as setCookieRaw, deleteCookie } from "$lib/utils/cookie";

/**
 * Generic factory for cookie-backed list preference objects.
 *
 * Each entity (videos, galleries, performers, etc.) defines its own prefs
 * type and validation logic. The factory provides the shared boilerplate:
 * JSON parse/encode with URI encoding, cookie read/write/clear, and
 * default-comparison helpers. No framework coupling, so the same
 * module runs in SvelteKit load functions (which call `parse` with
 * `cookies.get(...)`) and browser code (which call `writeCookie` /
 * `clearCookie`).
 */

export function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

const ONE_YEAR = 60 * 60 * 24 * 365;

export interface ListPrefsConfig<T> {
  cookieName: string;
  maxAge?: number;
  defaults: () => T;
  validate: (parsed: Record<string, unknown>) => T | null;
}

export interface ListPrefsApi<T> {
  cookieName: string;
  maxAge: number;
  defaults: () => T;
  isDefault: (prefs: T) => boolean;
  parse: (raw: string | undefined) => T | null;
  serialize: (prefs: T) => string;
  writeCookie: (prefs: T) => void;
  clearCookie: () => void;
}

export function createListPrefs<T>(config: ListPrefsConfig<T>): ListPrefsApi<T> {
  const { cookieName, defaults, validate } = config;
  const maxAge = config.maxAge ?? ONE_YEAR;

  function parse(raw: string | undefined): T | null {
    if (raw === undefined || raw === "") return null;
    let decoded: string;
    try {
      decoded = decodeURIComponent(raw);
    } catch {
      return null;
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(decoded) as unknown;
    } catch {
      return null;
    }
    if (!isRecord(parsed)) return null;
    return validate(parsed);
  }

  function serialize(prefs: T): string {
    // JSON.stringify drops `undefined` fields automatically, so the
    // serialized form of `{ ..., activePresetId: undefined }` matches
    // `defaults()` (which omits the key entirely) when compared via
    // isDefault.
    return encodeURIComponent(JSON.stringify(prefs));
  }

  function isDefault(prefs: T): boolean {
    return JSON.stringify(prefs) === JSON.stringify(defaults());
  }

  function writeCookie(prefs: T): void {
    setCookieRaw(cookieName, serialize(prefs), maxAge);
  }

  function clearCookie(): void {
    deleteCookie(cookieName);
  }

  return {
    cookieName,
    maxAge,
    defaults,
    isDefault,
    parse,
    serialize,
    writeCookie,
    clearCookie,
  };
}
