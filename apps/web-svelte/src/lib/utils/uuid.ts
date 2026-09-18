/**
 * RFC 4122 version 4 UUID generation that does not depend on a secure context.
 *
 * `crypto.randomUUID` is only exposed on secure origins (HTTPS, or localhost). Prismedia is
 * designed to be reached over a private LAN, where the common case is a plain
 * `http://<lan-ip>:8008` origin that is *not* a secure context. On those origins
 * `crypto.randomUUID` is `undefined`, so callers must not reach for it directly and must not
 * fall back to an ad hoc id shape: several Prismedia contracts type these identifiers as a
 * `Guid`, and a non-UUID value fails server model binding (HTTP 400) or a `:guid` route
 * constraint (HTTP 404).
 *
 * `crypto.getRandomValues` carries no secure-context requirement, so it stays available on the
 * LAN origins Prismedia actually runs on and is the preferred entropy source here.
 */

/** Generates a random RFC 4122 v4 UUID. Safe on insecure (plain-HTTP LAN) origins. */
export function createUuid(): string {
  const bytes = randomBytes(16);

  // Pin the version (4) and variant (RFC 4122) bits required by the UUID spec.
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  const hex: string[] = [];
  for (let index = 0; index < bytes.length; index += 1) {
    hex.push(bytes[index].toString(16).padStart(2, "0"));
  }

  return [
    hex.slice(0, 4).join(""),
    hex.slice(4, 6).join(""),
    hex.slice(6, 8).join(""),
    hex.slice(8, 10).join(""),
    hex.slice(10, 16).join(""),
  ].join("-");
}

/**
 * Fills a byte buffer with random values, preferring the Web Crypto RNG and degrading to
 * `Math.random` only where Web Crypto is entirely absent (older embedded webviews). These ids
 * address in-flight operations and sessions; they are not security tokens.
 */
function randomBytes(length: number): Uint8Array {
  const bytes = new Uint8Array(length);
  const webCrypto = globalThis.crypto;

  if (typeof webCrypto?.getRandomValues === "function") {
    webCrypto.getRandomValues(bytes);
    return bytes;
  }

  for (let index = 0; index < length; index += 1) {
    bytes[index] = Math.floor(Math.random() * 256);
  }

  return bytes;
}
