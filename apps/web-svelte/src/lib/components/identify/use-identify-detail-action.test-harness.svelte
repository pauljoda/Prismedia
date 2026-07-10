<script lang="ts">
  import type { EntityCapability, EntityKind } from "$lib/api/generated/model";
  import { useIdentifyDetailAction } from "./use-identify-detail-action.svelte";

  interface Props {
    entityId: string;
    entityKind: EntityKind;
    capabilities: EntityCapability[];
    hasSourceMedia: boolean;
  }

  let { entityId, entityKind, capabilities, hasSourceMedia }: Props = $props();

  const identifyAction = useIdentifyDetailAction(() => entityId && entityKind
    ? { id: entityId, kind: entityKind, capabilities, hasSourceMedia }
    : null);
</script>

{#if identifyAction.action}
  <button
    type="button"
    class:active={identifyAction.action.active}
    disabled={identifyAction.action.disabled}
    onclick={identifyAction.action.onClick}
    title={identifyAction.action.title}
    aria-label={identifyAction.action.ariaLabel}
  >
    {identifyAction.action.label}
  </button>
{/if}
