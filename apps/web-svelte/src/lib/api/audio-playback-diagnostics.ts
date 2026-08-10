import { reportAudioPlaybackDiagnostic } from "$lib/api/generated/prismedia";
import type { AudioPlaybackDiagnosticRequest } from "$lib/api/generated/model";

/** Sends one low-volume media lifecycle transition without blocking playback. */
export async function sendAudioPlaybackDiagnostic(
  diagnostic: AudioPlaybackDiagnosticRequest,
): Promise<void> {
  await reportAudioPlaybackDiagnostic(diagnostic);
}
