<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { METADATA_PATCH_FIELD } from "$lib/entities/entity-codes";
  import type { EntityDetailSection, EntityDetailTab } from "./entity-detail-types";

  const EDIT_QUERY_KEY = "edit";

  interface Props {
    canEdit: boolean;
    hasTabs: boolean;
    visibleTabs: EntityDetailTab[];
    standaloneEditSections: EntityDetailSection[];
    onEditTab: (tab: EntityDetailTab) => void;
    onEditStandalone: () => void;
  }

  let {
    canEdit,
    hasTabs,
    visibleTabs,
    standaloneEditSections,
    onEditTab,
    onEditStandalone,
  }: Props = $props();

  let handledRequest = $state("");

  $effect(() => {
    const requestKey = `${page.url.pathname}${page.url.search}`;
    if (page.url.searchParams.get(EDIT_QUERY_KEY) !== METADATA_PATCH_FIELD.dates || handledRequest === requestKey || !canEdit) return;

    const dateTab = visibleTabs.find((tab) => tab.sections.includes(METADATA_PATCH_FIELD.dates));
    if (hasTabs && dateTab) {
      onEditTab(dateTab);
    } else if (!hasTabs && standaloneEditSections.some((section) => section.id === METADATA_PATCH_FIELD.dates)) {
      onEditStandalone();
    } else {
      return;
    }

    handledRequest = requestKey;
    queueMicrotask(() => document.getElementById("entity-dates-editor")?.scrollIntoView({ block: "center" }));

    const cleaned = new URL(page.url);
    cleaned.searchParams.delete(EDIT_QUERY_KEY);
    void goto(`${cleaned.pathname}${cleaned.search}${cleaned.hash}`, {
      replaceState: true,
      noScroll: true,
      keepFocus: true,
    });
  });
</script>
