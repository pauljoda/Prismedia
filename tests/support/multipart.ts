const CRLF = "\r\n";

export function createMultipartBody(input: {
  fields?: Record<string, string>;
  file?: {
    fieldName: string;
    filename: string;
    contentType: string;
    content: string | Buffer;
  };
}) {
  const boundary = `----prismedia-test-${Math.random().toString(16).slice(2)}`;
  const chunks: Buffer[] = [];

  for (const [key, value] of Object.entries(input.fields ?? {})) {
    chunks.push(
      Buffer.from(
        `--${boundary}${CRLF}` +
          `Content-Disposition: form-data; name="${key}"${CRLF}${CRLF}` +
          `${value}${CRLF}`,
      ),
    );
  }

  if (input.file) {
    chunks.push(
      Buffer.from(
        `--${boundary}${CRLF}` +
          `Content-Disposition: form-data; name="${input.file.fieldName}"; filename="${input.file.filename}"${CRLF}` +
          `Content-Type: ${input.file.contentType}${CRLF}${CRLF}`,
      ),
    );
    chunks.push(
      Buffer.isBuffer(input.file.content)
        ? input.file.content
        : Buffer.from(input.file.content),
    );
    chunks.push(Buffer.from(CRLF));
  }

  chunks.push(Buffer.from(`--${boundary}--${CRLF}`));

  return {
    boundary,
    body: Buffer.concat(chunks),
  };
}
