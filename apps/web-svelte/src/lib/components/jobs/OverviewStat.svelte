<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import type { LucideIcon } from "@lucide/svelte";

  interface Props {
    icon: LucideIcon;
    label: string;
    value: number | string;
    detail: string;
    accent?: boolean;
    danger?: boolean;
  }

  let { icon: Icon, label, value, detail, accent = false, danger = false }: Props = $props();
</script>

<div
  class={cn(
    "surface-panel relative overflow-hidden px-3 py-2.5 flex flex-col justify-between min-h-[72px]",
    accent && !danger && "border-border-accent shadow-[var(--shadow-glow-accent)]",
    danger && "border-status-error/30 shadow-[0_0_12px_rgba(179,79,86,0.15)]",
  )}
>
  <div
    class={cn(
      "absolute left-0 top-0 bottom-0 w-[3px] opacity-90",
      danger ? "bg-status-error" : accent ? "bg-accent-500" : "bg-surface-4",
    )}
  ></div>
  <div class="flex items-center justify-between ml-1.5">
    <span
      class={cn(
        "text-[0.6rem] font-semibold tracking-[0.15em] uppercase",
        danger ? "text-status-error-text" : "text-text-muted",
      )}
    >
      {label}
    </span>
    <div
      class={cn(
        "opacity-70",
        danger ? "text-status-error-text" : accent ? "text-text-accent" : "text-text-disabled",
      )}
    >
      <Icon class="h-3.5 w-3.5" />
    </div>
  </div>
  <div class="flex items-end justify-between ml-1.5 mt-1">
    <div
      class={cn(
        "text-lg font-mono tracking-tight",
        danger
          ? "text-status-error-text"
          : accent
            ? "text-glow-accent"
            : "text-text-primary",
      )}
    >
      {value}
    </div>
    <div
      class="text-[0.6rem] text-text-disabled mb-0.5 truncate max-w-[60%] text-right"
      title={detail}
    >
      {detail}
    </div>
  </div>
</div>
