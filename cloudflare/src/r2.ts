import type { Env } from "./types";

export async function abortMultipart(env: Env, objectKey: string, uploadId: string): Promise<void> {
  const upload = env.FILES.resumeMultipartUpload(objectKey, uploadId);
  await upload.abort();
}
