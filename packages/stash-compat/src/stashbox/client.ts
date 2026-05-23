/**
 * StashBox GraphQL client.
 *
 * Talks to StashDB, FansDB, PMVStash, JAVStash, ThePornDB, and any
 * compatible stash-box instance. Read-only — no draft submissions.
 */

import type {
  FingerprintAlgorithm,
  StashBoxFingerprint,
  StashBoxScene,
  StashBoxPerformer,
  StashBoxStudio,
  StashBoxTag,
} from "./types";

export interface FingerprintSubmissionInput {
  scene_id: string;
  fingerprint: {
    hash: string;
    algorithm: FingerprintAlgorithm;
    /** Duration in integer seconds. Stash refuses to submit when duration == 0. */
    duration: number;
  };
  unmatch?: boolean;
}

// ─── GraphQL Fragments ─────────────────────────────────────────────

const URL_FRAGMENT = `
fragment URLFragment on URL {
  url
  type
}`;

const IMAGE_FRAGMENT = `
fragment ImageFragment on Image {
  id
  url
  width
  height
}`;

const FINGERPRINT_FRAGMENT = `
fragment FingerprintFragment on Fingerprint {
  algorithm
  hash
  duration
}`;

const TAG_FRAGMENT = `
fragment TagFragment on Tag {
  id
  name
  description
  aliases
  category {
    id
    name
    description
  }
}`;

const STUDIO_FRAGMENT = `
fragment StudioFragment on Studio {
  id
  name
  aliases
  urls { ...URLFragment }
  parent { id name }
  images { ...ImageFragment }
}`;

const PERFORMER_FRAGMENT = `
fragment PerformerFragment on Performer {
  id
  name
  disambiguation
  aliases
  gender
  deleted
  merged_ids
  urls { ...URLFragment }
  images { ...ImageFragment }
  birth_date
  death_date
  ethnicity
  country
  eye_color
  hair_color
  height
  measurements { band_size cup_size waist hip }
  breast_type
  career_start_year
  career_end_year
  tattoos { location description }
  piercings { location description }
}`;

const SCENE_FRAGMENT = `
fragment SceneFragment on Scene {
  id
  title
  code
  details
  director
  duration
  date
  urls { ...URLFragment }
  images { ...ImageFragment }
  studio { ...StudioFragment }
  tags { ...TagFragment }
  performers {
    as
    performer { ...PerformerFragment }
  }
  fingerprints { ...FingerprintFragment }
}`;

const ALL_FRAGMENTS = [
  URL_FRAGMENT,
  IMAGE_FRAGMENT,
  FINGERPRINT_FRAGMENT,
  TAG_FRAGMENT,
  STUDIO_FRAGMENT,
  PERFORMER_FRAGMENT,
  SCENE_FRAGMENT,
].join("\n");

// ─── Queries ───────────────────────────────────────────────────────

const FIND_SCENES_BY_FINGERPRINTS = `
query FindScenesBySceneFingerprints($fingerprints: [[FingerprintQueryInput!]!]!) {
  findScenesBySceneFingerprints(fingerprints: $fingerprints) {
    ...SceneFragment
  }
}
${ALL_FRAGMENTS}`;

const FIND_SCENE_BY_ID = `
query FindSceneByID($id: ID!) {
  findScene(id: $id) {
    ...SceneFragment
  }
}
${ALL_FRAGMENTS}`;

// Mutation for contributing a single fingerprint (md5/oshash/phash) to a
// StashBox-protocol server. Input shape matches upstream stash-box schema.
const SUBMIT_FINGERPRINT = `
mutation SubmitFingerprint($input: FingerprintSubmission!) {
  submitFingerprint(input: $input)
}`;

const SEARCH_SCENE = `
query SearchScene($term: String!) {
  searchScene(term: $term) {
    ...SceneFragment
  }
}
${ALL_FRAGMENTS}`;

const SEARCH_PERFORMER = `
query SearchPerformer($term: String!) {
  searchPerformer(term: $term) {
    ...PerformerFragment
  }
}
${URL_FRAGMENT}
${IMAGE_FRAGMENT}
${PERFORMER_FRAGMENT}`;

const FIND_PERFORMER_BY_ID = `
query FindPerformerByID($id: ID!) {
  findPerformer(id: $id) {
    ...PerformerFragment
  }
}
${URL_FRAGMENT}
${IMAGE_FRAGMENT}
${PERFORMER_FRAGMENT}`;

