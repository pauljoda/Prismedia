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

if (
  !globalThis.localStorage ||
  typeof globalThis.localStorage.getItem !== "function" ||
  typeof globalThis.localStorage.setItem !== "function" ||
  typeof globalThis.localStorage.clear !== "function"
) {
  const values = new Map<string, string>();
  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    writable: true,
    value: {
      get length() {
        return values.size;
      },
      clear() {
        values.clear();
      },
      getItem(key: string) {
        return values.get(String(key)) ?? null;
      },
      key(index: number) {
        return Array.from(values.keys())[index] ?? null;
      },
      removeItem(key: string) {
        values.delete(String(key));
      },
      setItem(key: string, value: string) {
        values.set(String(key), String(value));
      },
    } satisfies Storage,
  });
}
