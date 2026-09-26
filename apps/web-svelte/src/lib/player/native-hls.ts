const APPLE_WEBKIT_VENDOR = "Apple Computer, Inc.";
const APPLE_WEBKIT_USER_AGENT_TOKEN = "AppleWebKit/";

export interface BrowserEngineIdentity {
  readonly userAgent?: string;
  readonly vendor?: string;
}

/**
 * Returns whether HLS should bypass hls.js for Apple's native media pipeline.
 * Chromium can report optimistic native HLS MIME support, so canPlayType alone
 * cannot safely select Vidstack's plain video provider.
 */
export function shouldPreferNativeHLS(identity: BrowserEngineIdentity | null | undefined): boolean {
  return identity?.vendor === APPLE_WEBKIT_VENDOR
    && identity.userAgent?.includes(APPLE_WEBKIT_USER_AGENT_TOKEN) === true;
}
