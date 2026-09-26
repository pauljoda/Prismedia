<script lang="ts">
  import { untrack } from "svelte";
  import { ENTITY_KIND } from "$lib/api/generated/codes";
  import RequestTargetOptions from "$lib/components/acquisitions/RequestTargetOptions.svelte";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import { REQUEST_KINDS } from "$lib/requests/request-helpers";

  provideNsfw(() => ({ initialMode: "show", allowed: true }));

  let {
    initialTargetLibraryRootId = null,
    initialProfileId = null,
  }: {
    initialTargetLibraryRootId?: string | null;
    initialProfileId?: string | null;
  } = $props();

  const kindInfo = REQUEST_KINDS.find((candidate) => candidate.profileKind === ENTITY_KIND.book)!;
  let targetLibraryRootId = $state(untrack(() => initialTargetLibraryRootId));
  let profileId = $state(untrack(() => initialProfileId));
</script>

<output data-testid="target-root">{targetLibraryRootId ?? "none"}</output>
<output data-testid="profile">{profileId ?? "none"}</output>
<RequestTargetOptions {kindInfo} bind:targetLibraryRootId bind:profileId />
