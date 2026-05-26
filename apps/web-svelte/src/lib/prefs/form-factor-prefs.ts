import { writeCookie } from "$lib/utils/cookie";

export type UiPrefsFormFactor = "mobile" | "desktop";

export const UI_PREFS_FORM_FACTOR_COOKIE = "prismedia-ui-form-factor";
export const MOBILE_UI_PREFS_QUERY = "(max-width: 767px)";

export type FormFactorUiPrefs<T> = Partial<Record<UiPrefsFormFactor, T>>;

export function parseUiPrefsFormFactor(raw: string | null | undefined): UiPrefsFormFactor | null {
  return raw === "mobile" || raw === "desktop" ? raw : null;
}

function rememberUiPrefsFormFactor(formFactor: UiPrefsFormFactor): void {
  writeCookie(UI_PREFS_FORM_FACTOR_COOKIE, formFactor);
}

export function detectUiPrefsFormFactor(): UiPrefsFormFactor {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return "desktop";
  }
  const formFactor = window.matchMedia(MOBILE_UI_PREFS_QUERY).matches
    ? "mobile"
    : "desktop";
  rememberUiPrefsFormFactor(formFactor);
  return formFactor;
}

export function formFactorUiPrefKey(
  baseKey: string,
  formFactor: UiPrefsFormFactor = "desktop",
  suffix = "",
): string {
  return `${baseKey}:${formFactor}${suffix}`;
}
