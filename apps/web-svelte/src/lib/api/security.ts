import { fetchApi } from "$lib/api/orval-fetch";

export interface ApiKeyResponse {
  apiKey: string;
  createdAt: string;
  updatedAt: string;
}

export interface ApiKeyRegenerateResponse extends ApiKeyResponse {
  invalidatedSessions: number;
}

export interface JellyfinProfile {
  id: string;
  username: string;
  displayName: string;
  allowNsfw: boolean;
  enabled: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface JellyfinProfilesResponse {
  items: JellyfinProfile[];
}

export interface JellyfinProfileCreateRequest {
  username: string;
  displayName?: string | null;
  allowNsfw: boolean;
  enabled: boolean;
}

export interface JellyfinProfileUpdateRequest {
  username?: string | null;
  displayName?: string | null;
  allowNsfw?: boolean | null;
  enabled?: boolean | null;
}

export async function fetchApiKey(): Promise<ApiKeyResponse> {
  return fetchApi<ApiKeyResponse>("/security/api-key");
}

export async function regenerateApiKey(): Promise<ApiKeyRegenerateResponse> {
  return fetchApi<ApiKeyRegenerateResponse>("/security/api-key/regenerate", {
    method: "POST",
  });
}

export async function fetchJellyfinProfiles(): Promise<JellyfinProfile[]> {
  const response = await fetchApi<JellyfinProfilesResponse>("/security/jellyfin-profiles");
  return response.items;
}

export async function createJellyfinProfile(
  request: JellyfinProfileCreateRequest,
): Promise<JellyfinProfile> {
  return fetchApi<JellyfinProfile>("/security/jellyfin-profiles", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export async function updateJellyfinProfile(
  id: string,
  request: JellyfinProfileUpdateRequest,
): Promise<JellyfinProfile> {
  return fetchApi<JellyfinProfile>(`/security/jellyfin-profiles/${id}`, {
    method: "PATCH",
    body: JSON.stringify(request),
  });
}

export async function deleteJellyfinProfile(id: string): Promise<void> {
  return fetchApi<void>(`/security/jellyfin-profiles/${id}`, {
    method: "DELETE",
  });
}
