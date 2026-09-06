<script lang="ts">
  import { untrack } from "svelte";
  import EntityPicker, { type EntityPickerItem } from "$lib/components/forms/EntityPicker.svelte";
  import { fetchEntityThumbnails } from "$lib/api/entities";
  import { searchEntityPickerItems } from "$lib/entities/entity-picker-search";
  import { collectionRuleReferences, isSeriesReferenceId } from "$lib/collections/rule-references";
  import type { CollectionConditionValue } from "$lib/collections/models";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  interface Props {
    field: string;
    value: CollectionConditionValue;
    multiple: boolean;
    onChange: (value: CollectionConditionValue) => void;
    disabled?: boolean;
  }
  let { field, value, multiple, onChange, disabled = false }: Props = $props();
  const nsfw = useNsfw();
  const reference = $derived(collectionRuleReferences[field]);
  const wireValues = $derived(Array.isArray(value) ? value.map(String) : typeof value === "string" && value ? [value] : []);
  let known = $state.raw(new Map<string, EntityPickerItem>());
  let detailError = $state(false);
  const selected = $derived(wireValues.map(id => known.get(id) ?? {
    id, title: reference.useIds && isSeriesReferenceId(id) ? "Saved series" : id, thumbnailUrl: null,
  }));

  // Saved rules keep their original wire values. Resolve only display details, never rewrite references on load.
  $effect(() => {
    const ids = reference.useIds ? wireValues.filter(id => isSeriesReferenceId(id) && !untrack(() => known.has(id))) : [];
    const hideNsfw = nsfw.mode !== "show";
    detailError = false;
    if (!ids.length) return;
    const controller = new AbortController();
    void fetchEntityThumbnails(ids, { hideNsfw, signal: controller.signal }).then(items => {
      if (controller.signal.aborted) return;
      const byId = new Map(items.filter(item => item.kind === reference.kind).map(item => [item.id.toLowerCase(), item]));
      const details = ids.flatMap(id => {
        const item = byId.get(id.toLowerCase());
        return item ? [{ id, title: item.title, thumbnailUrl: item.coverThumbUrl ?? item.coverUrl ?? null }] : [];
      });
      known = new Map([...known, ...details.map(item => [item.id, item] as const)]);
      detailError = details.length !== ids.length;
    }).catch(() => { if (!controller.signal.aborted) detailError = true; });
    return () => controller.abort();
  });

  async function search(query: string): Promise<EntityPickerItem[]> {
    const target = reference;
    const results = await searchEntityPickerItems(target.kind, query, { hideNsfw: nsfw.mode !== "show" });
    // Name-based rules intentionally match all Entities sharing that exact name.
    return [...new Map(results.map(item => {
      const id = target.useIds ? item.id : item.title;
      return [id, { ...item, id }] as const;
    })).values()];
  }

  function change(items: EntityPickerItem[]) {
    const choices = items.map(item => ({
      ...item,
      // Only existing Entity IDs use identity matching. Typed future names remain name predicates.
      id: reference.useIds && isSeriesReferenceId(item.id) ? item.id : item.title,
    }));
    known = new Map([...known, ...choices.map(item => [item.id, item] as const)]);
    const next = choices.map(item => item.id);
    onChange(multiple ? next : next[0] ?? "");
  }
</script>

<EntityPicker label={reference.label} values={selected} onSearch={search} onChange={change}
  mode={multiple ? "multi" : "single"} {disabled} canAddNew addNewLabel="name"
  placeholder={multiple && selected.length ? "Add another…" : "Select or enter a name…"}
  helper={detailError ? "Some saved series are unavailable. Their rules are preserved." : undefined} />
