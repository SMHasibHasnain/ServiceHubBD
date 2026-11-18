import { useState } from "react";
import { useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, Package, Pencil, Plus, Trash2 } from "lucide-react";
import { api } from "../../api/client";
import type { BookingField, BookingFieldType, PriceType, Service } from "../../api/types";
import { flattenCategories, useCategories } from "../../components/shared";
import { Badge, Button, EmptyState, ErrorBox, LoadingList, Modal, PageHeader, useToast } from "../../components/ui";
import { bn, duration, priceLabel } from "../../lib/format";
import { useBiz } from "./Dashboard";

const FIELD_TYPES: { value: BookingFieldType; label: string }[] = [
  { value: "short_text", label: "ছোট লেখা" }, { value: "long_text", label: "বড় লেখা" }, { value: "select", label: "তালিকা থেকে বাছাই" },
  { value: "number", label: "সংখ্যা" }, { value: "phone", label: "মোবাইল নম্বর" }, { value: "date", label: "তারিখ" },
];

type Draft = {
  id?: string; name: string; categoryId: string; shortDescription: string; priceType: PriceType; price: string; priceMax: string;
  durationMinutes: string; availability: Service["availability"]; status: Service["status"]; isFeatured: boolean; internalNotes: string;
  bookingFields: (BookingField & { optionsText: string })[];
};

const blank = (categoryId: number): Draft => ({
  name: "", categoryId: String(categoryId), shortDescription: "", priceType: "fixed", price: "", priceMax: "", durationMinutes: "60",
  availability: "available", status: "active", isFeatured: false, internalNotes: "", bookingFields: [],
});

const fromService = (s: Service): Draft => ({
  id: s.id, name: s.name, categoryId: String(s.categoryId), shortDescription: s.shortDescription ?? "", priceType: s.priceType,
  price: s.price?.toString() ?? "", priceMax: s.priceMax?.toString() ?? "", durationMinutes: String(s.durationMinutes),
  availability: s.availability, status: s.status, isFeatured: s.isFeatured, internalNotes: s.internalNotes ?? "",
  bookingFields: s.bookingFields.map((f) => ({ ...f, optionsText: f.options?.join(", ") ?? "" })),
});

