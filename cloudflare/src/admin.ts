import type { Env } from "./types";
import { budgetSnapshot } from "./usage";

function esc(value: unknown): string {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c] ?? c));
}

function authorized(request: Request, env: Env): boolean {
  const email = request.headers.get("Cf-Access-Authenticated-User-Email") ?? "";
  return !!email && email.toLowerCase() === env.ADMIN_EMAIL.toLowerCase();
}

function sameOrigin(request: Request): boolean {
  const origin = request.headers.get("Origin");
  return !origin || origin === new URL(request.url).origin;
}

export async function adminPage(request: Request, env: Env): Promise<Response> {
  if (!authorized(request, env)) return new Response("Forbidden", { status: 403 });
  const url = new URL(request.url);
  if (request.method === "POST") {
    if (!sameOrigin(request)) return new Response("Forbidden", { status: 403 });
    if (url.pathname === "/admin/revoke-device") {
      const form = await request.formData();
      const id = String(form.get("id") ?? "");
      if (!id) return new Response("Bad request", { status: 400 });
      await env.DB.prepare("UPDATE devices SET revoked_at=? WHERE id=? AND revoked_at IS NULL")
        .bind(Math.floor(Date.now() / 1000), id).run();
      return Response.redirect(`${url.origin}/admin`, 303);
    }
    if (url.pathname === "/admin/revoke-all") {
      await env.DB.prepare("UPDATE devices SET revoked_at=? WHERE revoked_at IS NULL")
        .bind(Math.floor(Date.now() / 1000)).run();
      return Response.redirect(`${url.origin}/admin`, 303);
    }
    return new Response("Not found", { status: 404 });
  }
  if (request.method !== "GET" || url.pathname !== "/admin") return new Response("Not found", { status: 404 });

  const transfers = await env.DB.prepare("SELECT id,size_bytes,state,created_at,updated_at,error_code FROM transfers ORDER BY created_at DESC LIMIT 100").all();
  const devices = await env.DB.prepare("SELECT id,kind,name,revoked_at,last_seen_at,created_at FROM devices ORDER BY created_at DESC").all();
  const budget = await budgetSnapshot(env);
  const rows = transfers.results.map((t: any) => `<tr><td>${esc(t.id)}</td><td>${esc(t.state)}</td><td>${Number(t.size_bytes).toLocaleString()}</td><td>${new Date(Number(t.updated_at)*1000).toLocaleString()}</td><td>${esc(t.error_code)}</td></tr>`).join("");
  const deviceRows = devices.results.map((d: any) => `<tr><td>${esc(d.kind)}</td><td>${esc(d.name)}</td><td>${esc(d.id)}</td><td>${d.revoked_at ? "revoked" : "active"}</td><td>${d.last_seen_at ? new Date(Number(d.last_seen_at)*1000).toLocaleString() : "-"}</td><td>${d.revoked_at ? "-" : `<form method="post" action="/admin/revoke-device"><input type="hidden" name="id" value="${esc(d.id)}"><button type="submit">失効</button></form>`}</td></tr>`).join("");
  const maxRatio = Math.max(budget.storageRatio, budget.classARatio, budget.classBRatio);
  const html = `<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>CloudflareBOX Admin</title><style>body{font-family:system-ui;margin:32px;max-width:1200px}table{border-collapse:collapse;width:100%;margin-bottom:32px}th,td{padding:8px;border-bottom:1px solid #ddd;text-align:left;font-size:13px}.muted{color:#666}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px;margin:20px 0}.card{padding:14px;border:1px solid #ddd;border-radius:10px}button{padding:6px 10px}</style><h1>CloudflareBOX Admin</h1><p class="muted">Cloudflare Access protected control view. Secrets are never rendered.</p><div class="grid"><div class="card"><b>Stored</b><br>${budget.currentStoredBytes.toLocaleString()} bytes</div><div class="card"><b>Projected GB-month</b><br>${budget.projectedGbMonth.toFixed(3)}</div><div class="card"><b>Projected Class A</b><br>${budget.projectedClassA.toLocaleString()}</div><div class="card"><b>Projected Class B</b><br>${budget.projectedClassB.toLocaleString()}</div><div class="card"><b>Safety ratio</b><br>${(maxRatio*100).toFixed(1)}%</div></div><h2>Devices</h2><form method="post" action="/admin/revoke-all" onsubmit="return confirm('全端末を失効し、再ペアリングが必要になります。続行しますか？')"><button type="submit">全端末を失効（再ペアリング）</button></form><table><tr><th>Kind</th><th>Name</th><th>ID</th><th>Status</th><th>Last seen</th><th>Action</th></tr>${deviceRows}</table><h2>Recent transfers</h2><table><tr><th>ID</th><th>State</th><th>Bytes</th><th>Updated</th><th>Error</th></tr>${rows}</table>`;
  return new Response(html, { headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store", "content-security-policy": "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'" } });
}
