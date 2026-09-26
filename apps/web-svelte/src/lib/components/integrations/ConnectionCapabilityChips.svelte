<script lang="ts">
  import { Badge } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS } from "$lib/api/generated/codes";
  import type { ConnectionResponse } from "$lib/api/generated/model";
  import { entityKindIcon } from "$lib/entities/entity-kind-icons";
  import { summarizeConnectionCapabilities } from "$lib/integrations/connection-capability-chips";

  interface Props {
    connection: ConnectionResponse;
  }

  let { connection }: Props = $props();
  const summary = $derived(summarizeConnectionCapabilities(connection));
  const unavailable = $derived(connection.status !== CONNECTION_STATUS.ready);
</script>

<span class="connection-capability-chips flex max-w-full flex-wrap items-center gap-1" data-testid="connection-capability-chips">
  {#if unavailable}
    <Badge variant="warning" title="This source is unavailable">Unavailable</Badge>
  {/if}
  {#each summary.capabilities as capability (capability.key)}
    <Badge variant="secondary" title={capability.title}>{capability.label}</Badge>
  {/each}
  {#each summary.entityKinds as kind (kind.code)}
    {@const Icon = entityKindIcon(kind.code)}
    <Badge variant="outline" title={kind.label}>
      <Icon data-icon="inline-start" class="text-text-disabled" aria-hidden="true" />{kind.label}
    </Badge>
  {/each}
</span>
