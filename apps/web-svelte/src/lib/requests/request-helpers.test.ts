import { describe, expect, it } from "vitest";
import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
import {
  buildRequestSubmitPayload,
  defaultSelectedChildIds,
  selectDefaultService,
} from "./request-helpers";
import type { RequestDetailResponse, RequestServiceInstanceSummary } from "./request-model";

describe("request helpers", () => {
  it("selects the default matching service instance", () => {
    const services: RequestServiceInstanceSummary[] = [
      service("a", REQUEST_PROVIDER_KIND.radarr, false),
      service("b", REQUEST_PROVIDER_KIND.radarr, true),
      service("c", REQUEST_PROVIDER_KIND.sonarr, true),
    ];

    expect(selectDefaultService(services, REQUEST_PROVIDER_KIND.radarr)?.id).toBe("b");
  });

  it("selects requestable child ids including series specials", () => {
    const detail = detailWithChildren(["0", "1", "2"]);

    expect(defaultSelectedChildIds(detail)).toEqual(["0", "1", "2"]);
  });

  it("builds submit payload from detail and selected service options", () => {
    const payload = buildRequestSubmitPayload(
      detailWithChildren(["0", "1"]),
      service("svc", REQUEST_PROVIDER_KIND.sonarr, true),
      {
        qualityProfileId: 7,
        rootFolderPath: "/series",
        metadataProfileId: null,
        monitored: true,
        searchNow: true,
        selectedChildIds: ["0"],
      },
    );

    expect(payload).toMatchObject({
      serviceId: "svc",
      source: REQUEST_PROVIDER_KIND.sonarr,
      kind: REQUEST_MEDIA_KIND.series,
      externalId: "79169",
      title: "Twin Peaks",
      selectedChildIds: ["0"],
    });
  });
});

function service(
  id: string,
  kind: RequestServiceInstanceSummary["kind"],
  isDefault: boolean,
): RequestServiceInstanceSummary {
  return {
    id,
    kind,
    displayName: id,
    baseUrl: `http://${id}.test`,
    isDefault,
    defaultRootFolderPath: null,
    defaultQualityProfileId: null,
    defaultMetadataProfileId: null,
    searchOnRequest: true,
    hasApiKey: true,
    apiKey: null,
  };
}

function detailWithChildren(ids: string[]): RequestDetailResponse {
  return {
    source: REQUEST_PROVIDER_KIND.sonarr,
    kind: REQUEST_MEDIA_KIND.series,
    externalId: "79169",
    title: "Twin Peaks",
    year: 1990,
    overview: null,
    posterUrl: null,
    backdropUrl: null,
    rating: null,
    runtimeMinutes: null,
    certification: null,
    tags: [],
    studios: [],
    credits: [],
    serviceOptions: [],
    children: ids.map((id) => ({
      id,
      title: id === "0" ? "Specials" : `Season ${id}`,
      kind: REQUEST_MEDIA_KIND.series,
      requestable: true,
      number: Number(id),
      overview: null,
      posterUrl: null,
    })),
  };
}
