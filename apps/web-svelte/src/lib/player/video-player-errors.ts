/**
 * Returns true only for a genuine codec/decode failure. Network, abort, and transient
 * errors must stay on the current delivery path rather than forcing a heavy transcode.
 */
export function isFatalVideoDecodeError(detail: unknown): boolean {
  const error = detail as {
    code?: number;
    mediaError?: { code?: number };
    message?: string;
  } | null;
  const code = error?.code ?? error?.mediaError?.code;
  if (code === 3 || code === 4) return true;
  const message = (error?.message ?? "").toLowerCase();
  return message.includes("decode") ||
    message.includes("not supported") ||
    message.includes("buffer append") ||
    message.includes("src_not_supported");
}
