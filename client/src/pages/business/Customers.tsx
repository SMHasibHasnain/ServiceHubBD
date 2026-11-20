import { useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Crown, Phone, Users } from "lucide-react";
import { api } from "../../api/client";
import type { Customer, CustomerDetail, Paged } from "../../api/types";
import { Avatar, Badge, Button, EmptyState, ErrorBox, LoadingList, Modal, PAGE_SIZE, PageHeader, Pagination, Segmented, useClientPage, usePage, useToast } from "../../components/ui";
import { BOOKING_STATUS, bn, dateShort, relative, taka } from "../../lib/format";
import { useBiz } from "./Dashboard";

type Segment = "all" | "new" | "repeat" | "vip" | "inactive";
const BADGE: Record<string, { label: string; tone: "amber" | "blue" | "teal" | "neutral" }> = {
  vip: { label: "VIP", tone: "amber" }, repeat: { label: "পুনরাবৃত্ত গ্রাহক", tone: "blue" }, new: { label: "নতুন গ্রাহক", tone: "teal" }, inactive: { label: "নিষ্ক্রিয়", tone: "neutral" },
};

export default function Customers() {
  const biz = useBiz();
  const [segment, setSegment] = useState<Segment>("all");
  const [q, setQ] = useState("");
  const [open, setOpen] = useState<string | null>(null);
  const [page, setPage] = usePage({ segment, q });
  const list = useQuery({
    queryKey: ["business", biz?.id, "customers", { segment, q, page }],
    queryFn: () => api.get<Paged<Customer>>(`/api/businesses/${biz!.id}/customers`, { segment, q, page, pageSize: PAGE_SIZE }),
    enabled: !!biz,
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="গ্রাহক" subtitle={list.data ? `মোট ${bn(list.data.total)} জন গ্রাহক` : "আপনার গ্রাহকদের চিনুন, ধরে রাখুন"} />
      <div className="mb-5 flex flex-wrap items-center gap-3">
        <Segmented label="গ্রাহকের ধরন" value={segment} onChange={setSegment}
          options={[{ value: "all", label: "সব" }, { value: "new", label: "নতুন" }, { value: "repeat", label: "পুনরাবৃত্ত" }, { value: "vip", label: "VIP" }, { value: "inactive", label: "নিষ্ক্রিয়" }]} />
        <input className="input min-w-56 flex-1" value={q} onChange={(e) => setQ(e.target.value)} placeholder="নাম বা মোবাইল নম্বর" aria-label="গ্রাহক খুঁজুন" />
      </div>
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <div className="card overflow-x-auto">
          <table className="w-full min-w-[720px] text-left">
            <thead className="border-b border-line text-sm text-subtle">
              <tr><th className="px-5 py-3 font-medium">গ্রাহক</th><th className="px-3 py-3 font-medium">শেষ ভিজিট</th><th className="px-3 py-3 font-medium">পছন্দের সেবা</th><th className="px-3 py-3 font-medium">বুকিং</th><th className="px-3 py-3 font-medium">মোট খরচ</th><th className="px-5 py-3"><span className="sr-only">বিস্তারিত</span></th></tr>
            </thead>
            <tbody className="divide-y divide-zinc-100">
              {list.data.items.map((c) => (
                <tr key={c.id} className="hover:bg-zinc-50">
                  <td className="px-5 py-3">
                    <div className="flex items-center gap-3">
                      <Avatar name={c.displayName} tone="violet" className="size-10 rounded-full" />
                      <div><div className="flex items-center gap-1.5 font-semibold">{c.displayName}{c.isVip && <Crown className="size-4 text-amber-600" aria-label="VIP" />}</div><div className="font-num text-sm text-subtle">{c.phone}</div></div>
                    </div>
                  </td>
                  <td className="px-3 py-3 text-[15px]">{c.lastVisitAt ? relative(c.lastVisitAt) : "—"}</td>
                  <td className="px-3 py-3 text-[15px]">{c.favouriteService ?? "—"}</td>
                  <td className="px-3 py-3 font-num">{bn(c.completedBookings)}/{bn(c.totalBookings)}</td>
                  <td className="px-3 py-3 font-num">{taka(c.lifetimeValue)}</td>
                  <td className="px-5 py-3 text-right"><Button size="sm" variant="outline" onClick={() => setOpen(c.id)}>বিস্তারিত</Button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : <EmptyState icon={<Users className="size-8" aria-hidden />} title="এখনও কোনো গ্রাহক নেই" description="প্রথম বুকিং এলেই গ্রাহক এখানে স্বয়ংক্রিয়ভাবে যুক্ত হবেন।" />}
      {list.data && <Pagination page={page} total={list.data.total} onChange={setPage} label="গ্রাহকের পাতা" />}
      {open && biz && <CustomerPanel businessId={biz.id} customerId={open} permissions={biz.myPermissions} onClose={() => setOpen(null)} />}
    </>
  );
}

function CustomerPanel({ businessId, customerId, permissions, onClose }: { businessId: string; customerId: string; permissions: string[]; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [note, setNote] = useState("");
  const key = ["business", businessId, "customer", customerId];
  const detail = useQuery({ queryKey: key, queryFn: () => api.get<CustomerDetail>(`/api/businesses/${businessId}/customers/${customerId}`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: key }); qc.invalidateQueries({ queryKey: ["business", businessId, "customers"] }); };
  const vip = useMutation({ mutationFn: (isVip: boolean) => api.patch(`/api/businesses/${businessId}/customers/${customerId}`, { isVip }), onSuccess: refresh });
  const addNote = useMutation({
    mutationFn: () => api.post(`/api/businesses/${businessId}/customers/${customerId}/notes`, { body: note }),
    onSuccess: () => { setNote(""); refresh(); toast("নোট যোগ হয়েছে।"); },
  });
  const d = detail.data;
  const notes = useClientPage(d?.notes, customerId, 5);
  const history = useClientPage(d?.bookings, customerId, 5);

  return (
    <Modal open onClose={onClose} title={d?.customer.displayName ?? "গ্রাহক"} wide>
      {!d ? <LoadingList rows={2} /> : (
        <div className="flex flex-col gap-5">
          <ErrorBox error={vip.error ?? addNote.error} />
          <div className="flex flex-wrap items-center gap-2">
            {d.customer.badges.map((b) => <Badge key={b} tone={BADGE[b].tone}>{BADGE[b].label}</Badge>)}
            <a href={`tel:${d.customer.phone}`} className="ml-auto inline-flex min-h-11 items-center gap-1.5 font-num font-semibold text-primary hover:text-primary-hover"><Phone className="size-4" aria-hidden />{d.customer.phone}</a>
          </div>
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {[["মোট ভিজিট", bn(d.customer.completedBookings)], ["মোট খরচ", taka(d.customer.lifetimeValue)],
              ["গ্রাহক যেদিন থেকে", d.customer.firstBookingAt ? dateShort(d.customer.firstBookingAt) : "—"], ["পরবর্তী বুকিং", d.customer.nextBookingAt ? dateShort(d.customer.nextBookingAt) : "—"]]
              .map(([k, v]) => <div key={k} className="rounded-md bg-bg p-3"><dt className="text-sm text-subtle">{k}</dt><dd className="font-semibold">{v}</dd></div>)}
          </dl>
          {permissions.includes("customers.manage") && (
            <label className="flex min-h-11 items-center gap-2.5"><input type="checkbox" className="size-5 accent-primary" checked={d.customer.isVip} onChange={(e) => vip.mutate(e.target.checked)} />VIP গ্রাহক হিসেবে চিহ্নিত করুন (তালিকায় উপরে থাকবে)</label>
          )}
          <section>
            <h3 className="mb-2 font-semibold">গোপন নোট <span className="text-sm font-normal text-subtle">(গ্রাহক কখনো দেখবেন না)</span></h3>
            {permissions.includes("customers.notes") && (
              <div className="mb-3 flex gap-2">
                <input className="input flex-1" value={note} onChange={(e) => setNote(e.target.value)} placeholder="যেমন: সকালে আসতে পছন্দ করেন" aria-label="নতুন নোট" />
                <Button disabled={!note.trim()} loading={addNote.isPending} onClick={() => addNote.mutate()}>যোগ করুন</Button>
              </div>
            )}
            <ul className="flex flex-col gap-2">
              {notes.items.map((n) => <li key={n.id} className="rounded-md bg-amber-50 px-4 py-3"><p>{n.body}</p><p className="text-sm text-subtle">{n.authorName} · {relative(n.createdAt)}</p></li>)}
              {d.notes.length === 0 && <li className="text-muted">এখনও কোনো নোট নেই</li>}
            </ul>
            <Pagination page={notes.page} total={notes.total} pageSize={notes.pageSize} onChange={notes.setPage} scrollTop={false} label="নোটের পাতা" />
          </section>
          <section>
            <h3 className="mb-2 font-semibold">বুকিংয়ের ইতিহাস</h3>
            <ul className="divide-y divide-zinc-100 rounded-md border border-line">
              {history.items.map((b) => (
                <li key={b.bookingId} className="flex flex-wrap items-center gap-3 px-4 py-2.5 text-[15px]">
                  <span className="w-28 text-subtle">{dateShort(b.startAt)}</span><span className="flex-1 font-medium">{b.serviceName}</span>
                  {b.finalPrice != null && <span className="font-num">{taka(b.finalPrice)}</span>}
                  <Badge tone={BOOKING_STATUS[b.status].tone} className="text-xs">{BOOKING_STATUS[b.status].label}</Badge>
                </li>
              ))}
            </ul>
            <Pagination page={history.page} total={history.total} pageSize={history.pageSize} onChange={history.setPage} scrollTop={false} label="বুকিংয়ের ইতিহাসের পাতা" />
          </section>
        </div>
      )}
    </Modal>
  );
}
