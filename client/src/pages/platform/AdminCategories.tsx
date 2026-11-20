import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Eye, EyeOff, Pencil, Plus, Trash2 } from "lucide-react";
import { api } from "../../api/client";
import type { AdminCategory, RecordStatus } from "../../api/types";
import { CategoryTile } from "../../components/brand";
import { Badge, Button, EmptyState, ErrorBox, Field, LoadingList, Modal, PageHeader, useToast } from "../../components/ui";
import { bn } from "../../lib/format";

const STATUS: Record<RecordStatus, { label: string; tone: "green" | "neutral" | "amber" }> = {
  active: { label: "সক্রিয়", tone: "green" },
  inactive: { label: "লুকানো", tone: "neutral" },
  archived: { label: "আর্কাইভ", tone: "amber" },
};

/** "AC Repair & Service" → "ac-repair-service" */
const slugify = (v: string) => v.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 100);

type Form = { id?: number; nameBn: string; nameEn: string; slug: string; parentId: string; sortOrder: string; status: RecordStatus; icon: string | null };
const blank: Form = { nameBn: "", nameEn: "", slug: "", parentId: "", sortOrder: "0", status: "active", icon: null };

/**
 * Super admin: categories shown to customers and picked by businesses. Hidden ("inactive") categories disappear
 * from the public site but keep their businesses; only an unused category can be deleted.
 */
