<script lang="ts">
  import { Badge } from "@prismedia/ui-svelte";
  import type { PluginIntegrationDefinition } from "$lib/api/generated/model";
  import { capabilityLabels } from "$lib/integrations/connection-labels";
  let { integration }: { integration?: PluginIntegrationDefinition | null } = $props();
</script>

{#if integration}
  <div class="mt-2 flex flex-wrap gap-1.5" aria-label="Integration capabilities">
    {#each integration.capabilities as capability (capability.kind)}<Badge>{capabilityLabels[capability.kind]}</Badge>{/each}
  </div>
{/if}
