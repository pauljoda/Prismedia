import type { Action } from "svelte/action";

/**
 * Move a node to `target` (default `document.body`) so it escapes overflow/transform
 * ancestors. Restores the node on destroy.
 */
export const portal: Action<HTMLElement, HTMLElement | undefined> = (node, target) => {
  if (typeof document === "undefined") {
    return { destroy: () => {} };
  }
  const t = target ?? document.body;
  t.appendChild(node);
  return {
    destroy() {
      node.remove();
    },
  };
};
