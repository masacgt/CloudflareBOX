import type { Env } from "./types";

export interface BudgetSnapshot {
  currentStoredBytes: number;
  projectedGbMonth: number;
  projectedClassA: number;
  projectedClassB: number;
  storageRatio: number;
  classARatio: number;
  classBRatio: number;
  stopRatio: number;
  uploadAllowed: boolean;
}

function utcDay(now = new Date()): string {
  return now.toISOString().slice(0, 10);
}

export async function recordUsage(env: Env, deltaA = 0, deltaB = 0): Promise<void> {
  const now = Math.floor(Date.now() / 1000);
  const day = utcDay();
  const stored = await env.DB.prepare("SELECT COALESCE(SUM(encrypted_size_bytes),0) AS n FROM transfers WHERE state IN ('UPLOADING','R2_READY','PC_DOWNLOADING','VERIFYING','DELETE_PENDING')").first<{ n: number }>();
  await env.DB.prepare("INSERT INTO usage_daily(day, peak_stored_bytes, class_a_ops, class_b_ops, updated_at) VALUES(?,?,?,?,?) ON CONFLICT(day) DO UPDATE SET peak_stored_bytes = MAX(usage_daily.peak_stored_bytes, excluded.peak_stored_bytes), class_a_ops = usage_daily.class_a_ops + excluded.class_a_ops, class_b_ops = usage_daily.class_b_ops + excluded.class_b_ops, updated_at = excluded.updated_at")
    .bind(day, Number(stored?.n ?? 0), deltaA, deltaB, now).run();
}

export async function budgetSnapshot(env: Env, incomingBytes = 0, extraA = 0, extraB = 0): Promise<BudgetSnapshot> {
  const now = new Date();
  const today = utcDay(now);
  const month = today.slice(0, 7);
  const dayOfMonth = now.getUTCDate();
  const daysInMonth = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 0)).getUTCDate();
  const rows = await env.DB.prepare("SELECT day,peak_stored_bytes,class_a_ops,class_b_ops FROM usage_daily WHERE day LIKE ? ORDER BY day")
    .bind(`${month}-%`).all<{ day: string; peak_stored_bytes: number; class_a_ops: number; class_b_ops: number }>();
  const stored = await env.DB.prepare("SELECT COALESCE(SUM(encrypted_size_bytes),0) AS n FROM transfers WHERE state IN ('UPLOADING','R2_READY','PC_DOWNLOADING','VERIFYING','DELETE_PENDING')").first<{ n: number }>();
  const currentStoredBytes = Number(stored?.n ?? 0);
  const previous = rows.results.filter((r) => r.day !== today);
  const previousPeakSum = previous.reduce((sum, r) => sum + Number(r.peak_stored_bytes), 0);
  const historicalAvgPeak = previous.length ? previousPeakSum / previous.length : 0;
  const todayRow = rows.results.find((r) => r.day === today);
  const prospectiveTodayPeak = Math.max(Number(todayRow?.peak_stored_bytes ?? 0), currentStoredBytes + incomingBytes);
  const futureDays = Math.max(0, daysInMonth - dayOfMonth);
  const projectedGbMonth = (previousPeakSum + prospectiveTodayPeak + historicalAvgPeak * futureDays) / daysInMonth / 1_000_000_000;
  const actualA = rows.results.reduce((sum, r) => sum + Number(r.class_a_ops), 0) + extraA;
  const actualB = rows.results.reduce((sum, r) => sum + Number(r.class_b_ops), 0) + extraB;
  const scale = dayOfMonth > 0 ? daysInMonth / dayOfMonth : 1;
  const projectedClassA = Math.ceil(actualA * scale);
  const projectedClassB = Math.ceil(actualB * scale);
  const freeStorage = Number(env.FREE_STORAGE_GB_MONTH);
  const freeA = Number(env.FREE_CLASS_A);
  const freeB = Number(env.FREE_CLASS_B);
  const stopRatio = Number(env.FREE_STOP_RATIO);
  const storageRatio = freeStorage > 0 ? projectedGbMonth / freeStorage : 1;
  const classARatio = freeA > 0 ? projectedClassA / freeA : 1;
  const classBRatio = freeB > 0 ? projectedClassB / freeB : 1;
  return {
    currentStoredBytes,
    projectedGbMonth,
    projectedClassA,
    projectedClassB,
    storageRatio,
    classARatio,
    classBRatio,
    stopRatio,
    uploadAllowed: Math.max(storageRatio, classARatio, classBRatio) < stopRatio,
  };
}
