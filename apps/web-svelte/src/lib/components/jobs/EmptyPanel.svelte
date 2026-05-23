<script lang="ts">
  interface Props {
    title: string;
    detail: string;
  }

  let { title, detail }: Props = $props();
</script>

<div class="surface-card no-lift px-4 py-6 text-center">
  <p class="text-sm font-medium text-text-primary">{title}</p>
  <p class="mt-1 text-[0.78rem] text-text-muted">{detail}</p>
</div>