const FIND_STUDIO = `
query FindStudio($id: ID, $name: String) {
  findStudio(id: $id, name: $name) {
    ...StudioFragment
  }
}
${URL_FRAGMENT}
${IMAGE_FRAGMENT}
${STUDIO_FRAGMENT}`;

const FIND_TAG = `
query FindTag($id: ID, $name: String) {
  findTag(id: $id, name: $name) {
    ...TagFragment
  }
}
${TAG_FRAGMENT}`;

const QUERY_TAGS = `
query QueryTags($input: TagQueryInput!) {
  queryTags(input: $input) {
    count
    tags { ...TagFragment }
  }
}
${TAG_FRAGMENT}`;

// ─── Rate Limiter ──────────────────────────────────────────────────

class TokenBucket {
  private tokens: number;
  private lastRefill: number;

  constructor(
    private maxTokens: number,
    private refillRate: number, // tokens per second
  ) {
    this.tokens = maxTokens;
    this.lastRefill = Date.now();
  }

  async acquire(): Promise<void> {
    this.refill();
    if (this.tokens >= 1) {
      this.tokens -= 1;
      return;
    }
    // Wait until a token is available
    const waitMs = ((1 - this.tokens) / this.refillRate) * 1000;
    await new Promise((resolve) => setTimeout(resolve, Math.ceil(waitMs)));
    this.refill();
    this.tokens -= 1;
  }

  private refill() {
    const now = Date.now();
    const elapsed = (now - this.lastRefill) / 1000;
    this.tokens = Math.min(this.maxTokens, this.tokens + elapsed * this.refillRate);
    this.lastRefill = now;
  }
}

// ─── Client ────────────────────────────────────────────────────────

const FINGERPRINT_BATCH_SIZE = 40;
const DEFAULT_TIMEOUT_MS = 30_000;
const DEFAULT_MAX_REQUESTS_PER_MINUTE = 240;

export class StashBoxClient {
  private limiter: TokenBucket;
  private timeoutMs: number;

  constructor(
    private endpoint: string,
    private apiKey: string,
    options?: { timeoutMs?: number; maxRequestsPerMinute?: number },
  ) {
    this.timeoutMs = options?.timeoutMs ?? DEFAULT_TIMEOUT_MS;
    const rpm = options?.maxRequestsPerMinute ?? DEFAULT_MAX_REQUESTS_PER_MINUTE;
    this.limiter = new TokenBucket(rpm, rpm / 60);
  }

  // ── Scene Queries ──────────────────────────────────────────────

  /**
   * Look up scenes by fingerprints. Each element in the outer array
   * is one scene's fingerprints; the result is an array (same length)
   * of matched scenes (each scene may match zero or more results).
   *
   * Batches requests in groups of 40 per the StashBox protocol.
   */
  async findScenesByFingerprints(
    fingerprints: StashBoxFingerprint[][],
  ): Promise<(StashBoxScene[] | null)[]> {
    if (fingerprints.length === 0) return [];

    const results: (StashBoxScene[] | null)[] = new Array(fingerprints.length).fill(null);

    for (let offset = 0; offset < fingerprints.length; offset += FINGERPRINT_BATCH_SIZE) {
      const batch = fingerprints.slice(offset, offset + FINGERPRINT_BATCH_SIZE);
      const data = await this.query<{
        findScenesBySceneFingerprints: (StashBoxScene[] | null)[];
      }>(FIND_SCENES_BY_FINGERPRINTS, { fingerprints: batch });

      const batchResults = data.findScenesBySceneFingerprints ?? [];
      for (let i = 0; i < batchResults.length; i++) {
        results[offset + i] = batchResults[i];
      }
    }

    return results;
  }

  /** Free-text search for scenes by title/code. */
  async searchScenes(term: string): Promise<StashBoxScene[]> {
    const data = await this.query<{ searchScene: StashBoxScene[] }>(
      SEARCH_SCENE,
      { term },
    );
    return data.searchScene ?? [];
  }

  /** Look up a scene by remote StashBox ID. Null when deleted upstream. */
  async findSceneById(id: string): Promise<StashBoxScene | null> {
    const data = await this.query<{ findScene: StashBoxScene | null }>(
      FIND_SCENE_BY_ID,
      { id },
    );
    return data.findScene ?? null;
  }

