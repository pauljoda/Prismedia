<script lang="ts">
  import type { Snippet } from "svelte";
  import * as Base from "../components/ui/dialog";
  import { cn } from "../lib/utils";
  interface Props {
    open: boolean;
    ariaLabel: string;
    onClose: () => void;
    initialFocus?: () => HTMLElement | null;
    dismissible?: boolean;
    class?: string;
    children: Snippet;
  }
  let { open, ariaLabel, onClose, initialFocus, dismissible = true, class: className, children }: Props = $props();
</script>

<Base.Root {open} onOpenChange={(next) => { if (!next && dismissible) onClose(); }}>
  <!-- Mount portals in opening order so independent dialogs (global search,
       editors) paint in the same order as Bits UI's focus and Escape stack. -->
  {#if open}
  <Base.Content showCloseButton={false}
    escapeKeydownBehavior={dismissible ? "close" : "ignore"}
    interactOutsideBehavior={dismissible ? "close" : "ignore"}
    onOpenAutoFocus={(event) => { const target = initialFocus?.(); if (target) { event.preventDefault(); target.focus(); } }}
    class={cn("flex max-h-[calc(100dvh-2rem)] max-w-[calc(100vw-2rem)] flex-col gap-0 overflow-auto p-0 sm:max-w-[calc(100vw-2rem)]", className)}>
    <Base.Title class="sr-only">{ariaLabel}</Base.Title>
    {@render children()}
  </Base.Content>
  {/if}
</Base.Root>
