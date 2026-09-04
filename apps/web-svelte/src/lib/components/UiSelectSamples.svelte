<script lang="ts">
  import { Panel, Select, type SelectOption, type SelectSize } from "@prismedia/ui-svelte";

  const options: SelectOption[] = [
    { value: "option-a", label: "Option A" },
    { value: "option-b", label: "Option B" },
    { value: "option-c", label: "Option C" },
    { value: "option-d", label: "Option D (disabled)", disabled: true },
  ];
  const sizes: { value: SelectSize; label: string }[] = [
    { value: "sm", label: "Small" },
    { value: "md", label: "Medium" },
    { value: "lg", label: "Large" },
  ];
  let value = $state("");
</script>

<div class="space-y-4">
  <h3 class="text-kicker">DROPDOWN</h3>
  <Panel>
    <div class="space-y-4 p-5">
      <div>
        <p class="mb-1 text-kicker">Default</p>
        <Select {options} bind:value ariaLabel="Default dropdown" />
      </div>
      <div class="grid gap-3 sm:grid-cols-3">
        {#each sizes as size (size.value)}
          <div>
            <p class="mb-1 text-kicker">{size.label}</p>
            <Select {options} size={size.value} placeholder={size.label} ariaLabel={`${size.label} dropdown`} />
          </div>
        {/each}
      </div>
      <div>
        <p class="mb-1 text-kicker">Disabled</p>
        <Select {options} disabled value="option-a" ariaLabel="Disabled dropdown" />
      </div>
    </div>
  </Panel>
</div>
