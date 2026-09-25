<script lang="ts">
  import { ArrowRight } from "@lucide/svelte";
  import { StatusLed, buttonVariants, cn } from "@prismedia/ui-svelte";
  import type { ConnectionResponse, PluginProvider } from "$lib/api/generated/model";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import { connectionStatusLabels } from "$lib/integrations/connection-labels";
  import { CONNECTION_STATUS } from "$lib/api/generated/codes";
  import { bandGradient } from "./media-families";
  import { pluginBeam, pluginHealth, pluginLights } from "./plugin-light";

  interface Props {
    plugin: PluginProvider;
    /** Connections backed by this plugin. */
    connections: ConnectionResponse[];
  }

  let { plugin, connections }: Props = $props();

  const lights = $derived(pluginLights(plugin));
  const health = $derived(pluginHealth(plugin, connections));
  const beam = $derived(pluginBeam(lights));
  const action = $derived.by(() => {
    if (plugin.missingAuthKeys.length > 0) return { label: "Add keys", href: "/plugins" };
    if (plugin.integration && connections.length === 0) return { label: "Connect", href: "/settings/connections" };
    if (plugin.integration) return { label: "Connections", href: "/settings/connections" };
    return { label: "Manage", href: "/plugins" };
  });

  function connectionLed(connection: ConnectionResponse) {
    if (!connection.enabled) return "idle" as const;
    if (connection.status === CONNECTION_STATUS.ready) return "phosphor" as const;
    if (connection.status === CONNECTION_STATUS.unverified) return "info" as const;
    return "error" as const;
  }
</script>

<!--
  A plugin as a light source: neutral light enters from the left of its top edge and leaves as the
  bands of media it serves. The rows below name those bands and what the plugin does in each.
-->
<article
  class="relative flex min-w-0 flex-col overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] shadow-[var(--shadow-card)]"
  class:opacity-70={!plugin.enabled}
  aria-label="{plugin.name}, {health.label}"
>
  <span class="absolute inset-x-0 top-0 h-px opacity-80" style:background={beam} aria-hidden="true"></span>

  <header class="flex items-start gap-3 px-4 pt-4">
    <PluginIcon name={plugin.name} iconUrl={plugin.iconUrl} class="size-11" />
    <div class="min-w-0 flex-1">
      <div class="flex min-w-0 flex-wrap items-baseline gap-x-2">
        <h3 class="truncate font-heading text-sm font-semibold text-text-primary">{plugin.name}</h3>
        <span class="font-mono text-[0.68rem] text-text-muted">v{plugin.version}</span>
      </div>
      <p class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-caption text-text-secondary">
        <span class="flex items-center gap-1.5">
          <StatusLed status={health.led} size="sm" />
          {health.label}
        </span>
        {#if plugin.updateAvailable && plugin.availableVersion}
          <span class="flex items-center gap-1.5 text-text-muted">
            <StatusLed status="info" size="sm" />
            v{plugin.availableVersion} available
          </span>
        {/if}
      </p>
    </div>
  </header>

  <ul class="flex flex-1 flex-col gap-2 px-4 pt-4 pb-4" aria-label="Media this plugin serves">
    {#each lights as light (light.family.key)}
      <li class="grid min-w-0 grid-cols-[1.75rem_minmax(0,6.5rem)_minmax(0,1fr)] items-center gap-2.5" title={light.kinds.join(", ")}>
        <span class="h-1.5 rounded-[2px]" style:background={bandGradient(light.family.accent)} aria-hidden="true"></span>
        <span class="truncate text-caption text-text-secondary">{light.family.label}</span>
        <span class="truncate font-mono text-[0.66rem] text-text-muted">{light.operations.join(" · ")}</span>
      </li>
    {:else}
      <li class="font-mono text-caption text-text-disabled">—</li>
    {/each}
  </ul>

  {#if plugin.integration}
    <div class="border-t border-[var(--color-border-subtle)] px-4 py-2.5">
      {#if connections.length === 0}
        <p class="text-caption text-text-muted"><span class="font-mono text-text-secondary">0</span> connections</p>
      {:else}
        <ul class="flex flex-col gap-1" aria-label="Connections using {plugin.name}">
          {#each connections as connection (connection.id)}
            <li class="flex min-w-0 items-center justify-between gap-3 text-caption">
              <span class="truncate text-text-secondary">{connection.name}</span>
              <span class="flex shrink-0 items-center gap-1.5 text-text-muted">
                <StatusLed status={connectionLed(connection)} size="sm" />
                {connectionStatusLabels[connection.status] ?? "Checking"}
              </span>
            </li>
          {/each}
        </ul>
      {/if}
    </div>
  {/if}

  <footer class="flex items-center justify-between gap-3 border-t border-[var(--color-border-subtle)] py-1.5 pr-1.5 pl-4">
    <span class="truncate font-mono text-[0.64rem] text-text-disabled">{plugin.id}</span>
    <a
      href={action.href}
      class={cn(
        buttonVariants({ variant: plugin.missingAuthKeys.length > 0 ? "secondary" : "ghost", size: "sm" }),
        "text-text-secondary hover:text-text-primary",
      )}
    >
      {action.label}
      <ArrowRight aria-hidden="true" />
    </a>
  </footer>
</article>
