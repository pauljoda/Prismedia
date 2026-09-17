import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, PluginProvider } from "$lib/api/generated/model";
import ConnectionEditor from "./ConnectionEditor.svelte";

const plugin: PluginProvider = {
  id: "fixture", name: "Book catalog", version: "1.0.0", installed: true, enabled: true, isNsfw: false,
  supports: [], auth: [{ key: "token", label: "Access token", required: true, url: null }], missingAuthKeys: [],
  updateAvailable: false, availableVersion: null,
  integration: { protocolVersion: 1, settings: [], capabilities: [{ kind: PLUGIN_CAPABILITY.catalogDiscovery,
    operations: [INTEGRATION_OPERATION.search], entityKinds: [ENTITY_KIND.book] }] },
};
const connection: ConnectionResponse = {
  id: "fixture-connection", pluginId: plugin.id, name: "My books", baseUrl: "http://catalog.test", enabled: true,
  enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery], settings: {}, configuredSecretKeys: ["token"],
  revision: 2, status: CONNECTION_STATUS.ready, remoteInstanceId: "fixture-installation", hasPersistentRemoteIdentity: true,
  effectiveCapabilities: plugin.integration!.capabilities, lastCheckedAt: null, lastError: null,
};

describe("ConnectionEditor", () => {
  it("discloses the plugin's additional anonymous download hosts", () => {
    const catalog = { ...plugin, integration: { ...plugin.integration!, anonymousArtifactOrigins: ["https://files.test"] } };
    render(ConnectionEditor, { plugins: [catalog], saving: false, error: null, onSave: vi.fn(), onCancel: vi.fn() });
    expect(screen.getByText("Additional download hosts")).toBeInTheDocument();
    expect(screen.getByText("https://files.test")).toBeInTheDocument();
    expect(screen.getByText(/Downloads from these hosts use no authentication headers or cookies/)).toBeInTheDocument();
  });

  it("keeps existing credentials when a saved connection is edited without replacing them", async () => {
    const onSave = vi.fn();
    render(ConnectionEditor, { connection, plugins: [plugin], saving: false, error: null, onSave, onCancel: vi.fn() });
    const token = screen.getByLabelText("Access token") as HTMLInputElement;
    expect(token.value).toBe("");
    expect(token).toHaveAttribute("autocomplete", "new-password");
    expect(screen.getByRole("textbox", { name: "Application or catalog URL" })).toBeDisabled();
    await fireEvent.click(screen.getByRole("button", { name: "Save connection" }));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ secrets: {}, pluginId: plugin.id }));
  });

  it("sends an explicit clear only when the user selects removal", async () => {
    const onSave = vi.fn();
    render(ConnectionEditor, { connection, plugins: [plugin], saving: false, error: null, onSave, onCancel: vi.fn() });
    await fireEvent.click(screen.getByRole("checkbox", { name: "Remove saved access token" }));
    await fireEvent.click(screen.getByRole("button", { name: "Save connection" }));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ secrets: { token: "" } }));
  });
});
