import { describe, expect, it } from "vitest";
import { ENTITY_KIND, IDENTIFY_ACTION, REQUEST_MEDIA_KIND } from "$lib/api/generated/codes";
import type { PluginProvider } from "$lib/api/identify-types";
import { discoverSearchProviders, discoverSearchSupport } from "./discovery-plugins";

describe("Discover plugin capabilities", () => {
  it("uses the plugin protocol kind for authors rather than their materialized Entity kind", () => {
    const openLibrary = provider("openlibrary", ENTITY_KIND.person);

    expect(discoverSearchSupport(openLibrary, REQUEST_MEDIA_KIND.author, false)).not.toBeNull();
    expect(discoverSearchSupport(openLibrary, REQUEST_MEDIA_KIND.book, false)).toBeNull();
  });

  it("reuses Book provider capabilities for audiobook discovery", () => {
    const openLibrary = provider("openlibrary", ENTITY_KIND.book);

    expect(discoverSearchSupport(openLibrary, REQUEST_MEDIA_KIND.audiobook, false)).not.toBeNull();
  });

  it("requires installed, enabled, authenticated Search plus LookupId with a schema", () => {
    const valid = provider("valid", ENTITY_KIND.movie);
    const searchOnly = provider("search-only", ENTITY_KIND.movie, [IDENTIFY_ACTION.search]);
    const missingAuth = { ...provider("missing-auth", ENTITY_KIND.movie), missingAuthKeys: ["token"] };
    const nsfw = { ...provider("nsfw", ENTITY_KIND.movie), isNsfw: true };

    expect(discoverSearchProviders([searchOnly, missingAuth, nsfw, valid], REQUEST_MEDIA_KIND.movie, true))
      .toEqual([valid]);
  });

  it("places the configured default first and falls back when it is unavailable", () => {
    const alpha = provider("alpha", ENTITY_KIND.movie);
    const preferred = provider("zulu", ENTITY_KIND.movie);
    const disabled = { ...provider("disabled", ENTITY_KIND.movie), enabled: false };

    expect(discoverSearchProviders(
      [preferred, alpha],
      REQUEST_MEDIA_KIND.movie,
      false,
      preferred.id,
    )).toEqual([preferred, alpha]);
    expect(discoverSearchProviders(
      [disabled, preferred, alpha],
      REQUEST_MEDIA_KIND.movie,
      false,
      disabled.id,
    )).toEqual([alpha, preferred]);
    expect(discoverSearchProviders(
      [preferred, alpha],
      REQUEST_MEDIA_KIND.movie,
      false,
      "removed-provider",
    )).toEqual([alpha, preferred]);
  });
});

function provider(
  id: string,
  entityKind: string,
  actions = [IDENTIFY_ACTION.search, IDENTIFY_ACTION.lookupId],
): PluginProvider {
  return {
    id,
    name: id,
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{
      entityKind,
      actions,
      identityNamespaces: [id],
      search: actions.includes(IDENTIFY_ACTION.search)
        ? { fields: [{ key: "title", label: "Title", type: "text", required: true }] }
        : null,
    }],
    auth: [],
    missingAuthKeys: [],
  };
}
