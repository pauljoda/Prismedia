import { browser } from "$app/environment";
import { createContext } from "$lib/utils/context";
import { readCookie, writeCookie } from "$lib/utils/cookie";

const ctx = createContext<AppChromeStore>("AppChrome");
const COOKIE_NAME = "prismedia-sidebar";

export interface AppBreadcrumb {
  label: string;
  href?: string;
}

export function parseSidebarCookie(raw: string | undefined): boolean {
  return raw === "collapsed";
}

export function readSidebarCookie(): boolean {
  if (!browser) return false;
  return parseSidebarCookie(readCookie(COOKIE_NAME));
}

function writeSidebarCookie(collapsed: boolean) {
  writeCookie(COOKIE_NAME, collapsed ? "collapsed" : "expanded");
}

export class AppChromeStore {
  sidebarCollapsed = $state(false);
  bottomDockInsetPx = $state(0);
  breadcrumbs = $state.raw<AppBreadcrumb[]>([]);
  private bottomDocks = new Map<string, number>();

  constructor(initialCollapsed: boolean) {
    this.sidebarCollapsed = initialCollapsed;
  }

  toggleSidebar() {
    this.sidebarCollapsed = !this.sidebarCollapsed;
    writeSidebarCookie(this.sidebarCollapsed);
  }

  setBottomDockInset(id: string, heightPx: number) {
    const height = Math.max(0, Math.ceil(heightPx));
    if (height === 0) this.bottomDocks.delete(id);
    else this.bottomDocks.set(id, height);
    this.bottomDockInsetPx = Math.max(0, ...this.bottomDocks.values());
  }

  clearBottomDockInset(id: string) {
    this.bottomDocks.delete(id);
    this.bottomDockInsetPx = Math.max(0, ...this.bottomDocks.values());
  }

  setBreadcrumbs(breadcrumbs: AppBreadcrumb[]) {
    this.breadcrumbs = breadcrumbs;
    return () => {
      if (this.breadcrumbs === breadcrumbs) this.breadcrumbs = [];
    };
  }
}

export function provideAppChrome(getInitialCollapsed: () => boolean) {
  return ctx.provide(new AppChromeStore(getInitialCollapsed()));
}

export const useAppChrome = ctx.use;
