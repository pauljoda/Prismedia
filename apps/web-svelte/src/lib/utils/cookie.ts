/** Read a cookie value by name. Returns `undefined` when absent or on the server. */
export function readCookie(name: string): string | undefined {
  if (typeof document === "undefined") return undefined;
  const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${name}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : undefined;
}

/** Write a cookie with sensible defaults (path=/, samesite=lax). No-op on the server. */
export function writeCookie(name: string, value: string, maxAge = 31536000): void {
  if (typeof document === "undefined") return;
  document.cookie = `${name}=${encodeURIComponent(value)};path=/;max-age=${maxAge};samesite=lax`;
}

/** Delete a cookie by setting max-age to 0. */
export function deleteCookie(name: string): void {
  if (typeof document === "undefined") return;
  document.cookie = `${name}=;path=/;max-age=0;samesite=lax`;
}
