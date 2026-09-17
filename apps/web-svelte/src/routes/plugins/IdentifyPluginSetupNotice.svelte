<script lang="ts">
  import { Alert, buttonVariants } from "@prismedia/ui-svelte";
  import { ScanSearch } from "@lucide/svelte";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import type { EntityKind } from "$lib/api/generated/model";

  let { entityId, entityKind, ready }: {
    entityId: string;
    entityKind: EntityKind;
    ready: boolean;
  } = $props();
</script>

<Alert.Root role="status">
  <ScanSearch />
  <Alert.Title>{ready ? "Metadata provider ready" : `Set up metadata for ${labelForEntityKind(entityKind)}`}</Alert.Title>
  <Alert.Description>
    <p>{ready
      ? "Return to your item to search for a match and review its metadata."
      : "Install a plugin that supports this media type and configure any required credentials. Then return to your item to search for a match."}</p>
    <div class="mt-3">
      <a
        class={buttonVariants({ variant: "secondary", size: "sm" })}
        href={`/identify/${encodeURIComponent(entityId)}?${new URLSearchParams({ returnId: entityId })}`}
      >{ready ? "Continue Identify" : "Return to Identify"}</a>
    </div>
  </Alert.Description>
</Alert.Root>
