<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  onMount(() => { void goto(`/request${page.url.search}`, { replaceState: true }); });
</script>
