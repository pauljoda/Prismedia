<script lang="ts">
  import { Card, Select } from "@prismedia/ui-svelte";
  import type { AcquisitionTransferPresentation } from "$lib/requests/acquisition-transfer-presentation";
  import AcquisitionTransferSummary from "./AcquisitionTransferSummary.svelte";

  // Presentation-only samples. This preview never connects to download clients or changes library data.
  const running: AcquisitionTransferPresentation = {
    stage: "Downloading", active: true, percent: 42,
    speed: "8.4 MB/s", eta: "2m", size: "2.10 GB", peers: "8 / 24",
    pieces: Array.from({ length: 48 }, (_, index) => index < 20 ? 2 : index < 24 ? 1 : 0),
  };
  const samples: { label: string; transfer: AcquisitionTransferPresentation | null }[] = [
    { label: "Downloading", transfer: running },
    { label: "Waiting for client progress", transfer: null },
    { label: "Paused", transfer: { ...running, stage: "Paused", active: false, speed: "—", eta: "—" } },
    { label: "Verifying", transfer: { ...running, stage: "Verifying", percent: 100, speed: "—", eta: "—" } },
    { label: "Complete", transfer: { ...running, stage: "Complete", active: false, percent: 100, speed: "—", eta: "—", pieces: Array(48).fill(2) } },
    { label: "Unknown progress", transfer: { ...running, percent: null, speed: "—", eta: "—", pieces: [] } },
    { label: "Client error", transfer: { ...running, stage: "Error", active: false, speed: "—", eta: "—" } },
  ];
  let selected = $state("0");
  const options = samples.map((sample, index) => ({ value: String(index), label: sample.label }));
  const sample = $derived(samples[Number(selected)] ?? samples[0]);
</script>

<section id="acquisition-states" class="flex min-w-0 scroll-mt-20 flex-col gap-4" aria-label="Acquisition transfer previews">
  <div class="flex flex-wrap items-end justify-between gap-4">
    <div>
      <h2 class="font-heading text-xl font-semibold">Transfer states</h2>
      <p class="text-control text-muted-foreground">Read-only samples using the same component as Entity acquisition pages.</p>
    </div>
    <label class="flex w-full flex-col gap-2 sm:w-64">
      <span class="text-label font-medium">Preview state</span>
      <Select options={options} bind:value={selected} />
    </label>
  </div>
  <Card.Root>
    <Card.Header>
      <Card.Title>Download</Card.Title>
    </Card.Header>
    <Card.Content>
      {#key selected}
        <AcquisitionTransferSummary transfer={sample.transfer} />
      {/key}
    </Card.Content>
  </Card.Root>
</section>
