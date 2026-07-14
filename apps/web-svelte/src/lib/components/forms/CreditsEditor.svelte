<script lang="ts">
  import type { Component } from "svelte";
  import { Select, type SelectOption } from "@prismedia/ui-svelte";
  import { X } from "@lucide/svelte";
  import { CREDIT_ROLE } from "$lib/entities/entity-codes";
  import { creditRoleLabel } from "$lib/entities/entity-credits";
  import type { EntityCreditDraft } from "$lib/entities/entity-detail-edit";
  import EntityPicker, { type EntityPickerItem } from "./EntityPicker.svelte";
  import FormField from "./FormField.svelte";

  interface Props {
    credits: EntityCreditDraft[];
    onChange: (credits: EntityCreditDraft[]) => void;
    /** Async person search backing the add control. */
    onSearch: (query: string) => Promise<EntityPickerItem[]>;
    label?: string;
    icon?: Component;
    helper?: string;
    error?: string;
    disabled?: boolean;
    /** Role pre-selected for newly added people (e.g. actor on video kinds). */
    defaultRole?: string;
    placeholder?: string;
  }

  let {
    credits,
    onChange,
    onSearch,
    label,
    icon,
    helper,
    error,
    disabled = false,
    defaultRole = CREDIT_ROLE.person,
    placeholder = "Search people…",
  }: Props = $props();

  const roleOptions = Object.values(CREDIT_ROLE);

  // The picker is used purely as a search-and-add control; rows below render the
  // selection. Mirroring rows into picker values suppresses duplicate add-new offers.
  const pickerValues = $derived(
    credits.map((credit) => ({
      id: `credit:${credit.name.toLowerCase()}`,
      title: credit.name,
      thumbnailUrl: credit.thumbnailUrl,
    })),
  );

  function handlePickerChange(items: EntityPickerItem[]) {
    const existing = new Set(credits.map((credit) => credit.name.toLowerCase()));
    const added = items.find((item) => !existing.has(item.title.toLowerCase()));
    if (!added) return;
    onChange([
      ...credits,
      {
        name: added.title,
        thumbnailUrl: added.thumbnailUrl,
        roles: [defaultRole],
        character: "",
        extraCharacters: [],
      },
    ]);
  }

  function updateRow(index: number, patch: Partial<EntityCreditDraft>) {
    onChange(credits.map((credit, i) => (i === index ? { ...credit, ...patch } : credit)));
  }

  function removeRow(index: number) {
    onChange(credits.filter((_, i) => i !== index));
  }

  function addRole(index: number, role: string) {
    const current = credits[index];
    if (!role || !current || current.roles.includes(role)) return;
    updateRow(index, { roles: [...current.roles, role] });
  }

  function removeRole(index: number, role: string) {
    const current = credits[index];
    if (!current) return;
    updateRow(index, { roles: current.roles.filter((value) => value !== role) });
  }

  function availableRoles(credit: EntityCreditDraft): SelectOption[] {
    return roleOptions
      .filter((role) => !credit.roles.includes(role))
      .map((role) => ({ value: role, label: creditRoleLabel(role) }));
  }
</script>

