<script lang="ts">
  import { Disclosure } from "@prismedia/ui-svelte";

  let { providerName = "provider" }: { providerName?: string } = $props();
</script>

<div class="space-y-2">
  <p class="text-xs leading-relaxed text-text-muted">
    The Prismedia server must already be able to read this folder through a local mount or network share. The {providerName} API reports paths and metadata; this mapping translates paths and does not transfer media files.
  </p>
  <Disclosure title="Shared storage example" class="text-xs">
    <p class="leading-relaxed text-text-muted">
      Mount an NFS or SMB share at <span class="font-mono">/srv/media</span> on the Prismedia host, expose it read-only to the container as <span class="font-mono">/srv/media:/media/external:ro</span>, then map the provider's <span class="font-mono">/movies</span> path to <span class="font-mono">/media/external</span>. Your browser can be on another device. A symbolic link alone does not provide network access.
    </p>
  </Disclosure>
</div>
