<script lang="ts">
  import { cn, Toggle } from "@prismedia/ui-svelte";

  interface Props {
    checked: boolean;
    label: string;
    description: string;
    class?: string;
    onChange: (checked: boolean) => void;
  }

  let { checked, label, description, class: className, onChange }: Props = $props();
</script>

<button
  type="button"
  onclick={() => onChange(!checked)}
  class={cn(
    "surface-card no-lift flex h-full min-h-[104px] flex-col justify-between p-3.5 text-left transition-all duration-normal group",
    checked ? "border-border-accent/40 bg-surface-2/30" : "opacity-85 hover:opacity-100",
    className,
  )}
>
  <div class="flex items-start justify-between gap-3 mb-2 w-full">
    <p class={cn("text-sm font-medium transition-colors", checked ? "text-text-primary" : "text-text-secondary")}>
      {label}
    </p>
    <Toggle {checked} />
  </div>
  <p class="text-[0.72rem] text-text-muted leading-relaxed">{description}</p>
</button>
