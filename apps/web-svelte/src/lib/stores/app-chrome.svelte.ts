import { getContext, setContext } from "svelte";
import { browser } from "$app/environment";

const KEY = Symbol("app-chrome");
const COOKIE_NAME = "prismedia-sidebar";
const COOKIE_MAX_AGE = 60 * 60 * 24 * 365;

export interface AppBreadcrumb {
  label: string;
  href?: string;
}

export function parseSidebarCookie(raw: string | undefined): boolean {
  return raw === "collapsed";
}

export function readSidebarCookie(): boolean {
  if (!browser) return false;
  const match = document.cookie.match(/(?:^|;\s*)prismedia-sidebar=([^;]*)/);
  return parseSidebarCookie(match ? decodeURIComponent(match[1]) : undefined);
}

function writeSidebarCookie(collapsed: boolean) {
  if (!browser) return;
  document.cookie = `${COOKIE_NAME}=${collapsed ? "collapsed" : "expanded"};path=/;max-age=${COOKIE_MAX_AGE}`;
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
  const store = new AppChromeStore(getInitialCollapsed());
  setContext(KEY, store);
  return store;
}

export function useAppChrome(): AppChromeStore {
  const ctx = getContext<AppChromeStore | undefined>(KEY);
  if (!ctx) throw new Error("useAppChrome must be used inside <AppChromeProvider>");
  return ctx;
}
