import {
  defaultSubtitleAppearance,
  subtitleDisplayStyles,
  type SubtitleAppearance,
  type SubtitleDisplayStyle,
} from "./subtitle-types";

export { defaultSubtitleAppearance, subtitleDisplayStyles };
export type { SubtitleAppearance, SubtitleDisplayStyle };

const LOCAL_STORAGE_KEY = "prismedia:subtitle-appearance";

export function resolveSubtitleAppearance(
  libraryDefaults: Partial<SubtitleAppearance> | null | undefined,
  localOverride: Partial<SubtitleAppearance> | null | undefined,
): SubtitleAppearance {
  return {
    style:
      localOverride?.style ??
      libraryDefaults?.style ??
      defaultSubtitleAppearance.style,
    fontScale: clamp(
      localOverride?.fontScale ??
        libraryDefaults?.fontScale ??
        defaultSubtitleAppearance.fontScale,
      0.5,
      3,
    ),
    positionPercent: clamp(
      localOverride?.positionPercent ??
        libraryDefaults?.positionPercent ??
        defaultSubtitleAppearance.positionPercent,
      0,
      100,
    ),
    opacity: clamp(
      localOverride?.opacity ??
        libraryDefaults?.opacity ??
        defaultSubtitleAppearance.opacity,
      0.2,
      1,
    ),
  };
}

export function readLocalSubtitleAppearance(): Partial<SubtitleAppearance> | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = window.localStorage.getItem(LOCAL_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<SubtitleAppearance>;
    return {
      style: isValidStyle(parsed.style) ? parsed.style : undefined,
      fontScale: Number.isFinite(parsed.fontScale) ? parsed.fontScale : undefined,
      positionPercent: Number.isFinite(parsed.positionPercent)
        ? parsed.positionPercent
        : undefined,
      opacity: Number.isFinite(parsed.opacity) ? parsed.opacity : undefined,
    };
  } catch {
    return null;
  }
}

export function writeLocalSubtitleAppearance(
  override: Partial<SubtitleAppearance> | null,
) {
  if (typeof window === "undefined") return;
  if (!override) {
    window.localStorage.removeItem(LOCAL_STORAGE_KEY);
    return;
  }
  window.localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(override));
}

export function captionClassName(style: SubtitleDisplayStyle): string {
  switch (style) {
    case "classic":
      return "video-caption-classic";
    case "outline":
      return "video-caption-outline";
    case "stylized":
    default:
      return "video-caption-stylized";
  }
}

/** Pick the best track for the user's preferred language list. */
export function pickPreferredSubtitleTrack(
  tracks: { id: string; language: string; label?: string | null; isDefault?: boolean }[],
  preferredLanguages: string,
): string | null {
  if (!tracks.length) return null;
  const prefs = preferredLanguages
    .split(",")
    .map((p) => p.trim().toLowerCase())
    .filter(Boolean);
  if (prefs.length === 0) return tracks[0]!.id;

  for (const pref of prefs) {
    const exact = tracks.find((t) => subtitleTrackTokens(t).some((token) => token === pref));
    if (exact) return exact.id;
    const prefix = tracks.find((t) =>
      subtitleTrackTokens(t).some((token) => token.startsWith(pref) || pref.startsWith(token)),
    );
    if (prefix) return prefix.id;
    const equiv = tracks.find((t) =>
      subtitleTrackTokens(t).some((token) => iso639Equivalent(token, pref)),
    );
    if (equiv) return equiv.id;
  }
  return null;
}

const ISO639_PAIRS: Record<string, string> = {
  en: "eng", eng: "en",
  ja: "jpn", jpn: "ja",
  es: "spa", spa: "es",
  fr: "fra", fra: "fr",
  de: "deu", deu: "de",
  zh: "zho", zho: "zh",
  ko: "kor", kor: "ko",
  pt: "por", por: "pt",
  ru: "rus", rus: "ru",
  it: "ita", ita: "it",
  nl: "nld", nld: "nl",
  ar: "ara", ara: "ar",
  hi: "hin", hin: "hi",
};

const LANGUAGE_NAME_ALIASES: Record<string, string> = {
  english: "en",
  japanese: "ja",
  spanish: "es",
  french: "fr",
  german: "de",
  chinese: "zh",
  korean: "ko",
  portuguese: "pt",
  russian: "ru",
  italian: "it",
  dutch: "nl",
  arabic: "ar",
  hindi: "hi",
};

function subtitleTrackTokens(track: {
  language: string;
  label?: string | null;
}): string[] {
  const rawTokens = [track.language, track.label]
    .filter((value): value is string => Boolean(value?.trim()))
    .flatMap((value) => {
      const normalized = value.trim().toLowerCase();
      return [normalized, normalized.replace(/\s*\([^)]*\)\s*/g, "").trim()];
    })
    .filter(Boolean);

  return Array.from(new Set(rawTokens.flatMap((token) => [token, LANGUAGE_NAME_ALIASES[token]].filter(Boolean) as string[])));
}

function iso639Equivalent(a: string, b: string): boolean {
  return ISO639_PAIRS[a] === b || ISO639_PAIRS[b] === a;
}

function clamp(n: number, min: number, max: number): number {
  if (!Number.isFinite(n)) return min;
  return Math.min(max, Math.max(min, n));
}

function isValidStyle(value: unknown): value is SubtitleDisplayStyle {
  return (
    typeof value === "string" &&
    (subtitleDisplayStyles as readonly string[]).includes(value)
  );
}
