import type { EntityCapability } from "$lib/api/generated/model";
import type { EntityDetailCardFull } from "./entity-detail";
import { entityCardToDetailCard } from "./entity-detail";

export interface EntityDetailLabRow {
  kind: string;
  label: string;
  cards: EntityDetailCardFull[];
}

type ArtShape = "wide" | "video" | "square" | "portrait" | "poster";

function artDimensions(shape: ArtShape): { width: number; height: number } {
  switch (shape) {
    case "poster":
      return { width: 720, height: 1080 };
    case "portrait":
      return { width: 810, height: 1080 };
    case "square":
      return { width: 900, height: 900 };
    case "wide":
      return { width: 1260, height: 540 };
    case "video":
    default:
      return { width: 960, height: 540 };
  }
}

function svgArt(label: string, primary: string, secondary: string, accent: string, shape: ArtShape): string {
  const { width, height } = artDimensions(shape);
  const safeLabel = label.replace(/[<>&"]/g, "");
  const textWidth = Math.min(width - 96, 420);
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}"><defs><linearGradient id="g" x1="0" x2="1" y1="0" y2="1"><stop stop-color="${primary}"/><stop offset="1" stop-color="${secondary}"/></linearGradient><filter id="grain"><feTurbulence baseFrequency=".8" numOctaves="2" stitchTiles="stitch"/><feColorMatrix type="saturate" values="0"/></filter></defs><rect width="${width}" height="${height}" fill="url(#g)"/><rect width="${width}" height="${height}" opacity=".13" filter="url(#grain)"/><path d="M${width * 0.07} ${height * 0.82} C${width * 0.22} ${height * 0.55} ${width * 0.3} ${height * 0.66} ${width * 0.43} ${height * 0.4} S${width * 0.7} ${height * 0.22} ${width * 0.93} ${height * 0.48}" fill="none" stroke="${accent}" stroke-width="${Math.max(16, width * 0.018)}" opacity=".72"/><circle cx="${width * 0.78}" cy="${height * 0.25}" r="${Math.min(width, height) * 0.14}" fill="${accent}" opacity=".34"/><rect x="48" y="48" width="${textWidth}" height="70" fill="#050505" opacity=".5"/><text x="72" y="96" fill="#f4efe6" font-family="Inter,Arial,sans-serif" font-size="36" font-weight="700">${safeLabel}</text></svg>`;
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
}

const brass = "#c49a5a";
const forest = "#293f32";
const burgundy = "#522b34";
const indigo = "#26344f";
const ember = "#7b4a24";
const graphite = "#1f2226";

function rating(value: number): EntityCapability {
  return { kind: "rating", value };
}

function flags(options: { isNsfw?: boolean; isFavorite?: boolean; isOrganized?: boolean } = {}): EntityCapability {
  return {
    kind: "flags",
    isFavorite: options.isFavorite ?? null,
    isNsfw: options.isNsfw ?? null,
    isOrganized: options.isOrganized ?? null,
  };
}

function images(
  coverSrc: string,
  posterSrc?: string,
): EntityCapability {
  return {
    kind: "images",
    supportedKinds: ["cover", "poster", "thumbnail"],
    thumbnailUrl: posterSrc ?? coverSrc,
    coverUrl: coverSrc,
    items: [
      { kind: "cover", path: coverSrc, mimeType: "image/svg+xml" },
      ...(posterSrc ? [{ kind: "poster", path: posterSrc, mimeType: "image/svg+xml" }] : []),
    ],
  };
}

function description(value: string): EntityCapability {
  return { kind: "description", value };
}

function tags(_values: string[]): EntityCapability {
  return { kind: "flags", isFavorite: false, isNsfw: false, isOrganized: true };
}

function studio(_id: string, _title: string): EntityCapability {
  return { kind: "flags", isFavorite: false, isNsfw: false, isOrganized: true };
}

function credits(_people: Array<{ id: string; title: string }>): EntityCapability {
  return { kind: "flags", isFavorite: false, isNsfw: false, isOrganized: true };
}

function stats(items: Array<{ code: string; value: number }>): EntityCapability {
  return { kind: "stats", items };
}


function technical(options: {
  duration?: string;
  width?: number;
  height?: number;
  frameRate?: number;
  bitRate?: number;
  sampleRate?: number;
  channels?: number;
  codec?: string;
  container?: string;
  format?: string;
}): EntityCapability {
  return {
    kind: "technical",
    duration: options.duration ?? null,
    width: options.width ?? null,
    height: options.height ?? null,
    frameRate: options.frameRate ?? null,
    bitRate: options.bitRate ?? null,
    sampleRate: options.sampleRate ?? null,
    channels: options.channels ?? null,
    codec: options.codec ?? null,
    container: options.container ?? null,
    format: options.format ?? null,
  };
}

function positions(items: Array<{ code: string; value: number; label: string }>): EntityCapability {
  return { kind: "position", items };
}

function dates(items: Array<{ code: string; value: string; sortableValue?: string; precision?: string }>): EntityCapability {
  return {
    kind: "dates",
    items: items.map((item) => ({
      code: item.code,
      value: item.value,
      sortableValue: item.sortableValue ?? null,
      precision: item.precision ?? null,
    })),
  };
}

function links(
  urls: Array<{ url: string; label?: string }>,
  externalIds: Array<{ provider: string; value: string; url?: string }> = [],
): EntityCapability {
  return {
    kind: "links",
    urls: urls.map((u) => ({ value: u.url, label: u.label ?? null })),
    externalIds: externalIds.map((e) => ({ provider: e.provider, value: e.value, url: e.url ?? null })),
  };
}

function files(items: Array<{ role: string; path: string; mimeType?: string }>): EntityCapability {
  return {
    kind: "files",
    items: items.map((f) => ({ role: f.role, path: f.path, mimeType: f.mimeType ?? null })),
  };
}

function fingerprints(items: Array<{ algorithm: string; value: string }>): EntityCapability {
  return { kind: "fingerprints", items };
}

function markers(items: Array<{ id: string; title: string; seconds: number; endSeconds?: number }>): EntityCapability {
  return {
    kind: "markers",
    items: items.map((m) => ({
      id: m.id,
      title: m.title,
      seconds: m.seconds,
      endSeconds: m.endSeconds ?? null,
    })),
  };
}

function subtitles(items: Array<{ id: string; language: string; label?: string; format: string; source: string; isDefault?: boolean }>): EntityCapability {
  return {
    kind: "subtitles",
    items: items.map((s) => ({
      id: s.id,
      language: s.language,
      label: s.label ?? null,
      format: s.format,
      source: s.source,
      storagePath: `/data/subtitles/${s.id}.${s.format}`,
      sourceFormat: s.format,
      sourcePath: null,
      isDefault: s.isDefault ?? false,
    })),
  };
}

function progress(options: { index: number; total: number; unit: string; mode?: string; completed?: boolean }): EntityCapability {
  return {
    kind: "progress",
    currentEntityId: null,
    unit: options.unit,
    index: options.index,
    total: options.total,
    mode: options.mode ?? null,
    completedAt: options.completed ? "2026-05-01T12:00:00Z" : null,
    updatedAt: "2026-05-10T18:30:00Z",
  };
}

function classification(value: string, system?: string): EntityCapability {
  return { kind: "classification", value, system: system ?? null };
}

function source(items: Array<{ code: string; value: string }>): EntityCapability {
  return { kind: "source", items };
}

function detailCard(options: {
  id: string;
  kind: string;
  title: string;
  capabilities: EntityCapability[];
}): EntityDetailCardFull {
  return entityCardToDetailCard({
    id: options.id,
    kind: options.kind,
    title: options.title,
    parentEntityId: null,
    sortOrder: null,
    capabilities: options.capabilities,
    childrenByKind: [],
    relationships: [],
  });
}

/**
 * Base fixture card — universal capabilities only.
 * These 7 capabilities are present on every entity kind and form the
 * shared foundation that EntityDetail must handle before any kind-specific
 * customization is layered on.
 */
export const baseDetailCard: EntityDetailCardFull = detailCard({
  id: "base-universal",
  kind: "video",
  title: "Big Buck Bunny",
  capabilities: [
    images(
      svgArt("Big Buck Bunny", forest, indigo, brass, "wide"),
      svgArt("BBB Poster", forest, graphite, brass, "poster"),
    ),
    description(
      "A large and lovable rabbit deals with three tiny bullies, led by a flying squirrel, who are determined to ruin his perfect day.\n\nThis is a **test fixture** exercising the universal capability set — the sections that appear on *every* entity kind regardless of type.\n\n> The shared foundation before kind-specific customization.",
    ),
    rating(3),
    flags({ isFavorite: true, isNsfw: false, isOrganized: false }),
    tags(["animation", "comedy", "short-film", "open-source", "blender"]),
    links(
      [{ url: "https://peach.blender.org/", label: "Official Site" }],
      [{ provider: "stashdb", value: "abc-123-def", url: "https://stashdb.org/scenes/abc-123" }],
    ),
    files([
      { role: "source", path: "/media/videos/big-buck-bunny.mp4", mimeType: "video/mp4" },
    ]),
  ],
});

export const detailLabRows: EntityDetailLabRow[] = [
  {
    kind: "video",
    label: "Videos",
    cards: [
      (() => {
        const card = detailCard({
          id: "video-full",
          kind: "video",
          title: "Pete the Cat",
          capabilities: [
            images(
              "/fixtures/banner-petethecat.jpg",
              "/fixtures/poster-petethecat.jpg",
            ),
            description(
              "Pete the Cat is a groovy blue cat who never lets anything get him down. With his cool attitude and love of music, Pete faces everyday challenges with a positive outlook.\n\nBased on the **bestselling book series** by James Dean and Kimberly Dean:\n\n- Musical adventures with Pete and his friends\n- Life lessons about staying positive\n- *Far-out* groovy vibes in every episode\n\n> \"It's all good.\"\n\nA Prime Original animated series.",
            ),
            rating(4),
            flags({ isFavorite: true, isNsfw: true, isOrganized: true }),
            tags(["animation", "comedy", "short-film", "open-source", "blender"]),
            studio("studio-blender", "Blender Foundation"),
            credits([
              { id: "person-sacha", title: "Sacha Goedegebure" },
              { id: "person-nathan", title: "Nathan Vegdahl" },
              { id: "person-jan", title: "Jan Morgenstern" },
              { id: "person-emma", title: "Emma Silverton" },
              { id: "person-kira", title: "Kira Vasquez" },
            ]),
            stats([
              { code: "views", value: 1842 },
              { code: "play-count", value: 47 },
            ]),
            technical({
              duration: "00:09:56.40",
              width: 1920,
              height: 1080,
              frameRate: 24,
              bitRate: 8_500_000,
              codec: "h264",
              container: "mp4",
            }),
            positions([
              { code: "season", value: 1, label: "Season 1" },
              { code: "episode", value: 2, label: "Episode 2" },
            ]),
            dates([
              { code: "release", value: "2008-05-30", sortableValue: "2008-05-30" },
              { code: "added", value: "2026-01-15", sortableValue: "2026-01-15" },
            ]),
            links(
              [{ url: "https://peach.blender.org/", label: "Official Site" }],
              [{ provider: "stashdb", value: "abc-123-def", url: "https://stashdb.org/scenes/abc-123" }],
            ),
            files([
              { role: "source", path: "/media/videos/big-buck-bunny-dc.mp4", mimeType: "video/mp4" },
            ]),
            fingerprints([
              { algorithm: "oshash", value: "a1b2c3d4e5f60718" },
              { algorithm: "phash", value: "9876543210abcdef" },
              { algorithm: "md5", value: "d41d8cd98f00b204e9800998ecf8427e" },
            ]),
            markers([
              { id: "m-1", title: "Intro", seconds: 0, endSeconds: 45 },
              { id: "m-2", title: "Butterfly Scene", seconds: 120, endSeconds: 195 },
              { id: "m-3", title: "Revenge Montage", seconds: 340, endSeconds: 480 },
              { id: "m-4", title: "Credits", seconds: 550 },
            ]),
            subtitles([
              { id: "sub-en", language: "English", label: "English (CC)", format: "srt", source: "embedded", isDefault: true },
              { id: "sub-de", language: "German", format: "ass", source: "external" },
              { id: "sub-ja", language: "Japanese", format: "srt", source: "external" },
            ]),
            progress({ index: 340, total: 596, unit: "seconds", mode: "playing" }),
            classification("animation", "content-type"),
            source([{ code: "stash-compat", value: "scene-42" }]),
          ],
        });
        const creditThumbs: Record<string, string> = {
          "person-sacha": svgArt("SG", burgundy, indigo, brass, "portrait"),
          "person-nathan": svgArt("NV", indigo, forest, brass, "portrait"),
          "person-jan": svgArt("JM", ember, graphite, brass, "portrait"),
          "person-emma": svgArt("ES", forest, burgundy, brass, "portrait"),
          "person-kira": svgArt("KV", graphite, ember, brass, "portrait"),
        };
        card.studio = {
          id: "studio-blender",
          kind: "studio",
          title: "Blender Foundation",
          thumbnail: svgArt("BF", forest, brass, graphite, "square"),
        };
        card.credits = [
          { id: "person-sacha", kind: "person", title: "Sacha Goedegebure", thumbnail: creditThumbs["person-sacha"] ?? null },
          { id: "person-nathan", kind: "person", title: "Nathan Vegdahl", thumbnail: creditThumbs["person-nathan"] ?? null },
          { id: "person-jan", kind: "person", title: "Jan Morgenstern", thumbnail: creditThumbs["person-jan"] ?? null },
          { id: "person-emma", kind: "person", title: "Emma Silverton", thumbnail: creditThumbs["person-emma"] ?? null },
          { id: "person-kira", kind: "person", title: "Kira Vasquez", thumbnail: creditThumbs["person-kira"] ?? null },
        ];
        return card;
      })(),
    ],
  },
  {
    kind: "video-series",
    label: "Video Series",
    cards: [
      detailCard({
        id: "series-demo",
        kind: "video-series",
        title: "Demo Shorts Collection",
        capabilities: [
          images(
            svgArt("Demo Shorts", burgundy, graphite, brass, "wide"),
            svgArt("Demo Poster", burgundy, ember, brass, "poster"),
          ),
          description(
            "A curated collection of short animation demos from various open-source projects. Includes Blender Foundation shorts, procedural animation experiments, and community showcase reels.",
          ),
          rating(5),
          flags({ isFavorite: true, isOrganized: true }),
          tags(["animation", "demo", "showcase", "blender"]),
          studio("studio-blender", "Blender Foundation"),
          stats([
            { code: "videos", value: 12 },
            { code: "seasons", value: 2 },
          ]),
          dates([
            { code: "release", value: "2024-03-15", sortableValue: "2024-03-15" },
            { code: "added", value: "2026-02-01", sortableValue: "2026-02-01" },
          ]),
          progress({ index: 8, total: 12, unit: "episodes", mode: "watching" }),
          classification("series", "content-type"),
        ],
      }),
    ],
  },
  {
    kind: "gallery",
    label: "Galleries",
    cards: [
      detailCard({
        id: "gallery-landscapes",
        kind: "gallery",
        title: "Fixture Landscapes — Alpine Series",
        capabilities: [
          images(
            svgArt("Alpine Gallery", ember, forest, brass, "wide"),
            svgArt("Alpine Thumb", ember, graphite, brass, "square"),
          ),
          description(
            "High-resolution landscape photography from the Swiss Alps and Austrian Tyrol. Shot across three seasons, this collection captures the dramatic light and weather patterns of the central European alpine range.",
          ),
          rating(4),
          flags({ isFavorite: false, isOrganized: true }),
          tags(["landscape", "alps", "photography", "nature", "hdr"]),
          stats([{ code: "images", value: 42 }]),
          dates([
            { code: "captured", value: "2025-06 to 2026-02", precision: "month" },
            { code: "added", value: "2026-03-10", sortableValue: "2026-03-10" },
          ]),
          classification("photography", "content-type"),
        ],
      }),
    ],
  },
  {
    kind: "person",
    label: "People",
    cards: [
      detailCard({
        id: "person-sample",
        kind: "person",
        title: "Sacha Goedegebure",
        capabilities: [
          images(
            svgArt("Sacha G", burgundy, indigo, brass, "portrait"),
          ),
          description(
            "Dutch animator and director known for work on Blender Foundation open-movie projects. Lead animator on Big Buck Bunny and contributor to Sintel.",
          ),
          flags({ isFavorite: true }),
          tags(["animator", "director", "blender"]),
          stats([
            { code: "credits", value: 18 },
            { code: "videos", value: 12 },
          ]),
          dates([
            { code: "added", value: "2026-01-15", sortableValue: "2026-01-15" },
          ]),
          links(
            [{ url: "https://example.com/sacha", label: "Portfolio" }],
            [{ provider: "stashdb", value: "performer-abc" }],
          ),
          source([{ code: "stash-compat", value: "performer-42" }]),
        ],
      }),
    ],
  },
  {
    kind: "book",
    label: "Books",
    cards: [
      detailCard({
        id: "book-reader",
        kind: "book",
        title: "Public Domain Reader — Volume I",
        capabilities: [
          images(
            svgArt("PD Reader", burgundy, ember, brass, "wide"),
            svgArt("PD Cover", burgundy, graphite, brass, "poster"),
          ),
          description(
            "A curated anthology of public domain literature, including works by Poe, Shelley, and Lovecraft. Newly typeset with restored illustrations from original printings.",
          ),
          rating(3),
          flags({ isOrganized: true }),
          tags(["literature", "horror", "classic", "anthology"]),
          stats([
            { code: "pages", value: 342 },
            { code: "chapters", value: 14 },
            { code: "volumes", value: 3 },
          ]),
          dates([
            { code: "published", value: "2025-11-01", sortableValue: "2025-11-01" },
            { code: "added", value: "2026-04-20", sortableValue: "2026-04-20" },
          ]),
          progress({ index: 128, total: 342, unit: "pages", mode: "reading" }),
          classification("anthology", "content-type"),
        ],
      }),
    ],
  },
  {
    kind: "audio-library",
    label: "Audio Libraries",
    cards: [
      detailCard({
        id: "audio-album",
        kind: "audio-library",
        title: "Royalty Free Ambient — Field Recordings",
        capabilities: [
          images(
            svgArt("Ambient Album", graphite, indigo, brass, "square"),
          ),
          description(
            "A collection of field recordings from forests, coastlines, and urban environments. Captured in binaural stereo for spatial audio experiences.",
          ),
          rating(4),
          flags({ isOrganized: true }),
          tags(["ambient", "field-recording", "binaural", "nature"]),
          stats([{ code: "tracks", value: 18 }]),
          technical({
            sampleRate: 48000,
            channels: 2,
            codec: "flac",
            format: "flac",
          }),
          dates([
            { code: "recorded", value: "2025-08 to 2025-12", precision: "month" },
            { code: "added", value: "2026-03-01", sortableValue: "2026-03-01" },
          ]),
          progress({ index: 12, total: 18, unit: "tracks", mode: "listening" }),
        ],
      }),
    ],
  },
  {
    kind: "studio",
    label: "Studios",
    cards: [
      detailCard({
        id: "studio-blender",
        kind: "studio",
        title: "Blender Foundation",
        capabilities: [
          images(
            svgArt("Blender Studio", graphite, forest, brass, "wide"),
          ),
          description(
            "The Blender Foundation is a Dutch public benefit organization for public support of the Blender 3D creation suite. Known for producing open-source animated films.",
          ),
          flags({ isFavorite: true }),
          stats([
            { code: "items", value: 64 },
            { code: "videos", value: 48 },
            { code: "series", value: 6 },
          ]),
          links(
            [
              { url: "https://www.blender.org/", label: "Official Website" },
              { url: "https://studio.blender.org/", label: "Blender Studio" },
            ],
            [{ provider: "stashdb", value: "studio-blender-001" }],
          ),
          dates([
            { code: "added", value: "2026-01-01", sortableValue: "2026-01-01" },
          ]),
        ],
      }),
    ],
  },
  {
    kind: "collection",
    label: "Collections",
    cards: [
      detailCard({
        id: "collection-favorites",
        kind: "collection",
        title: "Editor's Picks — Spring 2026",
        capabilities: [
          images(
            svgArt("Editors Picks", forest, ember, brass, "wide"),
          ),
          description(
            "A hand-picked selection of the best content added this spring. Includes standout videos, galleries, and albums from across the library.",
          ),
          rating(5),
          flags({ isFavorite: true }),
          tags(["curated", "highlights", "mixed-media"]),
          stats([
            { code: "items", value: 24 },
            { code: "videos", value: 15 },
            { code: "galleries", value: 6 },
            { code: "albums", value: 3 },
          ]),
          dates([
            { code: "created", value: "2026-04-01", sortableValue: "2026-04-01" },
            { code: "updated", value: "2026-05-10", sortableValue: "2026-05-10" },
          ]),
        ],
      }),
    ],
  },
];
