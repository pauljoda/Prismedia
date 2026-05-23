interface Options {
  rootMargin?: string;
  threshold?: number;
}

/**
 * Rune-based IntersectionObserver wrapper. Returns an object with a
 * reactive `inView` getter and an `attach` action to bind to an element.
 *
 * Usage:
 *   const iv = elementInView();
 *   <div use:iv.attach>…</div>
 *   {#if iv.inView}visible{/if}
 */
export function elementInView(options?: Options) {
  let inView = $state(false);
  let observer: IntersectionObserver | null = null;

  function attach(node: Element) {
    observer = new IntersectionObserver(
      ([entry]) => {
        inView = Boolean(entry?.isIntersecting);
      },
      {
        rootMargin: options?.rootMargin ?? "200px",
        threshold: options?.threshold ?? 0.01,
      },
    );
    observer.observe(node);
    return {
      destroy() {
        observer?.disconnect();
        observer = null;
      },
    };
  }

  return {
    get inView() {
      return inView;
    },
    attach,
  };
}
