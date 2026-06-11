<script lang="ts">
  import { Users } from "@lucide/svelte";
  import type { RequestCastMember } from "$lib/requests/request-model";

  /**
   * Horizontal strip of cast members for an external request detail: headshot,
   * name, and role, hydrated from the metadata catalog. External items have no
   * library person entities, so the cards are display-only.
   */
  interface Props {
    cast: RequestCastMember[];
  }

  const { cast }: Props = $props();
</script>

<div class="cast-strip" role="list">
  {#each cast as member (member.name + (member.role ?? ""))}
    <div class="cast-card" role="listitem">
      <div class="cast-photo">
        {#if member.imageUrl}
          <img src={member.imageUrl} alt={member.name} loading="lazy" />
        {:else}
          <Users class="cast-placeholder" aria-hidden="true" />
        {/if}
      </div>
      <p class="cast-name" title={member.name}>{member.name}</p>
      {#if member.role}
        <p class="cast-role" title={member.role}>{member.role}</p>
      {/if}
    </div>
  {/each}
</div>

<style>
  .cast-strip {
    display: flex;
    gap: 0.75rem;
    padding-bottom: 0.5rem;
    overflow-x: auto;
    scrollbar-width: thin;
  }

  .cast-card {
    flex: 0 0 auto;
    width: 92px;
    min-width: 0;
  }

  .cast-photo {
    position: relative;
    display: grid;
    place-items: center;
    aspect-ratio: 2 / 3;
    overflow: hidden;
    border: 1px solid var(--color-border-subtle, rgb(255 255 255 / 0.08));
    border-radius: var(--radius-sm, 6px);
    background: linear-gradient(135deg, rgb(15 16 18 / 0.96), rgb(28 25 20 / 0.92));
  }

  .cast-photo img {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  .cast-photo :global(.cast-placeholder) {
    width: 32%;
    height: 32%;
    color: rgb(244 239 230 / 0.18);
  }

  .cast-name {
    margin: 0.35rem 0 0;
    overflow: hidden;
    font-size: 0.7rem;
    font-weight: 500;
    line-height: 1.25;
    color: var(--color-text-secondary, #c8ccd4);
    display: -webkit-box;
    -webkit-box-orient: vertical;
    -webkit-line-clamp: 2;
    line-clamp: 2;
  }

  .cast-role {
    margin: 0.1rem 0 0;
    overflow: hidden;
    font-size: 0.64rem;
    line-height: 1.2;
    color: var(--color-text-muted, #a4acb9);
    display: -webkit-box;
    -webkit-box-orient: vertical;
    -webkit-line-clamp: 2;
    line-clamp: 2;
  }
</style>
