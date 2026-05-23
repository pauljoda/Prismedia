import "@testing-library/jest-dom/vitest";

if (!globalThis.Element.prototype.animate) {
  Object.defineProperty(globalThis.Element.prototype, "animate", {
    configurable: true,
    writable: true,
    value() {
      return {
        cancel: () => {},
        finish: () => {},
        finished: Promise.resolve(),
        ready: Promise.resolve(),
        oncancel: null,
        onfinish: null,
        play: () => {},
        pause: () => {},
      } as unknown as Animation;
    },
  });
}

if (!globalThis.Element.prototype.getAnimations) {
  Object.defineProperty(globalThis.Element.prototype, "getAnimations", {
    configurable: true,
    writable: true,
    value() {
      return [];
    },
  });
}

Object.defineProperty(globalThis.HTMLMediaElement.prototype, "load", {
  configurable: true,
  writable: true,
  value() {},
});

Object.defineProperty(globalThis.HTMLMediaElement.prototype, "play", {
  configurable: true,
  writable: true,
  value() {
    return Promise.resolve();
  },
});

Object.defineProperty(globalThis.HTMLMediaElement.prototype, "pause", {
  configurable: true,
  writable: true,
  value() {},
});

if (!globalThis.ResizeObserver) {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

// JSDOM doesn't implement matchMedia. Svelte 5's reactivity layer uses
// it via tweened/spring stores and the prefers-reduced-motion hook.
if (!globalThis.matchMedia) {
  Object.defineProperty(globalThis, "matchMedia", {
    configurable: true,
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
}
