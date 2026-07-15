import {
  acquireVideoSubtitle as acquireVideoSubtitleRequest,
  getOpenSubtitlesConfiguration as getOpenSubtitlesConfigurationRequest,
  searchVideoSubtitles as searchVideoSubtitlesRequest,
  testOpenSubtitlesConnection as testOpenSubtitlesConnectionRequest,
  updateOpenSubtitlesConfiguration as updateOpenSubtitlesConfigurationRequest,
} from "$lib/api/generated/prismedia";
import type {
  AcquireVideoSubtitleResponse,
  OpenSubtitlesConfigurationResponse,
  SearchVideoSubtitlesResponse,
  SubtitleCandidateResponse,
  SubtitleProviderTestResponse,
  UpdateOpenSubtitlesConfigurationRequest,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

export type SubtitleCandidate = SubtitleCandidateResponse;
export type OpenSubtitlesConfiguration = OpenSubtitlesConfigurationResponse;
export type OpenSubtitlesConfigurationUpdate = UpdateOpenSubtitlesConfigurationRequest;
export type SubtitleProviderTest = SubtitleProviderTestResponse;

export async function searchVideoSubtitles(
  videoId: string,
  languages: string[],
  options?: RequestOptions,
): Promise<SearchVideoSubtitlesResponse> {
  return unwrapGenerated(
    await searchVideoSubtitlesRequest(videoId, { languages }, requestInit(options)),
    "Failed to search subtitle providers",
  );
}

export async function acquireVideoSubtitle(
  videoId: string,
  candidate: Pick<SubtitleCandidateResponse, "provider" | "candidateId">,
  options?: RequestOptions,
): Promise<AcquireVideoSubtitleResponse> {
  return unwrapGenerated(
    await acquireVideoSubtitleRequest(videoId, candidate, requestInit(options)),
    "Failed to acquire subtitle",
  );
}

export async function fetchOpenSubtitlesConfiguration(
  options?: RequestOptions,
): Promise<OpenSubtitlesConfigurationResponse> {
  return unwrapGenerated(
    await getOpenSubtitlesConfigurationRequest(requestInit(options)),
    "Failed to load OpenSubtitles configuration",
  );
}

export async function saveOpenSubtitlesConfiguration(
  configuration: UpdateOpenSubtitlesConfigurationRequest,
  options?: RequestOptions,
): Promise<OpenSubtitlesConfigurationResponse> {
  return unwrapGenerated(
    await updateOpenSubtitlesConfigurationRequest(configuration, requestInit(options)),
    "Failed to save OpenSubtitles configuration",
  );
}

export async function testOpenSubtitlesConnection(
  options?: RequestOptions,
): Promise<SubtitleProviderTestResponse> {
  return unwrapGenerated(
    await testOpenSubtitlesConnectionRequest(requestInit(options)),
    "Failed to test OpenSubtitles connection",
  );
}
