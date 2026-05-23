/**
 * TypeScript types for the StashBox GraphQL API responses.
 *
 * These mirror the stash-box GraphQL schema used by StashDB, FansDB,
 * PMVStash, JAVStash, ThePornDB, and compatible instances.
 */

// ─── Primitives ────────────────────────────────────────────────────

export type FingerprintAlgorithm = "MD5" | "OSHASH" | "PHASH";

export interface StashBoxFingerprint {
  hash: string;
  algorithm: FingerprintAlgorithm;
}

export interface StashBoxFingerprintResult {
  algorithm: string;
  hash: string;
  duration: number;
}

export interface StashBoxURL {
  url: string;
  type: string;
}

export interface StashBoxImage {
  id: string;
  url: string;
  width: number;
  height: number;
}

// ─── Scenes ────────────────────────────────────────────────────────

export interface StashBoxScene {
  id: string;
  title: string | null;
  code: string | null;
  details: string | null;
  director: string | null;
  duration: number | null;
  date: string | null;
  urls: StashBoxURL[];
  images: StashBoxImage[];
  studio: StashBoxStudio | null;
  tags: StashBoxTag[];
  performers: StashBoxPerformerAppearance[];
  fingerprints: StashBoxFingerprintResult[];
}

export interface StashBoxPerformerAppearance {
  as: string | null;
  performer: StashBoxPerformer;
}

// ─── Performers ────────────────────────────────────────────────────

export interface StashBoxMeasurements {
  band_size: number | null;
  cup_size: string | null;
  waist: number | null;
  hip: number | null;
}

export interface StashBoxBodyMod {
  location: string;
  description: string | null;
}

export interface StashBoxPerformer {
  id: string;
  name: string;
  disambiguation: string | null;
  aliases: string[];
  gender: string | null;
  deleted: boolean;
  merged_ids: string[];
  urls: StashBoxURL[];
  images: StashBoxImage[];
  birth_date: string | null;
  death_date: string | null;
  ethnicity: string | null;
  country: string | null;
  eye_color: string | null;
  hair_color: string | null;
  height: number | null;
  measurements: StashBoxMeasurements;
  breast_type: string | null;
  career_start_year: number | null;
  career_end_year: number | null;
  tattoos: StashBoxBodyMod[];
  piercings: StashBoxBodyMod[];
}

// ─── Studios ───────────────────────────────────────────────────────

export interface StashBoxStudio {
  id: string;
  name: string;
  aliases: string[];
  urls: StashBoxURL[];
  parent: { id: string; name: string } | null;
  images: StashBoxImage[];
}

// ─── Tags ──────────────────────────────────────────────────────────

export interface StashBoxTag {
  id: string;
  name: string;
  description: string | null;
  aliases: string[];
  category: { id: string; name: string; description: string | null } | null;
}