export default function AdminCategories() {
  const qc = useQueryClient();
  const toast = useToast();
  const list = useQuery({ queryKey: ["admin", "categories"], queryFn: () => api.get<AdminCategory[]>("/api/admin/categories") });
  const [form, setForm] = useState<Form | null>(null);
  const [slugTouched, setSlugTouched] = useState(false);
  const [deleting, setDeleting] = useState<AdminCategory | null>(null);

  const refresh = () => { qc.invalidateQueries({ queryKey: ["admin", "categories"] }); qc.invalidateQueries({ queryKey: ["categories"] }); };
  const body = (f: Form) => ({
    nameBn: f.nameBn.trim(), nameEn: f.nameEn.trim(), slug: f.slug.trim(), icon: f.icon,
    parentId: f.parentId ? Number(f.parentId) : null, sortOrder: Number(f.sortOrder) || 0, status: f.status,
  });
  const saveCat = useMutation({
    mutationFn: (f: Form) => f.id ? api.put("/api/admin/categories", { id: f.id, ...body(f) }) : api.post("/api/admin/categories", body(f)),
    onSuccess: (_, f) => { refresh(); setForm(null); toast(f.id ? "ক্যাটাগরি হালনাগাদ হয়েছে।" : "নতুন ক্যাটাগরি যোগ হয়েছে।"); },
  });
  const toggle = useMutation({
    mutationFn: (c: AdminCategory) => api.put("/api/admin/categories", { ...c, status: c.status === "active" ? "inactive" : "active" }),
    onSuccess: (_, c) => { refresh(); toast(c.status === "active" ? `“${c.nameBn}” লুকানো হয়েছে।` : `“${c.nameBn}” আবার দেখা যাবে।`); },
  });
  const del = useMutation({
    mutationFn: (c: AdminCategory) => api.del(`/api/admin/categories/${c.id}`),
    onSuccess: (_, c) => { refresh(); setDeleting(null); toast(`“${c.nameBn}” মুছে ফেলা হয়েছে।`); },
  });

  const all = list.data ?? [];
  const parents = all.filter((c) => c.parentId === null);
  const childrenOf = (id: number) => all.filter((c) => c.parentId === id);

  const open = (f: Form) => { saveCat.reset(); setSlugTouched(!!f.id); setForm(f); };
  const submit = (e: FormEvent) => { e.preventDefault(); if (form) saveCat.mutate(form); };

  const row = (c: AdminCategory, child = false) => {
    const s = STATUS[c.status];
    const canDelete = c.businessCount === 0 && c.serviceCount === 0 && c.childCount === 0;
    return (
      <tr key={c.id} className={c.status === "active" ? "" : "bg-zinc-50/60"}>
        <td className="px-5 py-3">
          <div className={`flex items-center gap-3 ${child ? "pl-8" : ""}`}>
            {!child && <CategoryTile id={c.id} name={c.nameBn} className="size-10 text-xl" />}
            <div className="min-w-0">
              <div className="font-semibold">{child && <span className="text-subtle">↳ </span>}{c.nameBn}</div>
              <div className="text-sm text-subtle">{c.nameEn} · <span className="font-num" dir="ltr">{c.slug}</span></div>
            </div>
          </div>
        </td>
        <td className="px-3 py-3"><Badge tone={s.tone}>{s.label}</Badge></td>
        <td className="px-3 py-3 font-num text-[15px]">{bn(c.sortOrder)}</td>
        <td className="px-3 py-3 text-[15px]">{bn(c.businessCount)} ব্যবসা · {bn(c.serviceCount)} সেবা</td>
        <td className="px-5 py-3">
          <div className="flex justify-end gap-1.5">
            <Button size="sm" variant="ghost" aria-label={`${c.nameBn} সম্পাদনা`} onClick={() => open({ id: c.id, nameBn: c.nameBn, nameEn: c.nameEn, slug: c.slug, parentId: c.parentId ? String(c.parentId) : "", sortOrder: String(c.sortOrder), status: c.status, icon: c.icon })}>
              <Pencil className="size-4" aria-hidden />সম্পাদনা
            </Button>
            <Button size="sm" variant="ghost" loading={toggle.isPending && toggle.variables?.id === c.id} onClick={() => toggle.mutate(c)}>
              {c.status === "active" ? <><EyeOff className="size-4" aria-hidden />লুকান</> : <><Eye className="size-4" aria-hidden />দেখান</>}
            </Button>
            <Button size="sm" variant="danger" disabled={!canDelete} title={canDelete ? undefined : "ব্যবহৃত ক্যাটাগরি মোছা যায় না, লুকিয়ে রাখুন"} aria-label={`${c.nameBn} মুছুন`} onClick={() => { del.reset(); setDeleting(c); }}>
              <Trash2 className="size-4" aria-hidden />
            </Button>
          </div>
        </td>
      </tr>
    );
  };

  return (
    <>
      <PageHeader title="ক্যাটাগরি" subtitle="গ্রাহকেরা যে ক্যাটাগরি দেখে আর ব্যবসা যেখানে যুক্ত হয়। লুকানো ক্যাটাগরি সাইটে দেখায় না, কিন্তু তার ব্যবসাগুলো থেকে যায়।"
        actions={<Button onClick={() => open(blank)}><Plus className="size-4" aria-hidden />নতুন ক্যাটাগরি</Button>} />
      <ErrorBox error={list.error ?? toggle.error} />
      {list.isLoading ? <LoadingList /> : !all.length ? (
        <EmptyState title="এখনও কোনো ক্যাটাগরি নেই" action={<Button onClick={() => open(blank)}>প্রথম ক্যাটাগরি যোগ করুন</Button>} />
      ) : (
        <div className="card overflow-x-auto">
          <table className="w-full min-w-[820px] text-left">
            <thead className="border-b border-line text-sm text-subtle">
              <tr><th className="px-5 py-3 font-medium">ক্যাটাগরি</th><th className="px-3 py-3 font-medium">অবস্থা</th><th className="px-3 py-3 font-medium">ক্রম</th><th className="px-3 py-3 font-medium">ব্যবহার</th><th className="px-5 py-3"><span className="sr-only">কাজ</span></th></tr>
            </thead>
            <tbody className="divide-y divide-zinc-100">
              {parents.flatMap((p) => [row(p), ...childrenOf(p.id).map((c) => row(c, true))])}
            </tbody>
          </table>
        </div>
      )}

      <Modal open={!!form} onClose={() => setForm(null)} title={form?.id ? "ক্যাটাগরি সম্পাদনা" : "নতুন ক্যাটাগরি"}
        footer={<><Button variant="ghost" onClick={() => setForm(null)}>বাতিল</Button><Button type="submit" form="category-form" loading={saveCat.isPending}>সংরক্ষণ করুন</Button></>}>
        {form && (
          <form id="category-form" onSubmit={submit} className="flex flex-col gap-4">
            <ErrorBox error={saveCat.error} />
            <Field label="বাংলা নাম">{(id) => <input id={id} className="input" value={form.nameBn} required maxLength={80} onChange={(e) => setForm({ ...form, nameBn: e.target.value })} placeholder="যেমন: এসি সার্ভিস" />}</Field>
            <Field label="ইংরেজি নাম">{(id) => (
              <input id={id} className="input" dir="ltr" value={form.nameEn} required maxLength={80} placeholder="AC Service"
                onChange={(e) => setForm({ ...form, nameEn: e.target.value, slug: slugTouched ? form.slug : slugify(e.target.value) })} />
            )}</Field>
            <Field label="Slug (লিংকের অংশ)" hint="ছোট হাতের ইংরেজি অক্ষর, সংখ্যা আর - চিহ্ন">{(id) => (
              <input id={id} className="input font-num" dir="ltr" value={form.slug} required pattern="[a-z0-9-]{2,100}"
                onChange={(e) => { setSlugTouched(true); setForm({ ...form, slug: e.target.value }); }} />
            )}</Field>
            <Field label="মূল ক্যাটাগরি" hint="খালি রাখলে এটি প্রধান ক্যাটাগরি হবে">{(id) => (
              <select id={id} className="input" value={form.parentId} onChange={(e) => setForm({ ...form, parentId: e.target.value })}>
                <option value="">কোনোটি না (প্রধান ক্যাটাগরি)</option>
                {parents.filter((p) => p.id !== form.id).map((p) => <option key={p.id} value={p.id}>{p.nameBn}</option>)}
              </select>
            )}</Field>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="ক্রম" hint="ছোট সংখ্যা আগে দেখায়">{(id) => <input id={id} className="input font-num" type="number" value={form.sortOrder} onChange={(e) => setForm({ ...form, sortOrder: e.target.value })} />}</Field>
              <Field label="অবস্থা">{(id) => (
                <select id={id} className="input" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as RecordStatus })}>
                  <option value="active">সক্রিয় (সাইটে দেখায়)</option>
                  <option value="inactive">লুকানো</option>
                  <option value="archived">আর্কাইভ</option>
                </select>
              )}</Field>
            </div>
          </form>
        )}
      </Modal>

      <Modal open={!!deleting} onClose={() => setDeleting(null)} title="ক্যাটাগরি মুছবেন?"
        footer={<><Button variant="ghost" onClick={() => setDeleting(null)}>বাতিল</Button><Button variant="danger" loading={del.isPending} onClick={() => deleting && del.mutate(deleting)}>মুছুন</Button></>}>
        <ErrorBox error={del.error} />
        <p className="text-muted">“{deleting?.nameBn}” স্থায়ীভাবে মুছে যাবে। এটি ফেরানো যায় না।</p>
      </Modal>
    </>
  );
}
