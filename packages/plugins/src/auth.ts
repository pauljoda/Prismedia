/**
 * Plugin authentication — resolve, encrypt, and decrypt per-plugin credentials.
 *
 * Credentials are stored encrypted at rest in the plugin_auth table using
 * AES-256-GCM. The key is derived from the PRISMEDIA_SECRET env var via PBKDF2.
 * Each credential value is stored as "salt:iv:ciphertext" (all base64).
 */

import { randomBytes, createCipheriv, createDecipheriv, pbkdf2Sync } from "node:crypto";

const ALGORITHM = "aes-256-gcm";
const SALT_LENGTH = 16;
const IV_LENGTH = 12;
const KEY_LENGTH = 32;
const PBKDF2_ITERATIONS = 100_000;

const DEV_FALLBACK_SECRET = "prismedia-dev-secret-do-not-use-in-production-32ch";

function getSecret(): string {
  const secret = process.env.PRISMEDIA_SECRET;
  if (secret) return secret;

  if (process.env.NODE_ENV === "production") {
    throw new Error(
      "PRISMEDIA_SECRET environment variable is required for plugin credential encryption. " +
        "Set it to a random string of at least 32 characters.",
    );
  }

  // Dev fallback — credentials are still encrypted at rest but with a known key
  return DEV_FALLBACK_SECRET;
}

function deriveKey(salt: Buffer): Buffer {
  return pbkdf2Sync(getSecret(), salt, PBKDF2_ITERATIONS, KEY_LENGTH, "sha256");
}

/**
 * Encrypt a plain-text value for storage in plugin_auth.encrypted_value.
 * Returns "salt:iv:ciphertext:tag" in base64.
 */
export function encryptAuthValue(plaintext: string): string {
  const salt = randomBytes(SALT_LENGTH);
  const key = deriveKey(salt);
  const iv = randomBytes(IV_LENGTH);

  const cipher = createCipheriv(ALGORITHM, key, iv);
  const encrypted = Buffer.concat([
    cipher.update(plaintext, "utf-8"),
    cipher.final(),
  ]);
  const tag = cipher.getAuthTag();

  return [
    salt.toString("base64"),
    iv.toString("base64"),
    encrypted.toString("base64"),
    tag.toString("base64"),
  ].join(":");
}

/**
 * Decrypt a value stored in plugin_auth.encrypted_value.
 * Input format: "salt:iv:ciphertext:tag" in base64.
 */
export function decryptAuthValue(stored: string): string {
  const parts = stored.split(":");
  if (parts.length !== 4) {
    throw new Error("Invalid encrypted auth value format");
  }

  const [saltB64, ivB64, ciphertextB64, tagB64] = parts;
  const salt = Buffer.from(saltB64, "base64");
  const iv = Buffer.from(ivB64, "base64");
  const ciphertext = Buffer.from(ciphertextB64, "base64");
  const tag = Buffer.from(tagB64, "base64");

  const key = deriveKey(salt);
  const decipher = createDecipheriv(ALGORITHM, key, iv);
  decipher.setAuthTag(tag);

  return Buffer.concat([
    decipher.update(ciphertext),
    decipher.final(),
  ]).toString("utf-8");
}

/**
 * Resolve all auth credentials for a plugin from the database.
 * Returns a Record<authKey, plaintext> for injection into the plugin execution envelope.
 */
export async function resolvePluginAuth(
  pluginId: string,
  rows: Array<{ authKey: string; encryptedValue: string }>,
): Promise<Record<string, string>> {
  const auth: Record<string, string> = {};
  for (const row of rows) {
    try {
      auth[row.authKey] = decryptAuthValue(row.encryptedValue);
    } catch {
      // Skip corrupted entries — authStatus will show "missing" in the UI
    }
  }
  return auth;
}
