const encoder = new TextEncoder();

function ownedArrayBuffer(value: Uint8Array<ArrayBufferLike>): ArrayBuffer {
  const copy = new Uint8Array(value.byteLength);
  copy.set(value);
  return copy.buffer;
}

export function bytesToHex(bytes: ArrayBuffer | Uint8Array<ArrayBufferLike>): string {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  return [...view].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export function b64ToBytes(value: string): Uint8Array<ArrayBuffer> {
  const binary = atob(value);
  const out = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) out[i] = binary.charCodeAt(i);
  return out;
}

export async function sha256Hex(value: ArrayBuffer | Uint8Array<ArrayBufferLike> | string): Promise<string> {
  const data = typeof value === "string"
    ? ownedArrayBuffer(encoder.encode(value))
    : value instanceof ArrayBuffer
      ? value
      : ownedArrayBuffer(value);
  return bytesToHex(await crypto.subtle.digest("SHA-256", data));
}

export function randomCode6(): string {
  const value = new Uint32Array(1);
  crypto.getRandomValues(value);
  return String(value[0] % 1_000_000).padStart(6, "0");
}

export function canonicalRequest(method: string, pathAndQuery: string, bodyHash: string, timestamp: string, nonce: string): Uint8Array<ArrayBuffer> {
  return encoder.encode([method.toUpperCase(), pathAndQuery, bodyHash, timestamp, nonce].join("\n"));
}
