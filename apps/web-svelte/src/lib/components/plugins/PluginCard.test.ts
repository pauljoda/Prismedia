import { render, screen, within } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, PluginProvider } from "$lib/api/generated/model";
import PluginCard from "./PluginCard.svelte";

const sonarr: PluginProvider = {
  id: "sonarr",
  name: "Sonarr",
  version: "1.0.0",
  installed: true,
  enabled: true,
  isNsfw: false,
  supports: [],
  auth: [],
  missingAuthKeys: [],
  integration: {
    protocolVersion: 1,
    settings: [],
    capabilities: [
      { kind: PLUGIN_CAPABILITY.externalManager, operations: [], entityKinds: [ENTITY_KIND.videoSeries] },
    ],
  },
};

function connection(overrides: Partial<ConnectionResponse>): ConnectionResponse {
  return {
    id: "c1",
    pluginId: "sonarr",
    name: "Sonarr",
    baseUrl: "http://sonarr",
    enabled: true,
    enabledCapabilities: [],
    settings: {},
    configuredSecretKeys: [],
    revision: 1,
    status: CONNECTION_STATUS.ready,
    remoteInstanceId: null,
    hasPersistentRemoteIdentity: false,
    effectiveCapabilities: [],
    lastCheckedAt: null,
    lastError: null,
    ...overrides,
  };
}

describe("PluginCard connections", () => {
  it("shows a ready connection by name alone, with no resting LED", () => {
    render(PluginCard, { props: { plugin: sonarr, connections: [connection({ name: "Main" })] } });

    const list = screen.getByRole("list", { name: "Connections using Sonarr" });
    expect(within(list).getByText("Main")).toBeInTheDocument();
    expect(within(list).queryByRole("status")).not.toBeInTheDocument();
  });

  it("lights an LED only for connections that need attention", () => {
    render(PluginCard, {
      props: {
        plugin: sonarr,
        connections: [
          connection({ id: "down", name: "Down", status: CONNECTION_STATUS.unavailable }),
          connection({ id: "off", name: "Off", enabled: false }),
          connection({ id: "checking", name: "Checking", status: CONNECTION_STATUS.unverified }),
        ],
      },
    });

    const list = screen.getByRole("list", { name: "Connections using Sonarr" });
    const leds = within(list).getAllByRole("status");
    expect(leds.map((led) => led.getAttribute("aria-label"))).toEqual([
      "Status: error",
      "Status: idle",
      "Status: info",
    ]);
    expect(within(list).getByText("Unavailable")).toBeInTheDocument();
    expect(within(list).getByText("Disabled")).toBeInTheDocument();
  });
});
