import type { LayoutLoad } from "./$types";
import { fetchSettingsValues } from "$lib/api/settings";
import { settingKeys, valuesToLibrarySettings } from "$lib/settings/app-settings";
import { readSidebarCookie } from "$lib/stores/app-chrome.svelte";

export const ssr = false;

export const load: LayoutLoad = async () => {
  const initialCollapsed = readSidebarCookie();
  try {
    const settings = valuesToLibrarySettings(
      (await fetchSettingsValues([
        settingKeys.visibilityDefaultMode,
        settingKeys.visibilityLanAutoEnable,
      ])).values,
    );

    return {
      initialCollapsed,
      initialNsfwMode: settings.visibilityDefaultMode,
      lanAutoEnable: settings.nsfwLanAutoEnable,
    };
  } catch {
    return { initialCollapsed };
  }
};
