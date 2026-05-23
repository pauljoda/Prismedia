import { useNsfw } from "$lib/nsfw/store.svelte";

/**
 * Filter a list of providers by NSFW classification against the
 * current NSFW mode. When mode is "off" (SFW), only non-NSFW
 * providers are returned; "show" passes everything through.
 *
 * Call from inside a `$derived` so the result re-evaluates when
 * either the provider list or the NSFW mode changes.
 */
export function filterNsfwAware<T extends { isNsfw: boolean }>(
  providers: T[],
): T[] {
  const nsfw = useNsfw();
  return nsfw.mode === "off" ? providers.filter((p) => !p.isNsfw) : providers;
}