<FormField {label} {icon} {helper} {error}>
  <div class="credits-editor">
    {#if credits.length > 0}
      <ul class="credit-rows">
        {#each credits as credit, i (i)}
          {@const rolesOptions = availableRoles(credit)}
          <li class="credit-row">
            <div class="credit-identity">
              {#if credit.thumbnailUrl}
                <img src={credit.thumbnailUrl} alt="" class="credit-avatar" />
              {:else}
                <span class="credit-avatar credit-avatar-placeholder">
                  {credit.name.charAt(0).toUpperCase()}
                </span>
              {/if}
              <span class="credit-name truncate" title={credit.name}>{credit.name}</span>
              <button
                type="button"
                class="credit-remove"
                onclick={() => removeRow(i)}
                {disabled}
                aria-label={`Remove ${credit.name}`}
              >
                <X class="h-3 w-3" />
              </button>
            </div>
            <div class="credit-details">
              <div class="credit-roles" aria-label={`Roles for ${credit.name}`}>
                {#each credit.roles as role (role)}
                  <span class="role-chip">
                    {creditRoleLabel(role)}
                    <button
                      type="button"
                      class="role-chip-remove"
                      onclick={() => removeRole(i, role)}
                      {disabled}
                      aria-label={`Remove ${creditRoleLabel(role)} role from ${credit.name}`}
                    >
                      <X class="h-2.5 w-2.5" />
                    </button>
                  </span>
                {/each}
                {#if rolesOptions.length > 0}
                  {#key credit.roles.length}
                    <Select
                      options={rolesOptions}
                      placeholder="+ Role"
                      size="sm"
                      class="role-add-select"
                      {disabled}
                      onchange={(role) => addRole(i, role)}
                    />
                  {/key}
                {/if}
              </div>
              <input
                type="text"
                value={credit.character}
                oninput={(e) => updateRow(i, { character: e.currentTarget.value })}
                {disabled}
                placeholder="as Character…"
                aria-label={`Character for ${credit.name}`}
                class="credit-character"
              />
            </div>
          </li>
        {/each}
      </ul>
    {/if}

    <EntityPicker
      values={pickerValues}
      onChange={handlePickerChange}
      {onSearch}
      {placeholder}
      {disabled}
      canAddNew={true}
      addNewLabel="person"
      mode="multi"
      showSelectedChips={false}
    />
  </div>
</FormField>

<style>
  .credits-editor {
    display: grid;
    gap: 0.5rem;
  }

  .credit-rows {
    display: grid;
    gap: 0.35rem;
    list-style: none;
    margin: 0;
    padding: 0;
  }

  .credit-row {
    display: grid;
    gap: 0.4rem;
    min-width: 0;
    padding: 0.45rem 0.55rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-2, #11151c);
  }

  .credit-identity {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    min-width: 0;
  }

  .credit-avatar {
    width: 1.75rem;
    height: 1.75rem;
    flex-shrink: 0;
    border-radius: var(--radius-xs, 4px);
    object-fit: cover;
  }

  .credit-avatar-placeholder {
    display: grid;
    place-items: center;
    background: var(--color-surface-3, #1a2030);
    color: var(--color-text-muted, #94a3b8);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
  }

  .credit-name {
    flex: 1;
    min-width: 0;
    color: var(--color-text-primary, #e2e8f0);
    font-size: 0.82rem;
    font-weight: 500;
  }

  .credit-remove {
    display: grid;
    place-items: center;
    width: 1.6rem;
    height: 1.6rem;
    flex-shrink: 0;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-xs, 4px);
    background: transparent;
    color: var(--color-text-disabled, #4a5568);
    cursor: pointer;
    transition: color 0.15s, background 0.15s, border-color 0.15s;
  }

  .credit-remove:hover:not(:disabled) {
    color: var(--color-error-text, #fca5a5);
    background: color-mix(in srgb, var(--color-surface-2) 90%, var(--color-error));
    border-color: rgba(220, 80, 80, 0.3);
  }

  .credit-details {
    display: grid;
    gap: 0.4rem;
    min-width: 0;
  }

  .credit-roles {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.3rem;
    min-width: 0;
  }

  .role-chip {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    padding: 0.16rem 0.3rem 0.16rem 0.45rem;
    border: 1px solid var(--color-border-accent, rgba(199, 155, 92, 0.24));
    border-radius: var(--radius-xs, 4px);
    background: color-mix(in srgb, var(--color-surface-2) 92%, var(--color-accent));
    color: var(--color-text-secondary, #c4c9d4);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    letter-spacing: 0.03em;
    text-transform: uppercase;
  }

  .role-chip-remove {
    display: grid;
    place-items: center;
    border: none;
    background: transparent;
    color: var(--color-text-disabled, #4a5568);
    cursor: pointer;
    padding: 0.1rem;
    transition: color 0.15s;
  }

  .role-chip-remove:hover:not(:disabled) {
    color: var(--color-error-text, #fca5a5);
  }

  .credit-roles :global(.role-add-select) {
    width: auto;
    min-width: 6.5rem;
    height: 1.65rem;
    font-size: 0.68rem;
  }

  .credit-character {
    width: 100%;
    min-width: 0;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-1, #0b0e14);
    color: var(--color-text-primary, #e2e8f0);
    font-size: 0.78rem;
    padding: 0.32rem 0.55rem;
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.3);
  }

  .credit-character::placeholder {
    color: var(--color-text-disabled, #4a5568);
  }

  .credit-character:focus {
    border-color: var(--color-border-accent, rgba(199, 155, 92, 0.24));
    outline: none;
    box-shadow:
      inset 0 2px 8px rgba(0, 0, 0, 0.3),
      0 0 0 1px rgba(199, 201, 204, 0.35),
      0 0 8px rgba(199, 201, 204, 0.15);
  }

  @media (min-width: 640px) {
    .credit-row {
      grid-template-columns: minmax(10rem, 1.1fr) 2fr;
      align-items: start;
    }

    .credit-details {
      grid-template-columns: 1.4fr 1fr;
      align-items: center;
    }
  }
</style>
