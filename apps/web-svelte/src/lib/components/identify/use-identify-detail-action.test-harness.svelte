<script lang="ts">
  import type { EntityCapability } from "$lib/api/generated/model";
  import { useIdentifyDetailAction } from "./use-identify-detail-action.svelte";

  interface Props {
    entityId: string;
    entityKind: string;
    capabilities: EntityCapability[];
  }

  let { entityId, entityKind, capabilities }: Props = $props();

  const identifyAction = useIdentifyDetailAction(
    () => entityId,
    () => entityKind,
    () => capabilities,
  );
</script>

{#if identifyAction.action}
  <button
    type="button"
    class:active={identifyAction.action.active}
    onclick={identifyAction.action.onClick}
    title={identifyAction.action.title}
    aria-label={identifyAction.action.ariaLabel}
  >
    {identifyAction.action.label}
  </button>
{/if}
