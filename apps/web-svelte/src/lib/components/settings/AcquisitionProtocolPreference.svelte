<script lang="ts">
  import { Badge, Select } from "@prismedia/ui-svelte";
  import { DOWNLOAD_PROTOCOL, type DownloadProtocolCode } from "$lib/api/generated/codes";

  interface Props {
    availableProtocols: DownloadProtocolCode[];
    value?: DownloadProtocolCode;
    busy?: boolean;
    onchange: (protocol: DownloadProtocolCode) => void;
  }

  let {
    availableProtocols,
    value = DOWNLOAD_PROTOCOL.usenet,
    busy = false,
    onchange,
  }: Props = $props();

  const canChoose = $derived(availableProtocols.length > 1);
  const onlyProtocol = $derived(availableProtocols.length === 1 ? availableProtocols[0] : null);
  const options = [
    { value: DOWNLOAD_PROTOCOL.usenet, label: "Usenet" },
    { value: DOWNLOAD_PROTOCOL.torrent, label: "Torrent" },
    { value: DOWNLOAD_PROTOCOL.soulseek, label: "Soulseek" },
  ].filter((option) => availableProtocols.includes(option.value));

  function label(protocol: DownloadProtocolCode): string {
    return options.find((option) => option.value === protocol)?.label ?? protocol;
  }

  function choose(raw: string) {
    if (availableProtocols.includes(raw as DownloadProtocolCode)) onchange(raw as DownloadProtocolCode);
  }
</script>

<section class="space-y-2">
  <div>
    <h3 class="text-kicker text-text-primary">Preferred download type</h3>
    <p class="mt-1 text-[0.72rem] leading-relaxed text-text-muted">
      Prismedia searches this type first, then falls back when it finds no good results.
    </p>
  </div>

  {#if canChoose}
    <div class="max-w-xs">
      <Select
        size="sm"
        {options}
        {value}
        disabled={busy}
        ariaLabel="Preferred download type"
        onchange={choose}
      />
    </div>
  {:else if onlyProtocol}
    <div class="flex flex-wrap items-center gap-2 text-[0.78rem] text-text-muted">
      <Badge variant="default">{label(onlyProtocol)} only</Badge>
      <span>Add and enable a client for another protocol to choose a preference.</span>
    </div>
  {:else}
    <p class="text-[0.78rem] text-text-muted">No enabled download clients. Add one before choosing a preference.</p>
  {/if}
</section>
