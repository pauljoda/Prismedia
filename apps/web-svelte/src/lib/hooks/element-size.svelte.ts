/**
 * Rune-based ResizeObserver wrapper. Returns reactive `width` / `height`
 * getters and an `attach` action to bind to an element.
 *
 * Usage:
 *   const size = elementSize();
 *   <div use:size.attach>…</div>
 *   Width is {size.width}px
 */
export function elementSize() {
  let width = $state(0);
  let height = $state(0);
  let observer: ResizeObserver | null = null;

  function attach(node: Element) {
    const rect = node.getBoundingClientRect();
    width = rect.width;
    height = rect.height;

    if (typeof ResizeObserver === "undefined") {
      return { destroy() {} };
    }

    observer = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (entry) {
        width = entry.contentRect.width;
        height = entry.contentRect.height;
      }
    });
    observer.observe(node);

    return {
      destroy() {
        observer?.disconnect();
        observer = null;
      },
    };
  }

  return {
    get width() {
      return width;
    },
    get height() {
      return height;
    },
    attach,
  };
}
