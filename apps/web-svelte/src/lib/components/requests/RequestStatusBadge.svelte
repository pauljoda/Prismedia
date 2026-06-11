<script lang="ts">
  import {
    Check,
    CircleDashed,
    Clock3,
    Download,
    HelpCircle,
    Send,
    Trash2,
  } from "@lucide/svelte";
  import { Badge, type BadgeVariant } from "@prismedia/ui-svelte";
  import { REQUEST_HISTORY_STATUS } from "$lib/api/generated/codes";
  import type { RequestHistoryStatusCode } from "$lib/api/generated/codes";
  import type { Component } from "svelte";

  /** Colored status chip for a request history entry's last known upstream state. */
  interface Props {
    status: RequestHistoryStatusCode;
  }

  let { status }: Props = $props();

  const meta: Record<string, { label: string; variant: BadgeVariant; icon: Component }> = {
    [REQUEST_HISTORY_STATUS.submitted]: { label: "Submitted", variant: "default", icon: Send },
    [REQUEST_HISTORY_STATUS.pending]: { label: "Pending", variant: "default", icon: Clock3 },
    [REQUEST_HISTORY_STATUS.downloading]: { label: "Downloading", variant: "info", icon: Download },
    [REQUEST_HISTORY_STATUS.partial]: { label: "Partial", variant: "warning", icon: CircleDashed },
    [REQUEST_HISTORY_STATUS.available]: { label: "Available", variant: "success", icon: Check },
    [REQUEST_HISTORY_STATUS.removed]: { label: "Removed", variant: "error", icon: Trash2 },
    [REQUEST_HISTORY_STATUS.unknown]: { label: "Unknown", variant: "default", icon: HelpCircle },
  };

  const entry = $derived(meta[status] ?? meta[REQUEST_HISTORY_STATUS.unknown]);
  const Icon = $derived(entry.icon);
  const downloading = $derived(status === REQUEST_HISTORY_STATUS.downloading);
</script>

<Badge variant={entry.variant} class="gap-1.5 whitespace-nowrap">
  <Icon class={`h-3 w-3 ${downloading ? "animate-pulse" : ""}`} aria-hidden="true" />
  {entry.label}
</Badge>
