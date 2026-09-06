<script lang="ts">
  import type { Component } from "svelte";
  import { Badge, Button, TextInput, Select, type SelectOption } from "@prismedia/ui-svelte";
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
              <Button variant="ghost" size="icon"
                type="button"
                class="size-7 shrink-0"
                onclick={() => removeRow(i)}
                {disabled}
                aria-label={`Remove ${credit.name}`}
              >
                <X class="h-3 w-3" />
              </Button>
            </div>
            <div class="credit-details">
              <div class="credit-roles" aria-label={`Roles for ${credit.name}`}>
                {#each credit.roles as role (role)}
                  <Badge variant="outline" class="gap-1 pr-1">
                    {creditRoleLabel(role)}
                    <Button variant="ghost" size="icon"
                      type="button"
                      class="size-5 p-0"
                      onclick={() => removeRole(i, role)}
                      {disabled}
                      aria-label={`Remove ${creditRoleLabel(role)} role from ${credit.name}`}
                    >
                      <X class="h-2.5 w-2.5" />
                    </Button>
                  </Badge>
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
              <TextInput
                type="text"
                value={credit.character}
                oninput={(e) => updateRow(i, { character: e.currentTarget.value })}
                {disabled}
                placeholder="as Character…"
                aria-label={`Character for ${credit.name}`}
                size="sm"
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




  .credit-roles :global(.role-add-select) {
    width: auto;
    min-width: 6.5rem;
    height: 1.65rem;
    font-size: 0.68rem;
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
