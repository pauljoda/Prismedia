import { IDENTIFY_ACTION, type RequestMediaKindCode } from "$lib/api/generated/codes";
import type { PluginEntitySupport, PluginProvider } from "$lib/api/identify-types";
import { requestKindInfo } from "$lib/requests/request-helpers";

/** Returns the usable schema-bearing support declaration for a plugin in Discover. */
export function discoverSearchSupport(
  provider: PluginProvider,
  kind: RequestMediaKindCode,
  hideNsfw: boolean,
): PluginEntitySupport | null {
  const kindInfo = requestKindInfo(kind);
  if (
    !kindInfo?.discoverable ||
    !provider.installed ||
    !provider.enabled ||
    provider.missingAuthKeys.length > 0 ||
    (hideNsfw && provider.isNsfw)
  ) {
    return null;
  }

  return provider.supports.find((support) =>
    support.entityKind === kindInfo.pluginEntityKind &&
    support.actions.includes(IDENTIFY_ACTION.search) &&
    support.actions.includes(IDENTIFY_ACTION.lookupId) &&
    Boolean(support.search?.fields?.length),
  ) ?? null;
}

/** Enabled, authenticated plugins that can both search and review the selected Discover kind. */
export function discoverSearchProviders(
  providers: PluginProvider[],
  kind: RequestMediaKindCode,
  hideNsfw: boolean,
): PluginProvider[] {
  return providers
    .filter((provider) => discoverSearchSupport(provider, kind, hideNsfw))
    .toSorted((left, right) => left.name.localeCompare(right.name));
}
