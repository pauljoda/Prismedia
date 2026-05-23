#!/usr/bin/env node
import postgres from "postgres";

const DEFAULT_DATABASE_URL = "postgres://prismedia:prismedia@localhost:5432/prismedia";
const DEFAULT_COUNT = 180;
const ROOT_PATH = "/tmp/prismedia-scroll-fixtures";

function parseCount(argv) {
  const raw = argv.find((arg) => arg.startsWith("--count="))?.slice("--count=".length);
  if (!raw) return DEFAULT_COUNT;
  const count = Number(raw);
  if (!Number.isInteger(count) || count < 1 || count > 10000) {
    throw new Error("--count must be an integer between 1 and 10000");
  }
  return count;
}

const count = parseCount(process.argv.slice(2));
const databaseUrl = process.env.DATABASE_URL ?? DEFAULT_DATABASE_URL;
const sql = postgres(databaseUrl);

try {
  const [root] = await sql`
    INSERT INTO library_roots (
      path,
      label,
      enabled,
      recursive,
      scan_videos,
      scan_images,
      scan_audio,
      is_nsfw,
      updated_at
    )
    VALUES (
      ${ROOT_PATH},
      'Scroll Fixture Library',
      true,
      false,
      true,
      false,
      false,
      false,
      now()
    )
    ON CONFLICT (path) DO UPDATE
      SET label = EXCLUDED.label,
          enabled = true,
          scan_videos = true,
          is_nsfw = false,
          updated_at = now()
    RETURNING id
  `;

  await sql`
    INSERT INTO video_movies (
      library_root_id,
      title,
      sort_title,
      overview,
      release_date,
      runtime,
      rating,
      is_nsfw,
      organized,
      file_path,
      file_size,
      duration,
      width,
      height,
      frame_rate,
      bit_rate,
      codec,
      container,
      created_at,
      updated_at
    )
    SELECT
      ${root.id},
      format('Scroll Fixture Video %s', lpad(gs::text, 3, '0')),
      format('Scroll Fixture Video %s', lpad(gs::text, 3, '0')),
      'Synthetic local row for infinite-scroll verification.',
      '2026-04-25',
      3300 + gs,
      ((gs - 1) % 5) + 1,
      false,
      false,
      format('/tmp/prismedia-scroll-fixtures/scroll-fixture-%s.mp4', lpad(gs::text, 3, '0')),
      700000000 + (gs * 1000000),
      3300 + gs,
      1280,
      720,
      23.976,
      2500000,
      'h264',
      'mp4',
      now() - ((${count} - gs) * interval '1 second'),
      now() - ((${count} - gs) * interval '1 second')
    FROM generate_series(1, ${count}) AS gs
    ON CONFLICT (file_path) DO UPDATE
      SET title = EXCLUDED.title,
          sort_title = EXCLUDED.sort_title,
          overview = EXCLUDED.overview,
          release_date = EXCLUDED.release_date,
          runtime = EXCLUDED.runtime,
          rating = EXCLUDED.rating,
          file_size = EXCLUDED.file_size,
          duration = EXCLUDED.duration,
          width = EXCLUDED.width,
          height = EXCLUDED.height,
          frame_rate = EXCLUDED.frame_rate,
          bit_rate = EXCLUDED.bit_rate,
          codec = EXCLUDED.codec,
          container = EXCLUDED.container,
          is_nsfw = false,
          updated_at = EXCLUDED.updated_at
  `;

  await sql`
    INSERT INTO ui_prefs (key, value, updated_at)
    VALUES (
      'videos:listPrefs',
      '{"viewMode":"grid","sortBy":"recent","sortDir":"desc","search":"","activeFilters":[]}'::jsonb,
      now()
    )
    ON CONFLICT (key) DO UPDATE
      SET value = EXCLUDED.value,
          updated_at = now()
  `;

  const [summary] = await sql`
    SELECT
      (SELECT count(*)::int FROM video_movies WHERE file_path LIKE ${`${ROOT_PATH}/%`}) AS scroll_fixture_movies,
      (SELECT count(*)::int FROM video_movies) AS total_movies,
      (SELECT count(*)::int FROM video_episodes) AS total_episodes
  `;

  console.log(
    `Seeded ${summary.scroll_fixture_movies} scroll fixture movies (${summary.total_movies} movies, ${summary.total_episodes} episodes total).`,
  );
} finally {
  await sql.end({ timeout: 5 });
}
