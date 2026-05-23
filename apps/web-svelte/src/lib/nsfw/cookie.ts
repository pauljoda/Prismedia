/**
 * Resolve NSFW display mode from the raw cookie value.
 * Matches defaults in +layout.server.ts.
 */
export type NsfwMode = "off" | "show";

export function parseNsfwModeCookie(raw: string | undefined): NsfwMode {
  if (raw === "show" || raw === "off") return raw;
  return "off";
}
