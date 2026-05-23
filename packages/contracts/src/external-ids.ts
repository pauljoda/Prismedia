/**
 * Flexible provider → string map used on every video entity (series,
 * seasons, episodes, movies) and every scrape result. Values are always
 * strings. A value may be a bare ID, a full URL, or a composite — the
 * plugin that wrote it decides the format.
 */
export type ExternalIds = Record<string, string>;

/**
 * Conventional provider keys. These are used for display labels, icons,
 * and plugin ergonomics. They are NOT enforced at the database layer —
 * a plugin may write any key it wants, including a `custom:<pluginId>`
 * namespace for proprietary providers.
 */
export const KnownExternalIdProviders = {
  tmdb: "tmdb",
  tvdb: "tvdb",
  imdb: "imdb",
  anidb: "anidb",
  mal: "mal",
  trakt: "trakt",
} as const;

export type KnownExternalIdProvider =
  (typeof KnownExternalIdProviders)[keyof typeof KnownExternalIdProviders];

export interface ExternalIdProviderDescriptor {
  key: string;
  label: string;
  linkTemplate?: (value: string) => string;
}

export const EXTERNAL_ID_PROVIDER_DESCRIPTORS: Record<
  KnownExternalIdProvider,
  ExternalIdProviderDescriptor
> = {
  // TMDB and TVDB URLs require entity-type context (movie vs tv), which
  // the descriptor layer doesn't know. Consumers build these links with
  // full context; no linkTemplate here on purpose.
  tmdb: {
    key: "tmdb",
    label: "The Movie Database",
  },
  tvdb: {
    key: "tvdb",
    label: "TheTVDB",
  },
  imdb: {
    key: "imdb",
    label: "IMDb",
    linkTemplate: (value) => `https://www.imdb.com/title/${value}/`,
  },
  anidb: {
    key: "anidb",
    label: "AniDB",
    linkTemplate: (value) =>
      `https://anidb.net/anime/?aid=${encodeURIComponent(value)}`,
  },
  mal: {
    key: "mal",
    label: "MyAnimeList",
    linkTemplate: (value) => `https://myanimelist.net/anime/${value}`,
  },
  trakt: {
    key: "trakt",
    label: "Trakt",
    linkTemplate: (value) => `https://trakt.tv/${value}`,
  },
};
