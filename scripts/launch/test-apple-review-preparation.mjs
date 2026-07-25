import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

const compose = await readFile(
  new URL('../../infra/docker/apple-review.compose.yml', import.meta.url),
  'utf8',
);
const seed = await readFile(
  new URL('./seed-apple-review-library.sh', import.meta.url),
  'utf8',
);
const attribution = await readFile(
  new URL('./apple-review-fixtures/ATTRIBUTION.md', import.meta.url),
  'utf8',
);
const comicInfo = await readFile(
  new URL('./apple-review-fixtures/ComicInfo.xml', import.meta.url),
  'utf8',
);
const metadata = await readFile(
  new URL('../../docs/launch/app-store/metadata.md', import.meta.url),
  'utf8',
);

assert.match(
  compose,
  /image: \$\{PRISMEDIA_IMAGE:\?[^}]+\}/,
  'review deployment must require an explicitly supplied image',
);
assert.doesNotMatch(
  compose,
  /image: .*:dev\b/,
  'review deployment must not follow the mutable dev tag',
);
assert.match(
  compose,
  /\/home\/paul\/docker-data\/apple-prismedia-media:\/media:ro/,
  'review media must use the dedicated read-only mount',
);
assert.doesNotMatch(
  compose,
  /\/home\/paul\/docker-data:\/media(?:\s|$)/,
  'review deployment must never mount the household media root',
);
assert.match(
  compose,
  /Host\(`apple-prismedia\.pauljoda\.com`\)/,
  'review deployment must use its isolated hostname',
);

assert.match(
  seed,
  /expected_target="\/home\/paul\/docker-data\/apple-prismedia-media"/,
  'seed script must constrain its target',
);
assert.match(
  seed,
  /Refusing to overwrite non-empty review library/,
  'seed script must preserve an existing review library',
);
for (const expectedSource of [
  'download.blender.org',
  'standardebooks.org',
  'archive.org',
  'peppercarrot.com',
  'incompetech.com',
  'upload.wikimedia.org',
]) {
  assert.ok(seed.includes(expectedSource), `missing licensed source: ${expectedSource}`);
}

for (const requiredCredit of [
  'Blender Foundation',
  'Standard Ebooks',
  'LibriVox',
  'Pepper&Carrot',
  'Kevin MacLeod',
  'Wikimedia Commons',
]) {
  assert.ok(
    attribution.includes(requiredCredit),
    `attribution is missing ${requiredCredit}`,
  );
}
assert.match(comicInfo, /<Title>Need a Hug\?<\/Title>/);
assert.match(comicInfo, /Creative Commons Attribution 4\.0/);

const subtitle = 'Your self-hosted media home';
const keywords =
  'self-hosted,media server,movies,music,audiobooks,ebooks,comics,reader,Apple TV,private';
assert.ok([...subtitle].length <= 30, 'subtitle exceeds 30 characters');
assert.ok(Buffer.byteLength(keywords, 'utf8') <= 100, 'keywords exceed 100 bytes');
assert.ok(metadata.includes(subtitle), 'metadata packet is missing the subtitle');
assert.ok(metadata.includes(keywords), 'metadata packet is missing the keywords');

console.log('Apple review preparation checks passed.');
