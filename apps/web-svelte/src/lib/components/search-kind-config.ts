import type { SearchEntityKind } from "$lib/search/models";
import { ALL_SEARCH_KINDS } from "$lib/search/models";
import { entityKindIcon } from "$lib/entities/entity-kind-icons";
import { labelForEntityKind, resolveEntityBrowsePath } from "$lib/entities/entity-codes";
import type { Component } from "svelte";

interface SearchKindConfig {
  label: string;
  icon: Component;
  href: string;
}

export const SEARCH_KIND_CONFIG = Object.fromEntries(
  ALL_SEARCH_KINDS.map((kind) => {
    const href = resolveEntityBrowsePath(kind);
    if (!href) throw new Error(`Search kind '${kind}' has no Entity browse route`);
    return [kind, {
      label: labelForEntityKind(kind),
      icon: entityKindIcon(kind),
      href,
    }];
  }),
) as Record<SearchEntityKind, SearchKindConfig>;
