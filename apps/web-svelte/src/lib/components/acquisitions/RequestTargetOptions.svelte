<script lang="ts">
  /**
   * The request-time questions asked before committing, profile-first: the quality profile is the
   * governing choice (it owns the rules AND the default import target), and the library select follows
   * it — picking a profile re-targets the library to that profile's default, which the user can then
   * override manually before submitting. Options are filtered to the request kind and to libraries the
   * current view mode may see; the selections bind out and ride the commit.
   */
  import { onMount, type Snippet } from "svelte";
  import { FolderOpen, SlidersHorizontal } from "@lucide/svelte";
  import { Select } from "@prismedia/ui-svelte";
  import { fetchAcquisitionProfiles } from "$lib/api/acquisitions";
  import { fetchLibraryRoots, type LibraryRoot } from "$lib/api/settings";
  import { ENTITY_KIND } from "$lib/api/generated/codes";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import type { BookAcquisitionProfileView } from "$lib/api/generated/model";
  import type { RequestKindInfo } from "$lib/requests/request-helpers";

  interface Props {
    kindInfo: RequestKindInfo;
    targetLibraryRootId: string | null;
    profileId: string | null;
    /** Optional action (the Request button) rendered at the end of the options row. */
    actions?: Snippet;
  }
  let { kindInfo, targetLibraryRootId = $bindable(), profileId = $bindable(), actions }: Props = $props();

  const nsfw = useNsfw();

  let roots = $state<LibraryRoot[]>([]);
  let profiles = $state<BookAcquisitionProfileView[]>([]);
  let loaded = $state(false);

  // Offer only libraries the current view mode may see: in SFW mode an NSFW library is not a
  // valid destination the user can pick (the same visibility rule the rest of the app follows).
  const suitableRoots = $derived(
    roots.filter(
      (root) =>
        root.enabled &&
        kindInfo.rootFlag !== null &&
        root[kindInfo.rootFlag] &&
        (nsfw.mode === "show" || !root.isNsfw),
    ),
  );
  /**
   * Profiles are named by what they govern (a "music" profile covers an artist's album requests).
   * Keyed on the profile kind, not the root flag — movies and TV share the video root but have
   * separate profiles, so a series must read "TV", not "movie".
   */
  const profileNoun = $derived(
    kindInfo.profileKind === ENTITY_KIND.movie
      ? "movie"
      : kindInfo.profileKind === ENTITY_KIND.videoSeries
        ? "TV"
        : kindInfo.profileKind === ENTITY_KIND.audioLibrary
          ? "music"
          : "book",
  );
  const kindProfiles = $derived(profiles.filter((profile) => profile.kind === kindInfo.profileKind));
  const rootOptions = $derived(suitableRoots.map((root) => ({ value: root.id, label: root.label || root.path })));
  const profileOptions = $derived(kindProfiles.map((profile) => ({ value: profile.id, label: profile.displayName })));

  /** The library the given profile targets when it suits this kind, else the first regular suitable library. */
  function defaultRootFor(profile: BookAcquisitionProfileView | null): string | null {
    const profileRoot = suitableRoots.find((root) => root.id === profile?.targetLibraryRootId);
    if (profileRoot) return profileRoot.id;
    const sorted = [...suitableRoots].sort(
      (a, b) => Number(a.isNsfw) - Number(b.isNsfw) || (a.label || a.path).localeCompare(b.label || b.path),
    );
    return sorted[0]?.id ?? null;
  }

  function selectProfile(value: string) {
    profileId = value || null;
    // Profile first: the chosen profile re-targets the library to its default; the user can still
    // change the library manually afterwards — the override just never outlives a profile switch.
    targetLibraryRootId = defaultRootFor(kindProfiles.find((profile) => profile.id === profileId) ?? null);
  }

  onMount(async () => {
    try {
      [roots, profiles] = await Promise.all([fetchLibraryRoots(), fetchAcquisitionProfiles()]);
      const defaultProfile = kindProfiles.find((profile) => profile.isDefault) ?? kindProfiles[0] ?? null;
      if (!profileId && defaultProfile) {
        profileId = defaultProfile.id;
      }
      if (!targetLibraryRootId) {
        targetLibraryRootId = defaultRootFor(kindProfiles.find((profile) => profile.id === profileId) ?? defaultProfile);
      }
    } finally {
      loaded = true;
    }
  });
</script>

<!-- Rendered from first paint (selects fill in as the lookups land) so the page never jumps. -->
<div class="flex flex-wrap items-end gap-3">
  <label class="min-w-44 flex-1 space-y-1 sm:max-w-64">
    <span class="text-label flex items-center gap-1.5 text-text-muted">
      <SlidersHorizontal class="h-3.5 w-3.5" /> Quality profile
    </span>
    {#if !loaded || profileOptions.length > 0}
      <Select
        size="sm"
        disabled={!loaded}
        value={profileId ?? ""}
        options={profileOptions}
        onchange={selectProfile}
      />
    {:else}
      <p class="text-[0.72rem] leading-relaxed text-text-muted">
        No {profileNoun} profile yet — permissive defaults apply (Settings → Acquisition).
      </p>
    {/if}
  </label>

  <label class="min-w-44 flex-1 space-y-1 sm:max-w-64">
    <span class="text-label flex items-center gap-1.5 text-text-muted">
      <FolderOpen class="h-3.5 w-3.5" /> Import into
    </span>
    {#if !loaded || rootOptions.length > 0}
      <Select
        size="sm"
        disabled={!loaded}
        value={targetLibraryRootId ?? ""}
        options={rootOptions}
        onchange={(value) => (targetLibraryRootId = value || null)}
      />
    {:else}
      <p class="text-[0.72rem] leading-relaxed text-error-text">
        No enabled library supports {kindInfo.plural.toLowerCase()} — add one in Settings → Libraries first.
      </p>
    {/if}
  </label>

  {#if actions}
    <div class="ml-auto flex items-end">
      {@render actions()}
    </div>
  {/if}
</div>
