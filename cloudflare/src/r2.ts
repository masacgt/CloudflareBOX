import type { Env } from "./types";

const encoder = new TextEncoder();

function hex(bytes: ArrayBuffer | Uint8Array): string {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  return [...view].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function encodeRfc3986(value: string): string {
  return encodeURIComponent(value).replace(/[!'()*]/g, (c) => `%${c.charCodeAt(0).toString(16).toUpperCase()}`);
}

async function sha256(value: string): Promise<string> {
  return hex(await crypto.subtle.digest("SHA-256", encoder.encode(value)));
}

async function hmac(key: ArrayBuffer | Uint8Array, value: string): Promise<Uint8Array> {
  const cryptoKey = await crypto.subtle.importKey(
    "raw",
    key,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  return new Uint8Array(await crypto.subtle.sign("HMAC", cryptoKey, encoder.encode(value)));
}

function amzDate(now: Date): { dateTime: string; day: string } {
  const iso = now.toISOString().replace(/[-:]/g, "").replace(/\.\d{3}Z$/, "Z");
  return { dateTime: iso, day: iso.slice(0, 8) };
}

async function signingKey(secret: string, day: string): Promise<Uint8Array> {
  const dateKey = await hmac(encoder.encode(`AWS4${secret}`), day);
  const regionKey = await hmac(dateKey, "auto");
  const serviceKey = await hmac(regionKey, "s3");
  return hmac(serviceKey, "aws4_request");
}

async function presign(
  env: Env,
  method: "GET" | "PUT",
  key: string,
  expiresIn: number,
  extraQuery: Record<string, string> = {},
): Promise<string> {
  const host = `${env.R2_ACCOUNT_ID}.r2.cloudflarestorage.com`;
  const path = `/${encodeRfc3986(env.R2_BUCKET_NAME)}/${key.split("/").map(encodeRfc3986).join("/")}`;
  const now = amzDate(new Date());
  const scope = `${now.day}/auto/s3/aws4_request`;
  const params: Record<string, string> = {
    ...extraQuery,
    "X-Amz-Algorithm": "AWS4-HMAC-SHA256",
    "X-Amz-Credential": `${env.R2_ACCESS_KEY_ID}/${scope}`,
    "X-Amz-Date": now.dateTime,
    "X-Amz-Expires": String(Math.max(1, Math.min(604800, expiresIn))),
    "X-Amz-SignedHeaders": "host",
  };
  const canonicalQuery = Object.entries(params)
    .map(([k, v]) => [encodeRfc3986(k), encodeRfc3986(v)] as const)
    .sort(([ak, av], [bk, bv]) => ak.localeCompare(bk) || av.localeCompare(bv))
    .map(([k, v]) => `${k}=${v}`)
    .join("&");
  const canonicalRequest = [
    method,
    path,
    canonicalQuery,
    `host:${host}\n`,
    "host",
    "UNSIGNED-PAYLOAD",
  ].join("\n");
  const stringToSign = [
    "AWS4-HMAC-SHA256",
    now.dateTime,
    scope,
    await sha256(canonicalRequest),
  ].join("\n");
  const signature = hex(await hmac(await signingKey(env.R2_SECRET_ACCESS_KEY, now.day), stringToSign));
  return `https://${host}${path}?${canonicalQuery}&X-Amz-Signature=${signature}`;
}

export async function createMultipart(env: Env, key: string): Promise<string> {
  const upload = await env.FILES.createMultipartUpload(key);
  return upload.uploadId;
}

export async function signUploadPart(
  env: Env,
  key: string,
  uploadId: string,
  partNumber: number,
  expiresIn: number,
): Promise<string> {
  return presign(env, "PUT", key, expiresIn, {
    partNumber: String(partNumber),
    uploadId,
  });
}

export async function signSinglePut(env: Env, key: string, expiresIn: number): Promise<string> {
  return presign(env, "PUT", key, expiresIn);
}

export async function completeMultipart(
  env: Env,
  key: string,
  uploadId: string,
  parts: Array<{ partNumber: number; etag: string }>,
): Promise<void> {
  const upload = env.FILES.resumeMultipartUpload(key, uploadId);
  await upload.complete(parts);
}

export async function abortMultipart(env: Env, key: string, uploadId: string): Promise<void> {
  const upload = env.FILES.resumeMultipartUpload(key, uploadId);
  await upload.abort();
}

export async function signGet(env: Env, key: string, expiresIn: number): Promise<string> {
  return presign(env, "GET", key, expiresIn);
}
