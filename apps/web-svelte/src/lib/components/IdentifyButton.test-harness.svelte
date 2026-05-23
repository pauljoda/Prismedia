<script lang="ts">
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import IdentifyButton from "./IdentifyButton.svelte";

  interface Props {
    entityKind: "video_series" | "video_movie" | "video_episode" | "book";
    entityId: string;
    title: string;
    label?: string;
  }

  let { entityKind, entityId, title, label }: Props = $props();

  provideNsfw(() => ({ initialMode: "show", lanAutoEnable: false }));
</script>

<IdentifyButton {entityKind} {entityId} {title} {label} />
