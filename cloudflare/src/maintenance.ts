import { abortMultipart } from "./r2";
import type { Env, TransferRow } from "./types";
import { recordUsage } from "./usage";

async function finishCanceled(env: Env, row: TransferRow, now: number): Promise<void> {
  await env.DB.prepare("UPDATE transfers SET state='CANCELED',multipart_upload_id=NULL,plaintext_sha256=NULL,delete_after=NULL,error_code=NULL,canceled_at=COALESCE(canceled_at,?),updated_at=? WHERE id=?")
    .bind(now, now, row.id).run();
}

export async function scheduledMaintenance(env: Env): Promise<void> {
  const now = Math.floor(Date.now() / 1000);
  let classAOps = 0;

  const canceling = await env.DB.prepare("SELECT * FROM transfers WHERE state='CANCELING' LIMIT 100").all<TransferRow>();
  for (const row of canceling.results) {
    const multipartStillOpen = row.r2_ready_at === null && row.multipart_upload_id !== null;
    try {
      if (multipartStillOpen) {
        await abortMultipart(env, row.object_key, row.multipart_upload_id!);
      } else {
        await env.FILES.delete(row.object_key);
      }
      classAOps++;
      await finishCanceled(env, row, now);
    } catch {
      const code = multipartStillOpen ? "cancel_abort_retry" : "cancel_delete_retry";
      await env.DB.prepare("UPDATE transfers SET error_code=?,updated_at=? WHERE id=?").bind(code, now, row.id).run();
    }
  }

  const due = await env.DB.prepare("SELECT * FROM transfers WHERE state='DELETE_PENDING' AND delete_after <= ? LIMIT 100").bind(now).all<TransferRow>();
  for (const row of due.results) {
    try {
      await env.FILES.delete(row.object_key);
      classAOps++;
      if (row.canceled_at !== null) {
        await finishCanceled(env, row, now);
      } else {
        await env.DB.prepare("UPDATE transfers SET state='COMPLETE',plaintext_sha256=NULL,completed_at=?,error_code=NULL,updated_at=? WHERE id=?")
          .bind(now, now, row.id).run();
      }
    } catch {
      const code = row.canceled_at !== null ? "cancel_delete_retry" : "r2_delete_retry";
      await env.DB.prepare("UPDATE transfers SET error_code=?,updated_at=? WHERE id=?").bind(code, now, row.id).run();
    }
  }

  await env.DB.prepare("DELETE FROM request_nonces WHERE expires_at < ?").bind(now).run();
  await env.DB.prepare("UPDATE pc_commands SET state='expired' WHERE state='pending' AND expires_at < ?").bind(now).run();
  if (classAOps > 0) await recordUsage(env, classAOps, 0);
}

export async function dailyMaintenance(env: Env): Promise<void> {
  const now = Math.floor(Date.now() / 1000);
  const staleBefore = now - 24 * 60 * 60;
  const stale = await env.DB.prepare("SELECT * FROM transfers WHERE state IN ('UPLOADING','PAUSED') AND canceled_at IS NULL AND updated_at < ? AND multipart_upload_id IS NOT NULL LIMIT 100")
    .bind(staleBefore).all<TransferRow>();
  let abortOps = 0;
  for (const row of stale.results) {
    try {
      await abortMultipart(env, row.object_key, row.multipart_upload_id!);
      abortOps++;
      await env.DB.prepare("UPDATE transfers SET state='FAILED',multipart_upload_id=NULL,error_code='stale_upload_aborted',updated_at=? WHERE id=?").bind(now, row.id).run();
    } catch {
      await env.DB.prepare("UPDATE transfers SET error_code='stale_abort_retry',updated_at=? WHERE id=?").bind(now, row.id).run();
    }
  }
  const historyBefore = now - 30 * 24 * 60 * 60;
  await env.DB.prepare("DELETE FROM transfer_parts WHERE transfer_id IN (SELECT id FROM transfers WHERE state IN ('COMPLETE','FAILED','CANCELED') AND updated_at < ?)").bind(historyBefore).run();
  await env.DB.prepare("DELETE FROM transfers WHERE state IN ('COMPLETE','FAILED','CANCELED') AND updated_at < ?").bind(historyBefore).run();
  await env.DB.prepare("DELETE FROM pc_commands WHERE expires_at < ?").bind(now - 24 * 60 * 60).run();
  if (abortOps) await recordUsage(env, abortOps, 0);
}
