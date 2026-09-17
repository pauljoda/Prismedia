<script lang="ts">
  import { untrack } from "svelte";
  import { Checkbox, Field, Select, Toggle } from "@prismedia/ui-svelte";
  import { PLUGIN_SEARCH_FIELD_TYPE, type PluginCapabilityCode } from "$lib/api/generated/codes";
  import type { ConnectionResponse, CreateConnectionRequest, PluginProvider } from "$lib/api/generated/model";
  import { EditFormShell, FormField, TextField } from "$lib/components/forms";
  import PasswordField from "$lib/components/forms/PasswordField.svelte";
  import UnsavedChangesGuard from "$lib/components/forms/UnsavedChangesGuard.svelte";
  import { capabilityLabels } from "$lib/integrations/connection-labels";

  let { connection = null, plugins, saving, error, onSave, onCancel }: {
    connection?: ConnectionResponse | null; plugins: PluginProvider[]; saving: boolean; error: string | null;
    onSave: (request: CreateConnectionRequest) => void; onCancel: () => void;
  } = $props();
  const initial = untrack(() => connection);
  const initialPlugin = untrack(() => plugins.find(item => item.id === initial?.pluginId) ?? plugins[0]);
  const id = $props.id();
  let pluginId = $state(initial?.pluginId ?? initialPlugin?.id ?? "");
  let name = $state(initial?.name ?? "");
  let baseUrl = $state(initial?.baseUrl ?? "");
  let enabled = $state(initial?.enabled ?? true);
  let capabilities = $state<PluginCapabilityCode[]>(initial?.enabledCapabilities ?? initialPlugin?.integration?.capabilities.map(item => item.kind) ?? []);
  let settings = $state<Record<string, string>>({ ...initial?.settings });
  let secrets = $state<Record<string, string>>({});
  let clearedSecrets = $state<string[]>([]);
  const plugin = $derived(plugins.find(item => item.id === pluginId));
  const initialDraft = untrack(() => JSON.stringify({ pluginId, name, baseUrl, enabled, capabilities, settings }));
  const dirty = $derived(JSON.stringify({ pluginId, name, baseUrl, enabled, capabilities, settings }) !== initialDraft
    || Object.values(secrets).some(Boolean) || clearedSecrets.length > 0);

  function choosePlugin(value: string) {
    pluginId = value;
    capabilities = plugins.find(item => item.id === value)?.integration?.capabilities.map(item => item.kind) ?? [];
    settings = {}; secrets = {}; clearedSecrets = [];
  }
  function save() {
    const changes: Record<string, string> = {};
    for (const [key, value] of Object.entries(secrets)) if (value) changes[key] = value;
    for (const key of clearedSecrets) changes[key] = "";
    onSave({ pluginId, name, baseUrl, enabled, enabledCapabilities: capabilities, settings, secrets: changes });
  }
</script>

<UnsavedChangesGuard {dirty} />
<EditFormShell title={initial ? `Edit ${initial.name}` : "New connection"}
  description="Connect a catalog or application using an installed plugin. Each connection has its own settings and credentials."
  {saving} {error} onSave={save} {onCancel} saveLabel="Save connection"
  saveDisabled={!plugin || !name.trim() || !baseUrl.trim() || capabilities.length === 0}>
  <Field.Group>
    <FormField label="Plugin" htmlFor={`${id}-plugin`} required>
      <Select id={`${id}-plugin`} value={pluginId} options={plugins.map(item => ({ value: item.id, label: item.name }))}
        disabled={saving || !!initial} onchange={choosePlugin} ariaLabel="Plugin" />
    </FormField>
    <TextField label="Connection name" value={name} onChange={value => name = value} placeholder="My book catalog" required disabled={saving} />
    <TextField label="Application or catalog URL" value={baseUrl} onChange={value => baseUrl = value} type="url"
      placeholder="http://catalog:8080" required disabled={saving || !!initial?.remoteInstanceId}
      helper={initial?.remoteInstanceId ? "Create another connection to use a different address for a verified application." : "Use the address Prismedia can reach, including any base path."} />
    <Field.Field orientation="horizontal">
      <Field.Label for={`${id}-enabled`}>Enable connection</Field.Label>
      <Toggle id={`${id}-enabled`} checked={enabled} onchange={value => enabled = value} disabled={saving} ariaLabel="Enable connection" />
    </Field.Field>
    <Field.Set>
      <Field.Legend>Capabilities</Field.Legend>
      <Field.Description>Allow this connection to provide these features.</Field.Description>
      <Field.Group>
        {#each plugin?.integration?.capabilities ?? [] as capability (capability.kind)}
          <Field.Field orientation="horizontal">
            <Checkbox id={`${id}-${capability.kind}`} checked={capabilities.includes(capability.kind)} disabled={saving}
              onchange={checked => capabilities = checked ? [...capabilities, capability.kind] : capabilities.filter(item => item !== capability.kind)} />
            <Field.Label for={`${id}-${capability.kind}`}>{capabilityLabels[capability.kind]}</Field.Label>
          </Field.Field>
        {/each}
      </Field.Group>
    </Field.Set>
    {#if plugin?.integration?.anonymousArtifactOrigins?.length}
      <Field.Set>
        <Field.Legend>Additional download hosts</Field.Legend>
        <Field.Description>Downloads from these hosts use no authentication headers or cookies. The installed plugin declares this list.</Field.Description>
        <ul class="text-sm text-muted-foreground break-all">
          {#each plugin.integration.anonymousArtifactOrigins as origin (origin)}
            <li>{origin}</li>
          {/each}
        </ul>
      </Field.Set>
    {/if}
    {#each plugin?.integration?.settings ?? [] as field (field.key)}
      <TextField label={field.label} value={settings[field.key] ?? ""} onChange={value => settings = { ...settings, [field.key]: value }}
        required={field.required} disabled={saving} placeholder={field.placeholder ?? undefined} helper={field.help ?? undefined}
        type={field.type === PLUGIN_SEARCH_FIELD_TYPE.text ? "text" : "number"} />
    {/each}
    {#if plugin?.auth.length}
      <Field.Set>
        <Field.Legend>Credentials</Field.Legend>
        <Field.Description>Saved separately for this connection. Leave a saved credential blank to keep it.</Field.Description>
        <Field.Group>
          {#each plugin.auth as field (field.key)}
            {@const configured = initial?.configuredSecretKeys.includes(field.key)}
            <PasswordField label={field.label} value={secrets[field.key] ?? ""} onChange={value => secrets = { ...secrets, [field.key]: value }}
              placeholder={configured ? "Saved credential" : "Enter credential"} autocomplete="new-password"
              required={field.required && !configured} disabled={saving || clearedSecrets.includes(field.key)} />
            {#if configured}
              <Field.Field orientation="horizontal">
                <Checkbox id={`${id}-clear-${field.key}`} checked={clearedSecrets.includes(field.key)} disabled={saving}
                  onchange={checked => clearedSecrets = checked ? [...clearedSecrets, field.key] : clearedSecrets.filter(key => key !== field.key)} />
                <Field.Label for={`${id}-clear-${field.key}`}>Remove saved {field.label.toLowerCase()}</Field.Label>
              </Field.Field>
            {/if}
          {/each}
        </Field.Group>
      </Field.Set>
    {/if}
  </Field.Group>
</EditFormShell>
