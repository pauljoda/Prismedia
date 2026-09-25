<script lang="ts">
  import type { PluginProvider } from "$lib/api/generated/model";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import BandStrip from "./BandStrip.svelte";
  import { pluginLights } from "./plugin-light";

  interface Props {
    plugin: PluginProvider;
  }

  let { plugin }: Props = $props();

  const lights = $derived(pluginLights(plugin));
  const bands = $derived(lights.map((light) => ({ key: light.family.key, label: light.family.label, accent: light.family.accent })));
  const operations = $derived([...new Set(lights.flatMap((light) => light.operations))]);
</script>

<!-- A community plugin not yet installed: the light it would add, at a glance. -->
<li class="flex min-w-0 items-center gap-3 bg-[var(--color-surface-1)] px-3 py-2.5">
  <PluginIcon name={plugin.name} iconUrl={plugin.iconUrl} class="size-9 opacity-80" />
  <div class="min-w-0 flex-1">
    <div class="flex min-w-0 flex-wrap items-baseline gap-x-2">
      <p class="truncate text-label font-medium text-text-primary">{plugin.name}</p>
      <span class="font-mono text-[0.64rem] text-text-disabled">v{plugin.version}</span>
      {#if plugin.auth.length > 0}
        <span class="font-mono text-[0.6rem] uppercase tracking-[0.1em] text-text-disabled">key</span>
      {/if}
    </div>
    <p class="truncate text-caption text-text-muted">
      {lights.map((light) => light.family.label).join(", ") || "—"}
      {#if operations.length > 0}<span class="text-text-disabled"> — {operations.join(" · ")}</span>{/if}
    </p>
  </div>
  <BandStrip {bands} height={4} label="Would add" class="hidden w-24 shrink-0 sm:flex" />
</li>
