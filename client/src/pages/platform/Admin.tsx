import { useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { UserPlus } from "lucide-react";
import { api } from "../../api/client";
import type { AdminUser, Assignment, AuditLogItem, Paged, Role } from "../../api/types";
import { flattenCategories, useCategories, ZonePicker } from "../../components/shared";
import { Avatar, Badge, Button, ErrorBox, LoadingList, Modal, PAGE_SIZE, PageHeader, Pagination, usePage, useToast } from "../../components/ui";
import { ROLE_NAMES, bn, dateShort, dateTime, relative } from "../../lib/format";

export function AdminUsers() {
  const qc = useQueryClient();
  const toast = useToast();
  const [q, setQ] = useState("");
  const [acting, setActing] = useState<{ user: AdminUser; action: "suspend" | "activate" } | null>(null);
  const [granting, setGranting] = useState<AdminUser | null>(null);
  const [reason, setReason] = useState("");
  const [page, setPage] = usePage(q);
  const users = useQuery({ queryKey: ["admin", "users", q, page], queryFn: () => api.get<Paged<AdminUser>>("/api/admin/users", { q, page, pageSize: PAGE_SIZE }), placeholderData: keepPreviousData });
  const act = useMutation({
    mutationFn: () => api.post(`/api/admin/users/${acting!.user.id}/${acting!.action}`, { reason }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["admin", "users"] }); toast(acting!.action === "suspend" ? "অ্যাকাউন্ট স্থগিত করা হয়েছে।" : "অ্যাকাউন্ট সক্রিয় করা হয়েছে।"); setActing(null); setReason(""); },
  });

  return (
    <>
      <PageHeader title="ব্যবহারকারী" subtitle="অ্যাকাউন্ট ব্যবস্থাপনা — প্রতিটি সংবেদনশীল কাজ অডিট লগে সংরক্ষিত" />
      <input className="input mb-5 max-w-md" value={q} onChange={(e) => setQ(e.target.value)} placeholder="নাম, ইমেইল বা মোবাইল নম্বর" aria-label="ব্যবহারকারী খুঁজুন" />
      <ErrorBox error={users.error} />
      {users.isLoading ? <LoadingList /> : (
        <div className="card overflow-x-auto">
          <table className="w-full min-w-[760px] text-left">
            <thead className="border-b border-line text-sm text-subtle"><tr><th className="px-5 py-3 font-medium">ব্যবহারকারী</th><th className="px-3 py-3 font-medium">ভূমিকা</th><th className="px-3 py-3 font-medium">ব্যবসা</th><th className="px-3 py-3 font-medium">শেষ লগইন</th><th className="px-5 py-3"><span className="sr-only">কাজ</span></th></tr></thead>
            <tbody className="divide-y divide-zinc-100">
              {users.data?.items.map((u) => (
                <tr key={u.id}>
                  <td className="px-5 py-3"><div className="flex items-center gap-3"><Avatar name={u.fullName} tone="violet" className="size-10 rounded-full" />
                    <div><div className="font-semibold">{u.fullName} {u.status !== "active" && <Badge tone="red" className="text-xs">স্থগিত</Badge>}</div><div className="font-num text-sm text-subtle">{u.phone}</div></div></div></td>
                  <td className="px-3 py-3"><div className="flex flex-wrap gap-1">{u.platformRoles.length ? u.platformRoles.map((r) => <Badge key={r} tone="blue" className="text-xs">{ROLE_NAMES[r] ?? r}</Badge>) : <span className="text-subtle">গ্রাহক</span>}</div></td>
                  <td className="px-3 py-3 font-num">{bn(u.businessesOwned)}</td>
                  <td className="px-3 py-3 text-[15px]">{u.lastLoginAt ? relative(u.lastLoginAt) : "—"}</td>
                  <td className="px-5 py-3"><div className="flex justify-end gap-2">
                    <Button size="sm" variant="outline" onClick={() => setGranting(u)}><UserPlus className="size-4" aria-hidden />ভূমিকা দিন</Button>
                    {u.status === "active"
                      ? <Button size="sm" variant="danger" onClick={() => setActing({ user: u, action: "suspend" })}>স্থগিত</Button>
                      : <Button size="sm" variant="outline" onClick={() => setActing({ user: u, action: "activate" })}>সক্রিয় করুন</Button>}
                  </div></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {users.data && <Pagination page={page} total={users.data.total} onChange={setPage} label="ব্যবহারকারীর পাতা" />}
      <Modal open={!!acting} onClose={() => setActing(null)} title={acting?.action === "suspend" ? "অ্যাকাউন্ট স্থগিত করবেন?" : "অ্যাকাউন্ট সক্রিয় করবেন?"}
        footer={<><Button variant="ghost" onClick={() => setActing(null)}>বাতিল</Button><Button variant={acting?.action === "suspend" ? "danger" : "primary"} disabled={reason.trim().length < 5} loading={act.isPending} onClick={() => act.mutate()}>নিশ্চিত করুন</Button></>}>
        <div className="flex flex-col gap-3">
          <ErrorBox error={act.error} />
          {acting?.action === "suspend" && <p className="text-muted">{acting.user.fullName} সব ডিভাইস থেকে সঙ্গে সঙ্গে লগআউট হয়ে যাবেন।</p>}
          <label className="label">কারণ<textarea className="input" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
        </div>
      </Modal>
      {granting && <GrantRole user={granting} onClose={() => setGranting(null)} />}
    </>
  );
}

function GrantRole({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const categories = useCategories();
  const roles = useQuery({ queryKey: ["roles", "platform"], queryFn: () => api.get<Role[]>("/api/roles") });
  const platformRoles = (roles.data ?? []).filter((r) => r.scope !== "business");
  const [roleKey, setRoleKey] = useState("system_moderator");
  const [zoneId, setZoneId] = useState<number | null>(null);
  const [categoryId, setCategoryId] = useState("");
  const role = platformRoles.find((r) => r.key === roleKey);
  const adminRole = roleKey === "admin" || roleKey === "super_admin";
  const grant = useMutation({
    mutationFn: () => api.post("/api/admin/role-assignments", { userId: user.id, roleKey, zoneId: adminRole ? null : zoneId, categoryId: adminRole || !categoryId ? null : Number(categoryId) }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["admin"] }); toast("ভূমিকা দেওয়া হয়েছে।"); onClose(); },
  });
  return (
    <Modal open onClose={onClose} title={`${user.fullName} — ভূমিকা দিন`}
      footer={<><Button variant="ghost" onClick={onClose}>বাতিল</Button><Button disabled={role?.scope === "zone" && !zoneId} loading={grant.isPending} onClick={() => grant.mutate()}>ভূমিকা দিন</Button></>}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={grant.error} />
        <label className="label">ভূমিকা<select className="input" value={roleKey} onChange={(e) => setRoleKey(e.target.value)}>{platformRoles.map((r) => <option key={r.key} value={r.key}>{r.nameBn}</option>)}</select></label>
        {!adminRole && (
          <>
            <fieldset><legend className="mb-1.5 font-medium">এলাকা {role?.scope === "zone" ? <span className="text-sm font-normal text-red-700">আবশ্যক</span> : <span className="text-sm font-normal text-subtle">(না দিলে সারা দেশ)</span>}</legend>
              <ZonePicker value={zoneId} minDepth={role?.scope === "zone" ? 1 : 0} onChange={setZoneId} />
              <p className="mt-1 text-sm text-subtle">নির্বাচিত এলাকা ও এর ভেতরের সব এলাকা অন্তর্ভুক্ত হবে।</p>
            </fieldset>
            <label className="label">ক্যাটাগরি <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>
              <select className="input" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}><option value="">সব ক্যাটাগরি</option>{flattenCategories(categories.data).map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}</select>
            </label>
          </>
        )}
      </div>
    </Modal>
  );
}

export function AdminRoles() {
  const qc = useQueryClient();
  const toast = useToast();
  const [revoking, setRevoking] = useState<Assignment | null>(null);
  const [reason, setReason] = useState("");
  const [page, setPage] = usePage();
  const list = useQuery({
    queryKey: ["admin", "assignments", page],
    queryFn: () => api.get<Paged<Assignment>>("/api/admin/role-assignments", { page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const roles = useQuery({ queryKey: ["roles", "all"], queryFn: () => api.get<Role[]>("/api/roles") });
  const revoke = useMutation({
    mutationFn: () => api.post(`/api/admin/role-assignments/${revoking!.id}/revoke`, { reason }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["admin"] }); setRevoking(null); setReason(""); toast("ভূমিকা প্রত্যাহার করা হয়েছে।"); },
  });

  return (
    <>
      <PageHeader title="ভূমিকা ও অনুমতি" subtitle="নতুন ভূমিকা দিতে ‘ব্যবহারকারী’ পাতা থেকে ব্যক্তিকে খুঁজুন" />
      <ErrorBox error={list.error} />
      <section className="mb-8">
        <h2 className="mb-3 text-base font-semibold">সক্রিয় প্ল্যাটফর্ম ভূমিকা</h2>
        {list.isLoading ? <LoadingList /> : (
          <ul className="card divide-y divide-zinc-100">
            {list.data?.items.map((a) => (
              <li key={a.id} className="flex flex-wrap items-center gap-3 px-5 py-3">
                <span className="min-w-40 flex-1 font-semibold">{a.userName}</span>
                <Badge tone="blue">{a.roleName}</Badge>
                <span className="text-[15px] text-muted">{a.zoneName ?? "সারা দেশ"}{a.categoryName && ` · ${a.categoryName}`}</span>
                <span className="text-sm text-subtle">{dateShort(a.validFrom)} থেকে</span>
                <Button size="sm" variant="danger" onClick={() => setRevoking(a)}>প্রত্যাহার</Button>
              </li>
            ))}
          </ul>
        )}
        {list.data && <Pagination page={page} total={list.data.total} onChange={setPage} scrollTop={false} label="ভূমিকার পাতা" />}
      </section>
      <section>
        <h2 className="mb-3 text-base font-semibold">ভূমিকার তালিকা</h2>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {roles.data?.map((r) => (
            <div key={r.key} className="card p-5">
              <div className="mb-2 flex items-center justify-between gap-2"><span className="font-semibold">{r.nameBn}</span><Badge className="text-xs">{{ platform: "প্ল্যাটফর্ম", zone: "এলাকা", business: "ব্যবসা পেজ" }[r.scope]}</Badge></div>
              <ul className="flex flex-wrap gap-1">{r.permissions.map((p) => <li key={p}><code className="rounded bg-zinc-100 px-1.5 py-0.5 text-xs">{p}</code></li>)}</ul>
            </div>
          ))}
        </div>
      </section>
      <Modal open={!!revoking} onClose={() => setRevoking(null)} title="ভূমিকা প্রত্যাহার করবেন?"
        footer={<><Button variant="ghost" onClick={() => setRevoking(null)}>বাতিল</Button><Button variant="danger" disabled={reason.trim().length < 5} loading={revoke.isPending} onClick={() => revoke.mutate()}>প্রত্যাহার করুন</Button></>}>
        <div className="flex flex-col gap-3">
          <ErrorBox error={revoke.error} />
          <p className="text-muted">{revoking?.userName} — {revoking?.roleName} ({revoking?.zoneName ?? "সারা দেশ"})</p>
          <label className="label">কারণ<textarea className="input" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
        </div>
      </Modal>
    </>
  );
}

export function AdminAudit() {
  const [page, setPage] = usePage();
  const logs = useQuery({
    queryKey: ["admin", "audit", page],
    queryFn: () => api.get<Paged<AuditLogItem>>("/api/admin/audit-logs", { page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  return (
    <>
      <PageHeader title="অডিট লগ" subtitle="প্রতিটি সংবেদনশীল কাজের অপরিবর্তনীয় রেকর্ড" />
      <ErrorBox error={logs.error} />
      {logs.isLoading ? <LoadingList /> : (
        <div className="card overflow-x-auto">
          <table className="w-full min-w-[720px] text-left text-[15px]">
            <thead className="border-b border-line text-sm text-subtle"><tr><th className="px-5 py-3 font-medium">সময়</th><th className="px-3 py-3 font-medium">কে</th><th className="px-3 py-3 font-medium">কাজ</th><th className="px-3 py-3 font-medium">কারণ / পরিবর্তন</th></tr></thead>
            <tbody className="divide-y divide-zinc-100">
              {logs.data?.items.map((l) => (
                <tr key={l.id}>
                  <td className="px-5 py-2.5 whitespace-nowrap text-subtle">{dateTime(l.createdAt)}</td>
                  <td className="px-3 py-2.5">{l.actorName ?? "সিস্টেম"}</td>
                  <td className="px-3 py-2.5"><code className="rounded bg-zinc-100 px-1.5 py-0.5 text-sm">{l.action}</code></td>
                  <td className="px-3 py-2.5 text-muted">{l.reason ?? (l.changes ? Object.entries(l.changes).map(([k, v]) => `${k}: ${v}`).join(", ") : "—")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {logs.data && <Pagination page={page} total={logs.data.total} onChange={setPage} label="অডিট লগের পাতা" />}
    </>
  );
}
