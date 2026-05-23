/**
 * Reactive `prefers-reduced-motion` reader. Returns a getter that flips
 * when the OS-level preference changes (e.g. macOS System Settings).
 * Safe to call from SSR — defaults to `false` until a window is
 * available.
 */
export function prefersReducedMotion(): { readonly value: boolean } {
  let value = $state(false);

  $effect(() => {
    if (typeof window === "undefined" || !window.matchMedia) return;
    const mql = window.matchMedia("(prefers-reduced-motion: reduce)");
    value = mql.matches;
    const onChange = (e: MediaQueryListEvent) => {
      value = e.matches;
    };
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  });

  return {
    get value() {
      return value;
    },
  };
}
