<script lang="ts">
  import { Button, Dialog, Select, Toggle, type SelectOption } from "@prismedia/ui-svelte";

  const options: SelectOption[] = [
    { value: "original", label: "Original source" },
    { value: "unavailable", label: "Unavailable source", disabled: true },
    { value: "mapped", label: "Mapped source", annotation: "Mapped" },
    { value: "long", label: "A longer source filename with chapter and edition information.mkv" },
    ...Array.from({ length: 12 }, (_, index) => ({ value: `sample-${index}`, label: `Sample source ${index + 1}` })),
  ];
  let selected = $state("original");
  let enabled = $state(true);
  let open = $state(false);
  const id = $props.id();
</script>

<section id="component-bases" aria-labelledby={`${id}-heading`} class="scroll-mt-20 rounded-lg border border-border-default bg-surface-1 p-5 sm:p-7">
  <div class="flex flex-wrap items-start justify-between gap-4">
    <div class="max-w-xl">
      <p class="text-kicker text-text-muted">SHARED FOUNDATIONS</p>
      <h2 id={`${id}-heading`} class="mt-2 font-heading text-xl font-medium tracking-tight">Prismedia, on shadcn-svelte</h2>
      <p class="mt-2 text-sm leading-relaxed text-text-secondary">Our controls and visual language, built on accessible selection and switch primitives. This preview does not save settings.</p>
    </div>
    <Button variant="secondary" onclick={() => { open = true; }}>Test inside a dialog</Button>
  </div>
  <div class="mt-6 grid items-start gap-6 border-t border-border-subtle pt-6 sm:grid-cols-2">
    <div class="min-w-0 space-y-2">
      <p class="text-sm font-medium">Source selection</p>
      <Select {options} bind:value={selected} ariaLabel="Preview source" />
      <p class="text-xs leading-relaxed text-text-muted">Try arrow keys, type a source name, or press Escape. Mapped choices remain selectable.</p>
    </div>
    <label for={`${id}-switch`} class="flex min-h-11 cursor-pointer items-start justify-between gap-4">
      <div>
        <span class="text-sm font-medium">Show source details</span>
        <p class="mt-1 text-xs leading-relaxed text-text-muted">A neutral switch with one label, a visible focus ring, and a generous hit area.</p>
      </div>
      <Toggle id={`${id}-switch`} ariaLabel="Show source details" checked={enabled} onchange={(next) => { enabled = next; }} class="mt-1" />
    </label>
  </div>
</section>

<Dialog {open} ariaLabel="Control preview" onClose={() => { open = false; }} class="w-[28rem]">
  <div class="space-y-5 p-6">
    <div>
      <h2 class="font-heading text-lg font-medium">Select inside a dialog</h2>
      <p class="mt-2 text-sm leading-relaxed text-text-secondary">The menu remains interactive above the dialog. Escape closes the menu first, then the dialog.</p>
    </div>
    <Select {options} bind:value={selected} ariaLabel="Dialog source" />
    <div class="flex justify-end">
      <Button variant="secondary" onclick={() => { open = false; }}>Done</Button>
    </div>
  </div>
</Dialog>
