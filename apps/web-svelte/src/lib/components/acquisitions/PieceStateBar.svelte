<script lang="ts">
  // A compact piece-progress map. qBittorrent piece counts can be large, so we downsample
  // contiguous runs into a fixed number of cells: a cell is "done" only when every piece in
  // its run is done, "active" when any is downloading, otherwise "missing".
  interface Props {
    pieces: number[];
    maxCells?: number;
  }
  let { pieces, maxCells = 160 }: Props = $props();

  const cells = $derived.by(() => {
    if (pieces.length === 0) return [] as number[];
    if (pieces.length <= maxCells) return pieces;
    const out: number[] = [];
    const per = pieces.length / maxCells;
    for (let i = 0; i < maxCells; i++) {
      const start = Math.floor(i * per);
      const end = Math.floor((i + 1) * per);
      let allDone = true;
      let anyActive = false;
      for (let j = start; j < end; j++) {
        if (pieces[j] !== 2) allDone = false;
        if (pieces[j] === 1) anyActive = true;
      }
      out.push(allDone ? 2 : anyActive ? 1 : 0);
    }
    return out;
  });

  function cellClass(state: number): string {
    if (state === 2) return "bg-accent-400";
    if (state === 1) return "bg-accent-700 animate-pulse";
    return "bg-surface-3";
  }
</script>

{#if cells.length > 0}
  <div class="flex flex-wrap gap-[2px]" aria-label="Download piece map">
    {#each cells as state, i (i)}
      <span class={`h-2 w-2 rounded-[1px] ${cellClass(state)}`}></span>
    {/each}
  </div>
{/if}
