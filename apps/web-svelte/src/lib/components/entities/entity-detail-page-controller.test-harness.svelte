<script lang="ts">
  import type { EntityCapability } from "$lib/api/generated/model";
  import type { EntityMetadataUpdateRequest } from "$lib/api/entity-mutations";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import { provideAppChrome } from "$lib/stores/app-chrome.svelte";
  import {
    useEntityDetailPage,
    type EntityDetailPageLoadContext,
    type EntityDetailPageMutations,
  } from "./entity-detail-page-controller.svelte";

  export interface TestDetailEntity {
    capabilities: EntityCapability[];
    id: string;
    kind: string;
    title: string;
  }

  interface Props {
    load: (context: EntityDetailPageLoadContext) => Promise<TestDetailEntity>;
    loadKey?: string;
    mutations?: Partial<EntityDetailPageMutations>;
  }

  let { load, loadKey = "entity-1", mutations }: Props = $props();

  const nsfw = provideNsfw(() => ({ initialMode: "off", allowed: true }));
  const chrome = provideAppChrome(() => false);
  // Test dependencies are fixed for the lifetime of each rendered harness.
  // svelte-ignore state_referenced_locally
  const fixedMutations = mutations;
  const detail = useEntityDetailPage<TestDetailEntity>({
    breadcrumbs: (entity) => [
      { label: "Entities", href: "/entities" },
      { label: entity.title },
    ],
    load: (context) => load(context),
    loadKey: () => loadKey,
    mutations: fixedMutations,
  });

  const metadataRequest: EntityMetadataUpdateRequest = {
    fields: ["title"],
    patch: {
      title: "Updated title",
      externalIds: {},
      urls: [],
      tags: [],
      credits: [],
      dates: {},
      stats: {},
      positions: {},
    },
  };
</script>

<p data-testid="load-state">{detail.loadState}</p>
<p data-testid="error-message">{detail.errorMessage ?? ""}</p>
<p data-testid="entity-title">{detail.entity?.title ?? ""}</p>
<p data-testid="breadcrumbs">{chrome.breadcrumbs.map((item) => item.label).join(" / ")}</p>
<button type="button" onclick={() => nsfw.setMode("show")}>Show NSFW</button>
<button type="button" onclick={() => void detail.retry()}>Retry</button>
<button type="button" onclick={() => void detail.changeRating(4)}>Rate</button>
<button type="button" onclick={() => void detail.saveMetadata(metadataRequest)}>Save metadata</button>
