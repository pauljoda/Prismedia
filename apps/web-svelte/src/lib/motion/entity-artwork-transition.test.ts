import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { OnNavigate } from "@sveltejs/kit";
import { dur } from "@prismedia/ui-svelte";
import { EntityArtworkTransition } from "./entity-artwork-transition";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => { resolve = done; });
  return { promise, resolve };
}

describe("Entity artwork navigation", () => {
  let motion: EntityArtworkTransition;
  let link: HTMLAnchorElement;
  let image: HTMLImageElement;
  let update: () => Promise<void>;
  let finished: ReturnType<typeof deferred<void>>;
  let skip: ReturnType<typeof vi.fn>;
  let start: ReturnType<typeof vi.fn>;

  function arm(options: MouseEventInit = {}, target: EventTarget = image) {
    const event = new MouseEvent("click", { button: 0, ...options });
    Object.defineProperties(event, { currentTarget: { value: link }, target: { value: target } });
    motion.arm(event, "entity-1");
  }

  function navigation(type: OnNavigate["type"] = "link", href = link.href): OnNavigate {
    return { type, to: { url: new URL(href) }, complete: Promise.resolve() } as OnNavigate;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    motion = new EntityArtworkTransition();
    document.body.innerHTML = '<a href="/books/entity-1"><div class="media"><img src="/cover.jpg"></div></a>';
    link = document.querySelector("a")!;
    image = document.querySelector("img")!;
    Object.defineProperties(image, { complete: { value: true, configurable: true }, naturalWidth: { value: 200, configurable: true } });
    vi.spyOn(image, "getBoundingClientRect").mockReturnValue({ width: 200 } as DOMRect);
    vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false })));
    finished = deferred<void>();
    skip = vi.fn(() => finished.resolve());
    start = vi.fn((callback) => {
      update = callback;
      return { ready: Promise.resolve(), finished: finished.promise, skipTransition: skip };
    });
    Object.defineProperty(document, "startViewTransition", { configurable: true, value: start });
  });

  afterEach(() => {
    motion.dispose();
    delete (document as Partial<Document>).startViewTransition;
    document.body.innerHTML = "";
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("captures the clicked cover and matches only the loaded detail poster", async () => {
    arm();
    const navigating = motion.navigate(navigation());
    expect(image.style.viewTransitionName).toBe("entity-artwork");
    const updating = update();
    await navigating;
    expect(image.style.viewTransitionName).toBe("");
    const poster = image.cloneNode() as HTMLImageElement;
    Object.defineProperty(poster, "naturalWidth", { value: 200 });
    poster.decode = vi.fn().mockResolvedValue(undefined);
    document.body.append(poster);
    motion.receive("another-entity", poster);
    expect(poster.style.viewTransitionName).toBe("");
    motion.receive("entity-1", poster);
    await updating;
    expect(poster.style.viewTransitionName).toBe("entity-artwork");
    await vi.advanceTimersByTimeAsync(dur.slow);
    expect(skip).not.toHaveBeenCalled();
    finished.resolve();
    await finished.promise;
    expect(poster.style.viewTransitionName).toBe("");
    expect(document.documentElement.dataset.entityArtworkTransition).toBeUndefined();
  });

  it.each<OnNavigate["type"]>(["popstate", "goto"])("does not animate %s navigation", (type) => {
    arm();
    expect(motion.navigate(navigation(type))).toBeUndefined();
    expect(start).not.toHaveBeenCalled();
  });

  it.each<MouseEventInit>([{ metaKey: true }, { ctrlKey: true }, { shiftKey: true }, { altKey: true }, { button: 1 }])("preserves modified activation %o", (options) => {
    arm(options);
    motion.navigate(navigation());
    expect(start).not.toHaveBeenCalled();
  });

  it("does not arm a nested selection control or a new-tab link", () => {
    const control = document.createElement("button");
    link.append(control);
    arm({}, control);
    motion.navigate(navigation());
    link.target = "_blank";
    arm();
    motion.navigate(navigation());
    expect(start).not.toHaveBeenCalled();
  });

  it("ignores unloaded covers, reduced motion, and unsupported browsers", () => {
    Object.defineProperty(image, "complete", { value: false });
    arm();
    motion.navigate(navigation());
    Object.defineProperty(image, "complete", { value: true });
    vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: true })));
    arm();
    motion.navigate(navigation());
    vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false })));
    delete (document as Partial<Document>).startViewTransition;
    arm();
    expect(motion.navigate(navigation())).toBeUndefined();
    expect(start).not.toHaveBeenCalled();
  });

  it("consumes intent once and never matches another destination", () => {
    arm();
    motion.navigate(navigation("link", new URL("/settings", location.href).href));
    motion.navigate(navigation());
    expect(start).not.toHaveBeenCalled();
  });

  it("expires an abandoned or cancelled click", async () => {
    arm();
    await vi.advanceTimersByTimeAsync(2_001);
    motion.navigate(navigation());
    expect(start).not.toHaveBeenCalled();
  });

  it("falls back promptly when the detail or its image never arrives", async () => {
    arm();
    const navigating = motion.navigate(navigation());
    const updating = update();
    await navigating;
    await vi.advanceTimersByTimeAsync(dur.slow);
    await updating;
    expect(skip).toHaveBeenCalled();
    expect(image.style.viewTransitionName).toBe("");
    expect(document.documentElement.dataset.entityArtworkTransition).toBeUndefined();
  });

  it("releases navigation if the browser cannot start a snapshot", async () => {
    start.mockImplementation(() => { throw new Error("Snapshot unavailable"); });
    arm();
    await motion.navigate(navigation());
    expect(image.style.viewTransitionName).toBe("");
    expect(document.documentElement.dataset.entityArtworkTransition).toBeUndefined();
  });

  it("cancels outstanding snapshots when another route navigation starts", async () => {
    arm();
    motion.navigate(navigation());
    const updating = update();
    motion.navigate(navigation("popstate"));
    await updating;
    expect(skip).toHaveBeenCalled();
    expect(document.documentElement.dataset.entityArtworkTransition).toBeUndefined();
  });
});
