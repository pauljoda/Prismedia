import { browser } from "$app/environment";
import {
  computeContainedScrollHeight,
  entityGridScrollContainerBottom,
  findEntityGridScrollAncestor,
} from "./entity-grid-viewport.svelte";

export interface EntityGridViewportControllerOptions {
  dockControls: () => boolean;
  scrollBottomPadding: () => number;
  scrollMaxHeight: () => string | null | undefined;
  scrollMinHeight: () => number;
}

/**
 * Owns EntityGrid's contained viewport measurement, scroll positioning, and the
 * short hover-preview suppression window used while any ancestor is scrolling.
 */
export class EntityGridViewportController {
  viewportEl = $state<HTMLDivElement>();
  sectionEl = $state<HTMLElement>();
  measuredScrollMaxHeight = $state<string | null>(null);
  measuredFillHeight = $state<string | null>(null);

  private hoverPreviewsResumeAt = 0;

  constructor(private readonly options: EntityGridViewportControllerOptions) {}

  get effectiveScrollMaxHeight(): string | null | undefined {
    if (!this.options.dockControls()) return null;
    return this.options.scrollMaxHeight() === undefined
      ? this.measuredScrollMaxHeight
      : this.options.scrollMaxHeight();
  }

  get containsScroll(): boolean {
    return this.options.dockControls() && this.options.scrollMaxHeight() !== null;
  }

  /** Installs the ResizeObserver and global scrolling listener for one mounted grid. */
  connect = (): (() => void) => {
    let raf: number | null = null;

    const measureViewport = (): void => {
      if (!this.options.dockControls() || !this.viewportEl || this.options.scrollMaxHeight() !== undefined) {
        this.measuredScrollMaxHeight = null;
        this.measuredFillHeight = null;
        return;
      }

      const containerBottom = entityGridScrollContainerBottom(this.viewportEl);
      this.measuredScrollMaxHeight = computeContainedScrollHeight({
        bottomPadding: this.options.scrollBottomPadding(),
        minHeight: this.options.scrollMinHeight(),
        top: this.viewportEl.getBoundingClientRect().top,
        viewportHeight: containerBottom,
      });

      if (this.sectionEl) {
        const sectionTop = this.sectionEl.getBoundingClientRect().top;
        const fill = Math.max(
          0,
          Math.floor(containerBottom - sectionTop - this.options.scrollBottomPadding()),
        );
        this.measuredFillHeight = `${fill}px`;
      } else {
        this.measuredFillHeight = null;
      }
    };

    const scheduleMeasure = (): void => {
      if (raf !== null) return;
      raf = requestAnimationFrame(() => {
        raf = null;
        measureViewport();
      });
    };

    const observer = new ResizeObserver(scheduleMeasure);
    if (this.viewportEl) observer.observe(this.viewportEl);
    if (this.sectionEl) observer.observe(this.sectionEl);
    window.addEventListener("resize", scheduleMeasure, { passive: true });
    window.addEventListener("scroll", this.markScrolling, { capture: true, passive: true });
    queueMicrotask(measureViewport);

    return () => {
      observer.disconnect();
      window.removeEventListener("resize", scheduleMeasure);
      window.removeEventListener("scroll", this.markScrolling, { capture: true });
      if (raf !== null) cancelAnimationFrame(raf);
    };
  };

  areHoverPreviewsSuppressed = (): boolean =>
    browser && performance.now() < this.hoverPreviewsResumeAt;

  markScrolling = (): void => {
    this.hoverPreviewsResumeAt = performance.now() + 220;
  };

  scrollPageToTop = (): void => {
    if (!browser || !this.viewportEl) return;
    const scrollAncestor = findEntityGridScrollAncestor(this.viewportEl);
    if (scrollAncestor instanceof HTMLElement) {
      const ancestorRect = scrollAncestor.getBoundingClientRect();
      const viewportRect = this.viewportEl.getBoundingClientRect();
      scrollAncestor.scrollTo({
        top: scrollAncestor.scrollTop + viewportRect.top - ancestorRect.top,
      });
      return;
    }

    window.scrollTo({
      top: window.scrollY + this.viewportEl.getBoundingClientRect().top,
    });
  };
}
