import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { open, stat } from "node:fs/promises";

export const supportedFingerprintKinds = ["md5", "oshash"] as const;

export type FingerprintKind = (typeof supportedFingerprintKinds)[number];

const HASH_READ_BUFFER_BYTES = 4 * 1024 * 1024;
const OSHASH_CHUNK_BYTES = 64 * 1024;

export async function computeMd5(filePath: string) {
  const hash = createHash("md5");

  await new Promise<void>((resolve, reject) => {
    const stream = createReadStream(filePath, {
      highWaterMark: HASH_READ_BUFFER_BYTES,
    });
    stream.on("data", (chunk) => hash.update(chunk));
    stream.on("error", reject);
    stream.on("end", () => resolve());
  });

  return hash.digest("hex");
}

export async function computeMd5AndOsHash(
  filePath: string,
): Promise<{ md5: string; oshash: string }> {
  const stats = await stat(filePath);
  const md5 = createHash("md5");
  let head: Buffer | null = null;
  let headRemaining = OSHASH_CHUNK_BYTES;

  await new Promise<void>((resolve, reject) => {
    const stream = createReadStream(filePath, {
      highWaterMark: HASH_READ_BUFFER_BYTES,
    });
    stream.on("data", (chunk: string | Buffer) => {
      const buf = typeof chunk === "string" ? Buffer.from(chunk) : chunk;
      md5.update(buf);
      if (headRemaining > 0) {
        if (head === null) head = Buffer.alloc(OSHASH_CHUNK_BYTES);
        const take = Math.min(headRemaining, buf.length);
        buf.copy(head, OSHASH_CHUNK_BYTES - headRemaining, 0, take);
        headRemaining -= take;
      }
    });
    stream.on("error", reject);
    stream.on("end", () => resolve());
  });

  if (head === null) head = Buffer.alloc(OSHASH_CHUNK_BYTES);

  const tail = Buffer.alloc(OSHASH_CHUNK_BYTES);
  const handle = await open(filePath, "r");
  try {
    await handle.read(
      tail,
      0,
      OSHASH_CHUNK_BYTES,
      Math.max(0, stats.size - OSHASH_CHUNK_BYTES),
    );
  } finally {
    await handle.close();
  }

  let h = BigInt(stats.size);
  for (let i = 0; i < OSHASH_CHUNK_BYTES; i += 8) {
    h += readUInt64LE(head, i);
    h += readUInt64LE(tail, i);
  }

  return {
    md5: md5.digest("hex"),
    oshash: (h & BigInt("0xFFFFFFFFFFFFFFFF")).toString(16).padStart(16, "0"),
  };
}

function readUInt64LE(buffer: Buffer, offset: number) {
  return buffer.readBigUInt64LE(offset);
}

export async function computeOsHash(filePath: string) {
  const stats = await stat(filePath);
  const handle = await open(filePath, "r");

  try {
    const head = Buffer.alloc(OSHASH_CHUNK_BYTES);
    const tail = Buffer.alloc(OSHASH_CHUNK_BYTES);

    await handle.read(head, 0, OSHASH_CHUNK_BYTES, 0);
    await handle.read(
      tail,
      0,
      OSHASH_CHUNK_BYTES,
      Math.max(0, stats.size - OSHASH_CHUNK_BYTES),
    );

    let hash = BigInt(stats.size);

    for (let index = 0; index < OSHASH_CHUNK_BYTES; index += 8) {
      hash += readUInt64LE(head, index);
      hash += readUInt64LE(tail, index);
    }

    return (hash & BigInt("0xFFFFFFFFFFFFFFFF")).toString(16).padStart(16, "0");
  } finally {
    await handle.close();
  }
}
