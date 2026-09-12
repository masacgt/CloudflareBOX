import type { Env } from "./types";

export async function createMultipart(env: Env, objectKey: string): Promise<string> {
  const upload = await env.FILES.createMultipartUpload(objectKey);
  return upload.uploadId;
}

export async function uploadMultipartPart(
  env: Env,
  objectKey: string,
  uploadId: string,
  partNumber: number,
  body: Uint8Array,
): Promise<R2UploadedPart> {
  const upload = env.FILES.resumeMultipartUpload(objectKey, uploadId);
  return await upload.uploadPart(partNumber, body);
}

export async function completeMultipart(
  env: Env,
  objectKey: string,
  uploadId: string,
  parts: R2UploadedPart[],
): Promise<void> {
  const upload = env.FILES.resumeMultipartUpload(objectKey, uploadId);
  await upload.complete(parts);
}

export async function abortMultipart(env: Env, objectKey: string, uploadId: string): Promise<void> {
  const upload = env.FILES.resumeMultipartUpload(objectKey, uploadId);
  await upload.abort();
}
