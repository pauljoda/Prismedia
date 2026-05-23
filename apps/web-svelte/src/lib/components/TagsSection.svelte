<script lang="ts">
  import { Tag as TagIcon } from "@lucide/svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { tagsVisibleInNsfwMode } from "$lib/nsfw/tags";
  import NsfwTagLabel from "./nsfw/NsfwTagLabel.svelte";

  export interface TagEmbed {
    id: string;
    name: string;
    isNsfw: boolean;
  }

  interface Props {
    tags: TagEmbed[];
  }

  let { tags }: Props = $props();

  const nsfw = useNsfw();
  const visible = $derived(tagsVisibleInNsfwMode(tags, nsfw.mode));
</script>

<section>
  <h4 class="text-kicker mb-3 flex items-center gap-2">
    <TagIcon class="h-3.5 w-3.5" />
    Tags
  </h4>
  {#if visible.length === 0}
    <p class="text-text-disabled text-sm">No tags</p>
  {:else}
    <div class="flex flex-wrap gap-1.5">
      {#each visible as tag (tag.id)}
        <a
          href={`/tags/${encodeURIComponent(tag.name)}`}
          class="tag-chip tag-chip-default hover:tag-chip-accent transition-colors cursor-pointer"
        >
          <NsfwTagLabel isNsfw={tag.isNsfw} text={tag.name} />
        </a>
      {/each}
    </div>
  {/if}
</section>
