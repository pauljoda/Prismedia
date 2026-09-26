<script lang="ts">
  let { options, value = "", ariaLabel, disabled = false, onchange }: {
    options: { value: string; label: string }[];
    value?: string;
    ariaLabel: string;
    disabled?: boolean;
    onchange: (value: string) => void;
  } = $props();
</script>

<select aria-label={ariaLabel} {value} {disabled} onchange={event => onchange(event.currentTarget.value)}>
  {#each options as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
</select>
