const encoder = new TextEncoder();

export function bytesToHex(bytes: ArrayBuffer | Uint8Array): string {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  return [...view].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export function b64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  const out = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) out[i] = binary.charCodeAt(i);
  return out;
}

export async function sha256Hex(value: ArrayBuffer | Uint8Array | string): Promise<string> {
  const data = typeof value === "string" ? encoder.encode(value) : value;
  return bytesToHex(await crypto.subtle.digest("SHA-256", data));
}

export function randomCode6(): string {
  const value = new Uint32Array(1);
  crypto.getRandomValues(value);
  return String(value[0] % 1_000_000).padStart(6, "0");
}

export function canonicalRequest(method: string, pathAndQuery: string, bodyHash: string, timestamp: string, nonce: string): Uint8Array {
  return encoder.encode([method.toUpperCase(), pathAndQuery, bodyHash, timestamp, nonce].join("\n"));
}