export default function Services() {
  const biz = useBiz();
  const [params, setParams] = useSearchParams();
  const qc = useQueryClient();
  const toast = useToast();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [deleting, setDeleting] = useState<Service | null>(null);
  const canManage = !!biz?.myPermissions.includes("services.manage");

  const list = useQuery({
    queryKey: ["business", biz?.id, "services"],
    queryFn: () => api.get<Service[]>(`/api/businesses/${biz!.id}/services`),
    enabled: !!biz,
  });
  const invalidate = () => qc.invalidateQueries({ queryKey: ["business", biz?.id, "services"] });
  const duplicate = useMutation({ mutationFn: (id: string) => api.post(`/api/businesses/${biz!.id}/services/${id}/duplicate`), onSuccess: () => { invalidate(); toast("সেবার একটি কপি তৈরি হয়েছে (নিষ্ক্রিয়)।"); } });
  const remove = useMutation({
    mutationFn: (id: string) => api.del(`/api/businesses/${biz!.id}/services/${id}`),
    onSuccess: () => { invalidate(); setDeleting(null); toast("সেবাটি মুছে ফেলা হয়েছে।"); },
  });

  return (
    <>
      <PageHeader title="সেবাসমূহ" subtitle="প্রতিটি বুকিং একটি সেবা থেকে শুরু হয়"
        actions={canManage && biz && <Button onClick={() => setDraft(blank(biz.categoryId))}><Plus className="size-4" aria-hidden />নতুন সেবা যোগ করুন</Button>} />
      {params.get("welcome") && (
        <div className="mb-5 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-line bg-zinc-50 p-4">
          <span>আপনার ব্যবসার খসড়া তৈরি হয়েছে। এখন অন্তত একটি সেবা যোগ করুন, তারপর সেটিংস থেকে যাচাইকরণের জন্য জমা দিন।</span>
          <Button variant="ghost" size="sm" onClick={() => setParams({})}>ঠিক আছে</Button>
        </div>
      )}
      <ErrorBox error={list.error ?? duplicate.error} />
      {list.isLoading ? <LoadingList /> : list.data?.length ? (
        <ul className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {list.data.map((s) => (
            <li key={s.id} className="card flex flex-col gap-3 p-5">
              <div className="flex items-start justify-between gap-2">
                <h3 className="text-base font-semibold">{s.name}</h3>
                <div className="flex flex-wrap justify-end gap-1">
                  {s.isFeatured && <Badge tone="amber" className="text-xs">ফিচার্ড</Badge>}
                  <Badge tone={s.status === "active" ? "green" : "neutral"} className="text-xs">{s.status === "active" ? "সক্রিয়" : "নিষ্ক্রিয়"}</Badge>
                </div>
              </div>
              {s.shortDescription && <p className="text-[15px] text-muted">{s.shortDescription}</p>}
              <div className="flex flex-wrap gap-x-4 text-[15px]"><strong>{priceLabel(s.priceType, s.price, s.priceMax)}</strong><span className="text-muted">{duration(s.durationMinutes)}</span></div>
              <div className="text-sm text-subtle">{bn(s.bookingCount)} বার বুক হয়েছে{s.avgRating > 0 && ` · ★ ${bn(s.avgRating)}`}{s.bookingFields.length > 0 && ` · ${bn(s.bookingFields.length)}টি প্রশ্ন`}</div>
              {canManage && (
                <div className="mt-auto flex gap-1 border-t border-zinc-100 pt-3">
                  <Button size="sm" variant="ghost" onClick={() => setDraft(fromService(s))}><Pencil className="size-4" aria-hidden />সম্পাদনা করুন</Button>
                  <Button size="sm" variant="ghost" onClick={() => duplicate.mutate(s.id)} aria-label="কপি করুন"><Copy className="size-4" aria-hidden /></Button>
                  <Button size="sm" variant="ghost" className="ml-auto text-red-700" onClick={() => setDeleting(s)} aria-label="মুছে ফেলুন"><Trash2 className="size-4" aria-hidden /></Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState icon={<Package className="size-8" aria-hidden />} title="এখনও কোনো সেবা যোগ করা হয়নি।"
          action={canManage && biz && <Button onClick={() => setDraft(blank(biz.categoryId))}>নতুন সেবা যোগ করুন</Button>} />
      )}

      {draft && biz && <ServiceEditor businessId={biz.id} draft={draft} setDraft={setDraft} onSaved={() => { invalidate(); setDraft(null); }} />}
      <Modal open={!!deleting} onClose={() => setDeleting(null)} title="আপনি কি নিশ্চিত?"
        footer={<><Button variant="ghost" onClick={() => setDeleting(null)}>বাতিল</Button><Button variant="danger" loading={remove.isPending} onClick={() => deleting && remove.mutate(deleting.id)}>মুছে ফেলুন</Button></>}>
        <ErrorBox error={remove.error} />
        <p className="text-muted">“{deleting?.name}” সেবাটি মুছে ফেললে তা পুনরুদ্ধার করা যাবে না। আগের বুকিংগুলো ইতিহাসে থেকে যাবে।</p>
      </Modal>
    </>
  );
}

function ServiceEditor({ businessId, draft, setDraft, onSaved }: { businessId: string; draft: Draft; setDraft: (d: Draft | null) => void; onSaved: () => void }) {
  const categories = useCategories();
  const toast = useToast();
  const set = (patch: Partial<Draft>) => setDraft({ ...draft, ...patch });
  const setField = (i: number, patch: Partial<Draft["bookingFields"][number]>) =>
    set({ bookingFields: draft.bookingFields.map((f, j) => (j === i ? { ...f, ...patch } : f)) });

  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: draft.name, categoryId: Number(draft.categoryId), shortDescription: draft.shortDescription || null, priceType: draft.priceType,
        price: draft.priceType === "contact_for_price" ? null : Number(draft.price), priceMax: draft.priceType === "range" ? Number(draft.priceMax) : null,
        durationMinutes: Number(draft.durationMinutes), availability: draft.availability, status: draft.status, isFeatured: draft.isFeatured,
        internalNotes: draft.internalNotes || null,
        bookingFields: draft.bookingFields.map((f, i) => ({
          id: f.id, label: f.label, fieldType: f.fieldType, isRequired: f.isRequired, sortOrder: i, placeholder: f.placeholder,
          options: f.fieldType === "select" ? f.optionsText.split(",").map((o) => o.trim()).filter(Boolean) : null,
        })),
      };
      return draft.id ? api.put(`/api/businesses/${businessId}/services/${draft.id}`, body) : api.post(`/api/businesses/${businessId}/services`, body);
    },
    onSuccess: () => { toast(draft.id ? "সেবা হালনাগাদ হয়েছে।" : "নতুন সেবা যোগ হয়েছে।"); onSaved(); },
  });

  return (
    <Modal open onClose={() => setDraft(null)} title={draft.id ? "সেবা সম্পাদনা করুন" : "নতুন সেবা যোগ করুন"} wide
      footer={<><Button variant="ghost" onClick={() => setDraft(null)}>বাতিল</Button><Button loading={save.isPending} onClick={() => save.mutate()}>সংরক্ষণ করুন</Button></>}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={save.error} />
        <label className="label">সেবার নাম<input className="input" value={draft.name} onChange={(e) => set({ name: e.target.value })} placeholder="যেমন: এসি সার্ভিসিং ও গ্যাস চার্জ" /></label>
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="label">ক্যাটাগরি
            <select className="input" value={draft.categoryId} onChange={(e) => set({ categoryId: e.target.value })}>
              {flattenCategories(categories.data).map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}
            </select>
          </label>
          <label className="label">সময়কাল
            <select className="input" value={draft.durationMinutes} onChange={(e) => set({ durationMinutes: e.target.value })}>
              {[15, 30, 45, 60, 90, 120, 180, 240].map((m) => <option key={m} value={m}>{duration(m)}</option>)}
            </select>
          </label>
          <label className="label">মূল্যের ধরন
            <select className="input" value={draft.priceType} onChange={(e) => set({ priceType: e.target.value as PriceType })}>
              <option value="fixed">নির্দিষ্ট মূল্য</option><option value="starting_from">শুরু থেকে</option><option value="range">মূল্যসীমা</option><option value="contact_for_price">মূল্যের জন্য যোগাযোগ</option>
            </select>
          </label>
          {draft.priceType !== "contact_for_price" && (
            <div className="flex gap-2">
              <label className="label flex-1">মূল্য (৳)<input className="input" type="number" min={0} value={draft.price} onChange={(e) => set({ price: e.target.value })} /></label>
              {draft.priceType === "range" && <label className="label flex-1">সর্বোচ্চ (৳)<input className="input" type="number" min={0} value={draft.priceMax} onChange={(e) => set({ priceMax: e.target.value })} /></label>}
            </div>
          )}
        </div>
        <label className="label">সংক্ষিপ্ত বিবরণ <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>
          <textarea className="input" rows={2} maxLength={300} value={draft.shortDescription} onChange={(e) => set({ shortDescription: e.target.value })} />
        </label>
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="label">প্রাপ্যতা
            <select className="input" value={draft.availability} onChange={(e) => set({ availability: e.target.value as Draft["availability"] })}>
              <option value="available">পাওয়া যাচ্ছে</option><option value="unavailable">পাওয়া যাচ্ছে না</option><option value="temporarily_closed">সাময়িক বন্ধ</option><option value="seasonal">মৌসুমি</option>
            </select>
          </label>
          <label className="label">অবস্থা
            <select className="input" value={draft.status} onChange={(e) => set({ status: e.target.value as Draft["status"] })}>
              <option value="active">সক্রিয়</option><option value="inactive">নিষ্ক্রিয়</option>
            </select>
          </label>
        </div>
        <label className="flex min-h-11 items-center gap-2.5"><input type="checkbox" className="size-5 accent-primary" checked={draft.isFeatured} onChange={(e) => set({ isFeatured: e.target.checked })} />ফিচার্ড সেবা (সর্বোচ্চ ৩টি, প্রোফাইলে আগে দেখায়)</label>

        <details className="rounded-md border border-line" open={draft.bookingFields.length > 0}>
          <summary className="flex min-h-11 items-center px-4 font-semibold">বুকিং ফর্মের প্রশ্ন ({bn(draft.bookingFields.length)})</summary>
          <div className="flex flex-col gap-3 border-t border-line p-4">
            <p className="text-sm text-subtle">গ্রাহক বুক করার সময় এই প্রশ্নগুলোর উত্তর দেবেন। যেমন: এসির ব্র্যান্ড, সমস্যার বিবরণ।</p>
            {draft.bookingFields.map((f, i) => (
              <div key={f.id ?? i} className="flex flex-col gap-2 rounded-md bg-bg p-3">
                <div className="flex flex-wrap gap-2">
                  <input className="input min-w-48 flex-1" value={f.label} onChange={(e) => setField(i, { label: e.target.value })} placeholder="প্রশ্ন" aria-label="প্রশ্ন" />
                  <select className="input w-auto" value={f.fieldType} onChange={(e) => setField(i, { fieldType: e.target.value as BookingFieldType })} aria-label="ধরন">
                    {FIELD_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
                  </select>
                  <Button variant="ghost" size="sm" onClick={() => set({ bookingFields: draft.bookingFields.filter((_, j) => j !== i) })} aria-label="প্রশ্ন মুছুন"><Trash2 className="size-4" aria-hidden /></Button>
                </div>
                {f.fieldType === "select" && <input className="input" value={f.optionsText} onChange={(e) => setField(i, { optionsText: e.target.value })} placeholder="অপশনগুলো কমা দিয়ে লিখুন: গ্রী, জেনারেল, স্যামসাং" aria-label="অপশন" />}
                <label className="flex min-h-11 items-center gap-2.5 text-[15px]"><input type="checkbox" className="size-5 accent-primary" checked={f.isRequired} onChange={(e) => setField(i, { isRequired: e.target.checked })} />আবশ্যক</label>
              </div>
            ))}
            <Button variant="outline" size="sm" className="self-start" onClick={() => set({
              bookingFields: [...draft.bookingFields, { id: null, label: "", fieldType: "short_text", options: null, placeholder: null, isRequired: false, sortOrder: draft.bookingFields.length, optionsText: "" }],
            })}><Plus className="size-4" aria-hidden />প্রশ্ন যোগ করুন</Button>
          </div>
        </details>

        <label className="label">অভ্যন্তরীণ নোট <span className="text-sm font-normal text-subtle">(গ্রাহক দেখবেন না)</span>
          <textarea className="input" rows={2} value={draft.internalNotes} onChange={(e) => set({ internalNotes: e.target.value })} />
        </label>
      </div>
    </Modal>
  );
}
