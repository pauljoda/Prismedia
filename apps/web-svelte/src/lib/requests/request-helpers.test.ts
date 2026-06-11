import { describe, expect, it } from "vitest";
import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
import {
  buildRequestSubmitPayload,
  defaultSelectedChildIds,
  inferRequestSourceForKind,
  optionDefaultsForService,
  selectDefaultService,
  thumbnailAspectForKind,
} from "./request-helpers";
import type { RequestDetailResponse, RequestServiceInstanceSummary, RequestServiceOptionsResponse } from "./request-model";

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

  it("mirrors upstream monitoring for tracked items so updates submit what is shown", () => {
    const base = detailWithChildren(["0", "1", "2"]);
    const detail = {
      ...base,
      tracked: true,
      upstreamId: "12",
      monitored: true,
      children: base.children.map((child) => ({ ...child, monitored: child.id === "1" })),
    };

    expect(defaultSelectedChildIds(detail)).toEqual(["1"]);
  });

  it("does not default Lidarr album lookup children into submit selections", () => {
    const detail = {
      ...detailWithChildren(["mb-album"]),
      source: REQUEST_PROVIDER_KIND.lidarr,
      kind: REQUEST_MEDIA_KIND.artist,
    };

    expect(defaultSelectedChildIds(detail)).toEqual([]);
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

  it("uses square artwork for music kinds and posters elsewhere", () => {
    expect(thumbnailAspectForKind(REQUEST_MEDIA_KIND.artist)).toBe("1 / 1");
    expect(thumbnailAspectForKind(REQUEST_MEDIA_KIND.album)).toBe("1 / 1");
    expect(thumbnailAspectForKind(REQUEST_MEDIA_KIND.movie)).toBe("2 / 3");
    expect(thumbnailAspectForKind(REQUEST_MEDIA_KIND.series)).toBe("2 / 3");
  });

  it("infers source for direct detail routes", () => {
    expect(inferRequestSourceForKind(REQUEST_MEDIA_KIND.movie)).toBe(REQUEST_PROVIDER_KIND.radarr);
    expect(inferRequestSourceForKind(REQUEST_MEDIA_KIND.series)).toBe(REQUEST_PROVIDER_KIND.sonarr);
    expect(inferRequestSourceForKind(REQUEST_MEDIA_KIND.artist)).toBe(REQUEST_PROVIDER_KIND.lidarr);
    expect(inferRequestSourceForKind(REQUEST_MEDIA_KIND.album)).toBe(REQUEST_PROVIDER_KIND.lidarr);
  });

  it("selects valid defaults and search setting from service options before falling back to first option", () => {
    const defaults = optionDefaultsForService(
      {
        ...service("svc", REQUEST_PROVIDER_KIND.lidarr, true),
        defaultQualityProfileId: 2,
        defaultMetadataProfileId: 9,
        defaultRootFolderPath: "/missing",
        searchOnRequest: false,
      },
      {
        qualityProfiles: [
          { id: "1", name: "Any", path: null },
          { id: "2", name: "Lossless", path: null },
        ],
        rootFolders: [{ id: "/music", name: "/music", path: "/music" }],
        metadataProfiles: [{ id: "9", name: "Standard", path: null }],
        tags: [],
      },
    );

    expect(defaults).toEqual({
      qualityProfileId: 2,
      rootFolderPath: "/music",
      metadataProfileId: 9,
      searchNow: false,
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
    minimumAvailability: "released",
    defaultTagIds: [],
    searchOnRequest: true,
    hasApiKey: true,
  };
}

function detailWithChildren(ids: string[]): RequestDetailResponse {
  const serviceOptions: RequestServiceOptionsResponse = {
    qualityProfiles: [],
    rootFolders: [],
    metadataProfiles: [],
    tags: [],
  };

  return {
    source: REQUEST_PROVIDER_KIND.sonarr,
    kind: REQUEST_MEDIA_KIND.series,
    externalId: "79169",
    title: "Twin Peaks",
    subtitle: null,
    year: 1990,
    overview: null,
    trackCount: null,
    posterUrl: null,
    backdropUrl: null,
    rating: null,
    runtimeMinutes: null,
    certification: null,
    tags: [],
    studios: [],
    credits: [],
    tracks: [],
    tracked: false,
    upstreamId: null,
    monitored: null,
    serviceOptions,
    children: ids.map((id) => ({
      id,
      title: id === "0" ? "Specials" : `Season ${id}`,
      kind: REQUEST_MEDIA_KIND.series,
      requestable: true,
      number: Number(id),
      year: null,
      itemCount: null,
      overview: null,
      posterUrl: null,
      monitored: null,
    })),
  };
}
