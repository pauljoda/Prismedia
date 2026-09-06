import type { OnNavigate } from "@sveltejs/kit";
import { tick } from "svelte";
import { dur, prefersReducedMotion } from "@prismedia/ui-svelte";
import { createOptionalContext } from "$lib/utils/context";

const ARTWORK_NAME = "entity-artwork";
const INTENT_LIFETIME_MS = 2_000;

interface ArtworkIntent {
  entityId: string;
  href: string;
  image: HTMLImageElement;
  armedAt: number;
}

interface ActiveTransition {
  entityId: string;
  receive: (image: HTMLImageElement | null) => void;
  cancel: () => void;
}

/**
 * Matches only explicitly activated thumbnails to their detail artwork. The
 * browser owns the snapshots and geometry; no cloned media or global route
 * animation is needed. Each app layout owns its own short-lived intent.
 */
export class EntityArtworkTransition {
  private intent: ArtworkIntent | null = null;
  private active: ActiveTransition | null = null;

  /** Arm ordinary same-tab link activation without changing native navigation. */
  arm(event: MouseEvent, entityId: string): void {
    this.intent = null;
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    if (!document.startViewTransition || prefersReducedMotion()) return;
    const link = event.currentTarget;
    if (!(link instanceof HTMLAnchorElement) || (link.target && link.target !== "_self") || link.hasAttribute("download")) return;
    if (new URL(link.href).origin !== location.origin || link.href === location.href) return;
    const target = event.target;
    if (target instanceof Element && target.closest("button, input, [role='checkbox']")) return;
    const image = link.querySelector<HTMLImageElement>(".media > img");
    if (!image || !image.complete || !image.naturalWidth || !image.getBoundingClientRect().width || getComputedStyle(image).opacity === "0") return;
    this.intent = { entityId, href: link.href, image, armedAt: Date.now() };
  }

  /** Called only by the actual detail poster, never its decorative backdrop. */
  receive(entityId: string, image: HTMLImageElement): void {
    if (this.active?.entityId === entityId) this.active.receive(image);
  }

  /** SvelteKit pauses only until the old artwork snapshot has been captured. */
  navigate(navigation: OnNavigate): Promise<void> | void {
    this.active?.cancel();
    const intent = this.intent;
    this.intent = null;
    if (!intent || navigation.type !== "link" || navigation.to?.url.href !== intent.href || !intent.image.isConnected) return;
    if (Date.now() - intent.armedAt > INTENT_LIFETIME_MS || !document.startViewTransition || prefersReducedMotion()) return;

    return new Promise<void>((resumeNavigation) => {
      const source = intent.image;
      const sourceName = source.style.viewTransitionName;
      let destination: HTMLImageElement | null = null;
      let destinationName = "";
      let transition: ViewTransition | undefined;
      let timer: ReturnType<typeof setTimeout> | undefined;
      let settled = false;
      let receive!: ActiveTransition["receive"];
      const artworkReady = new Promise<HTMLImageElement | null>((resolve) => { receive = resolve; });
      const active: ActiveTransition = {
        entityId: intent.entityId,
        receive,
        cancel: () => {
          transition?.skipTransition();
          receive(null);
          resumeNavigation();
          cleanup();
        },
      };
      const cleanup = () => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        source.style.viewTransitionName = sourceName;
        if (destination) destination.style.viewTransitionName = destinationName;
        if (this.active === active) {
          this.active = null;
          delete document.documentElement.dataset.entityArtworkTransition;
        }
      };
      this.active = active;
      source.style.viewTransitionName = ARTWORK_NAME;
      document.documentElement.dataset.entityArtworkTransition = "";

      // Detail data is loaded by the mounted route. Never hold the old page
      // indefinitely for a slow request, failed image, or posterless destination.
      timer = setTimeout(active.cancel, dur.slow);
      try {
        transition = document.startViewTransition(async () => {
          resumeNavigation();
          source.style.viewTransitionName = sourceName;
          await navigation.complete;
          const image = await artworkReady;
          if (settled || !image?.isConnected || !image.naturalWidth) {
            transition?.skipTransition();
            return;
          }
          destination = image;
          destinationName = image.style.viewTransitionName;
          image.style.viewTransitionName = ARTWORK_NAME;
          // Decode before the destination snapshot, including cached images.
          await image.decode().catch(() => undefined);
          await tick();
          if (settled) return;
          clearTimeout(timer);
        });
        void transition.ready.catch(() => undefined);
        void transition.finished.then(cleanup, () => { resumeNavigation(); cleanup(); });
      } catch {
        active.cancel();
      }
    });
  }

  /** Release snapshots and outstanding intent when the owning layout unmounts. */
  dispose(): void {
    this.intent = null;
    this.active?.cancel();
  }
}

const context = createOptionalContext<EntityArtworkTransition | undefined>("EntityArtworkTransition", undefined);
export const provideEntityArtworkTransition = context.provide;
export const useEntityArtworkTransition = context.use;
