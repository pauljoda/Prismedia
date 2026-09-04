<script lang="ts">
  import { cn, SearchableSelect } from "@prismedia/ui-svelte";
  import type { PluginProvider } from "$lib/api/identify-types";

  interface Props {
    providers: PluginProvider[];
    selectedId: string;
    onChange: (providerId: string) => void;
    label?: string;
    compact?: boolean;
    class?: string;
  }
  let { providers, selectedId, onChange, label = "Provider", compact = false, class: className }: Props = $props();
  const selectedProvider = $derived(providers.find((provider) => provider.id === selectedId) ?? providers[0]);
  const options = $derived(providers.map((provider) => ({
    value: provider.id, label: provider.name, description: provider.id,
  })));
</script>

<div class={cn("w-full", compact ? "max-w-68" : "max-w-88", className)}>
  <SearchableSelect {options} value={selectedProvider?.id} {label}
    searchLabel="Search providers" placeholder="Select provider" emptyText="No providers found" onchange={onChange} />
</div>
