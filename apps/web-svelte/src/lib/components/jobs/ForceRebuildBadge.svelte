<script lang="ts">
  import { Badge } from "@prismedia/ui-svelte";
  import type { JobRun } from "$lib/jobs/models";
  import { isForceRebuildJob } from "$lib/jobs/helpers";

  interface Props {
    job: JobRun;
  }

  let { job }: Props = $props();
</script>

{#if isForceRebuildJob(job)}
  <Badge variant="error" class="text-[0.56rem]">
    {#snippet children()}Force rebuild{/snippet}
  </Badge>
{/if}