  /**
   * Submit a single fingerprint (md5/oshash/phash) to associate it with a
   * remote StashBox scene. Mirrors Stash's SubmitFingerprints loop: one
   * mutation per (scene, algorithm) pair, serialized through the token bucket.
   * Returns the server's boolean ack.
   */
  async submitFingerprint(input: FingerprintSubmissionInput): Promise<boolean> {
    const data = await this.query<{ submitFingerprint: boolean }>(
      SUBMIT_FINGERPRINT,
      { input },
    );
    return data.submitFingerprint === true;
  }

  // ── Performer Queries ──────────────────────────────────────────

  /** Free-text search for performers. */
  async searchPerformers(term: string): Promise<StashBoxPerformer[]> {
    const data = await this.query<{ searchPerformer: StashBoxPerformer[] }>(
      SEARCH_PERFORMER,
      { term },
    );
    return data.searchPerformer ?? [];
  }

  /** Look up a performer by StashBox ID. */
  async findPerformer(id: string): Promise<StashBoxPerformer | null> {
    const data = await this.query<{ findPerformer: StashBoxPerformer | null }>(
      FIND_PERFORMER_BY_ID,
      { id },
    );
    return data.findPerformer ?? null;
  }

  // ── Studio Queries ─────────────────────────────────────────────

  /**
   * Find a studio by name or ID.
   * Automatically detects UUIDs vs name strings.
   */
  async findStudio(query: string): Promise<StashBoxStudio | null> {
    const isUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(query);
    const variables = isUuid ? { id: query } : { name: query };
    const data = await this.query<{ findStudio: StashBoxStudio | null }>(
      FIND_STUDIO,
      variables,
    );
    return data.findStudio ?? null;
  }

  // ── Tag Queries ────────────────────────────────────────────────

  /** Look up a tag by StashBox ID. */
  async findTag(id: string): Promise<StashBoxTag | null> {
    const data = await this.query<{ findTag: StashBoxTag | null }>(
      FIND_TAG,
      { id },
    );
    return data.findTag ?? null;
  }

  /** Search tags by name. */
  async queryTags(query: string): Promise<StashBoxTag[]> {
    const isUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(query);
    if (isUuid) {
      const tag = await this.findTag(query);
      return tag ? [tag] : [];
    }

    const data = await this.query<{
      queryTags: { count: number; tags: StashBoxTag[] };
    }>(QUERY_TAGS, {
      input: {
        name: query,
        page: 1,
        per_page: 25,
        sort: "NAME",
        direction: "ASC",
      },
    });

    const tags = data.queryTags?.tags ?? [];
    // Sort exact matches first
    const lower = query.toLowerCase();
    return tags.sort((a, b) => {
      const aExact = a.name.toLowerCase() === lower ? 0 : 1;
      const bExact = b.name.toLowerCase() === lower ? 0 : 1;
      return aExact - bExact;
    });
  }

  // ── Test Connection ────────────────────────────────────────────

  /** Verify connection and credentials using a spec-compliant introspection query. */
  async testConnection(): Promise<{ valid: boolean; error?: string }> {
    try {
      // Use __typename which is valid on any GraphQL server (not just StashDB)
      await this.query<{ __typename: string }>(
        "query { __typename }",
        {},
      );
      return { valid: true };
    } catch (err) {
      return {
        valid: false,
        error: err instanceof Error ? err.message : "Unknown error",
      };
    }
  }

  // ── Internal ───────────────────────────────────────────────────

  private async query<T>(
    gql: string,
    variables: Record<string, unknown>,
  ): Promise<T> {
    await this.limiter.acquire();

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const response = await fetch(this.endpoint, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ApiKey: this.apiKey,
        },
        body: JSON.stringify({ query: gql, variables }),
        signal: controller.signal,
      });

      if (!response.ok) {
        const text = await response.text().catch(() => "");
        throw new StashBoxError(
          `StashBox returned ${response.status}: ${text.slice(0, 200)}`,
          response.status,
        );
      }

      const json = (await response.json()) as {
        data?: T;
        errors?: Array<{ message: string }>;
      };

      if (json.errors?.length) {
        throw new StashBoxError(
          `StashBox GraphQL error: ${json.errors.map((e) => e.message).join("; ")}`,
        );
      }

      if (!json.data) {
        throw new StashBoxError("StashBox returned empty data");
      }

      return json.data;
    } finally {
      clearTimeout(timeout);
    }
  }
}

export class StashBoxError extends Error {
  constructor(
    message: string,
    public readonly statusCode?: number,
  ) {
    super(message);
    this.name = "StashBoxError";
  }
}
