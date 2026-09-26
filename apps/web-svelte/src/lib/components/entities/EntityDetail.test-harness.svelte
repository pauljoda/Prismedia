<script lang="ts">
  import { untrack, type ComponentProps } from "svelte";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import EntityDetail from "./EntityDetail.svelte";
  import { provideSession } from "$lib/stores/session.svelte";
  import { USER_ROLE } from "$lib/api/generated/codes";

  let { admin = false, ...props }: ComponentProps<typeof EntityDetail> & { admin?: boolean } = $props();

  provideNsfw(() => ({ initialMode: "show", allowed: true }));
  provideSession({
    user: untrack(() => admin) ? {
      id: "admin-user",
      username: "admin",
      displayName: "Administrator",
      role: USER_ROLE.admin,
      allowNsfw: true,
      canCreateLibraries: true,
      canRequestContent: true,
      enabled: true,
      lastLoginAt: null,
      createdAt: "2026-01-01T00:00:00Z",
      updatedAt: "2026-01-01T00:00:00Z",
      libraryRootIds: null,
    } : null,
    needsSetup: false,
  });
</script>

<EntityDetail {...props} />
