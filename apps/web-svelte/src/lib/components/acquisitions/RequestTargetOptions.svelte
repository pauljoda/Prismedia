<script lang="ts">
  /**
   * The request-time questions asked before committing: which library the acquired files import into
   * and which quality profile scores the release search. Options are filtered to the request kind
   * (video libraries for a movie, the movie profiles, …) and default to the kind's default profile and
   * its target library, so accepting the defaults is one glance — but nothing is assumed silently.
   * The selections bind out and ride the commit; the server degrades gracefully if they go stale.
   */
  import { onMount } from "svelte";
  import { FolderOpen, SlidersHorizontal } from "@lucide/svelte";
  import { Select } from "@prismedia/ui-svelte";
  import { fetchAcquisitionProfiles } from "$lib/api/acquisitions";
  import { fetchLibraryRoots, type LibraryRoot } from "$lib/api/settings";
  import type { BookAcquisitionProfileView } from "$lib/api/generated/model";
  import type { RequestKindInfo } from "$lib/requests/request-helpers";

  interface Props {
    kindInfo: RequestKindInfo;
    targetLibraryRootId: string | null;
    profileId: string | null;
  }
  let { kindInfo, targetLibraryRootId = $bindable(), profileId = $bindable() }: Props = $props();

  let roots = $state<LibraryRoot[]>([]);
  let profiles = $state<BookAcquisitionProfileView[]>([]);
  let loaded = $state(false);

  const suitableRoots = $derived(
    roots.filter((root) => root.enabled && kindInfo.rootFlag !== null && root[kindInfo.rootFlag]),
  );
  /** Profiles are named by what they govern (a "music" profile covers an artist's album requests). */
  const profileNoun = $derived(
    kindInfo.rootFlag === "scanVideos" ? "movie" : kindInfo.rootFlag === "scanAudio" ? "music" : "book",
  );
  const kindProfiles = $derived(profiles.filter((profile) => profile.kind === kindInfo.profileKind));
  const rootOptions = $derived(suitableRoots.map((root) => ({ value: root.id, label: root.label || root.path })));
  const profileOptions = $derived(kindProfiles.map((profile) => ({ value: profile.id, label: profile.displayName })));

  onMount(async () => {
    try {
      [roots, profiles] = await Promise.all([fetchLibraryRoots(), fetchAcquisitionProfiles()]);
      const defaultProfile = kindProfiles.find((profile) => profile.isDefault) ?? kindProfiles[0] ?? null;
      if (!profileId && defaultProfile) {
        profileId = defaultProfile.id;
      }

      if (!targetLibraryRootId) {
        // Prefer the chosen profile's import target when it suits the kind; else the first regular
        // (non-NSFW-first) suitable library — mirroring the server's fallback order.
        const sorted = [...suitableRoots].sort(
          (a, b) => Number(a.isNsfw) - Number(b.isNsfw) || (a.label || a.path).localeCompare(b.label || b.path),
        );
        const profileRoot = suitableRoots.find((root) => root.id === defaultProfile?.targetLibraryRootId);
        targetLibraryRootId = (profileRoot ?? sorted[0])?.id ?? null;
      }
    } finally {
      loaded = true;
    }
  });
</script>

{#if loaded}
  <div class="flex flex-wrap items-end gap-3">
    <label class="min-w-44 flex-1 space-y-1 sm:max-w-64">
      <span class="text-label flex items-center gap-1.5 text-text-muted">
        <FolderOpen class="h-3.5 w-3.5" /> Import into
      </span>
      {#if rootOptions.length > 0}
        <Select
          size="sm"
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

    <label class="min-w-44 flex-1 space-y-1 sm:max-w-64">
      <span class="text-label flex items-center gap-1.5 text-text-muted">
        <SlidersHorizontal class="h-3.5 w-3.5" /> Quality profile
      </span>
      {#if profileOptions.length > 0}
        <Select
          size="sm"
          value={profileId ?? ""}
          options={profileOptions}
          onchange={(value) => (profileId = value || null)}
        />
      {:else}
        <p class="text-[0.72rem] leading-relaxed text-text-muted">
          No {profileNoun} profile yet — permissive defaults apply (Settings → Acquisition).
        </p>
      {/if}
    </label>
  </div>
{/if}
