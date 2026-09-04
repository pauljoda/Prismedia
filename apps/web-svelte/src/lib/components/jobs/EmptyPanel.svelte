<script lang="ts">
  import { Empty } from "@prismedia/ui-svelte";
  interface Props {
    title: string;
    detail: string;
  }

  let { title, detail }: Props = $props();
</script>

<Empty.Root class="border border-border">
  <Empty.Header><Empty.Title>{title}</Empty.Title><Empty.Description>{detail}</Empty.Description></Empty.Header>
</Empty.Root>
