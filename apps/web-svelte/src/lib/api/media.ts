import {
  getAudioTrack,
} from "$lib/api/generated/prismedia";
import type {
  AudioTrackDetail,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

export type {
  AudioTrackDetail,
};

export function fetchAudioTrack(
  id: string,
  options?: RequestOptions,
): Promise<AudioTrackDetail> {
  return getAudioTrack(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch audio track ${id}`),
  );
}
