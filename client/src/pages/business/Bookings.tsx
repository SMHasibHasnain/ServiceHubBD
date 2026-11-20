import { useState } from "react";
import { useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, CalendarDays, Phone } from "lucide-react";
import { api } from "../../api/client";
import type { Booking, BookingStatus, Member, Paged } from "../../api/types";
import { Avatar, Badge, Button, EmptyState, ErrorBox, LoadingList, Modal, PageHeader, useToast } from "../../components/ui";
import { BOOKING_STATUS, bn, dateLong, dateShort, dateTime, priceLabel, taka, timeOnly, todayBd } from "../../lib/format";
import { useBiz } from "./Dashboard";

const STATUS_FILTERS: { value: BookingStatus | ""; label: string }[] = [
  { value: "", label: "সব" }, { value: "pending", label: "অপেক্ষমাণ" }, { value: "confirmed", label: "নিশ্চিত" },
  { value: "completed", label: "সম্পন্ন" }, { value: "cancelled", label: "বাতিল" },
];

export default function Bookings() {
  const biz = useBiz();
  const [params, setParams] = useSearchParams();
  const [q, setQ] = useState("");
  const status = (params.get("status") ?? "") as BookingStatus | "";
  const range = params.get("range") ?? "upcoming";
  const openId = params.get("open");

  const dates = range === "today" ? { from: todayBd(), to: todayBd() } : range === "upcoming" ? { from: todayBd() } : {};
  const list = useQuery({
    queryKey: ["business", biz?.id, "bookings", { status, range, q }],
    queryFn: () => api.get<Paged<Booking>>(`/api/businesses/${biz!.id}/bookings`, { status, q, ...dates, pageSize: 100 }),
    enabled: !!biz,
    placeholderData: keepPreviousData,
  });
  const set = (k: string, v: string | null) => { const n = new URLSearchParams(params); v ? n.set(k, v) : n.delete(k); setParams(n); };
  const staffOnly = biz && !biz.myPermissions.includes("bookings.view_all");

  return (
    <>
      <PageHeader title="অ্যাপয়েন্টমেন্ট" subtitle={staffOnly ? "শুধু আপনাকে বরাদ্দ করা অ্যাপয়েন্টমেন্ট" : "কে আসছেন, কখন, কোন সেবার জন্য — এক নজরে"} />
      <div className="mb-5 flex flex-col gap-3">
        <div className="flex flex-wrap gap-2" role="group" aria-label="অবস্থা">
          {STATUS_FILTERS.map((f) => (
            <button key={f.value} onClick={() => set("status", f.value || null)} aria-pressed={status === f.value}
              className={`min-h-10 rounded-full border px-4 text-[15px] font-medium ${status === f.value ? "border-zinc-900 bg-zinc-900 text-white" : "border-zinc-300 bg-surface text-zinc-700"}`}>
              {f.label}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap gap-2">
          <select className="input w-auto" value={range} onChange={(e) => set("range", e.target.value)} aria-label="সময়সীমা">
            <option value="today">আজ</option><option value="upcoming">আজ ও আসন্ন</option><option value="all">সব সময়</option>
          </select>
          <form className="flex flex-1 gap-2" onSubmit={(e) => { e.preventDefault(); list.refetch(); }}>
            <input className="input min-w-48 flex-1" value={q} onChange={(e) => setQ(e.target.value)} placeholder="গ্রাহকের নাম, মোবাইল, সেবা বা বুকিং নম্বর" aria-label="অ্যাপয়েন্টমেন্ট খুঁজুন" />
          </form>
        </div>
      </div>

      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <ul className="flex flex-col gap-3">
          {list.data.items.map((b) => (
            <li key={b.id}>
              <button onClick={() => set("open", b.id)} className="card flex w-full flex-wrap items-center gap-4 p-4 text-left transition-colors hover:border-zinc-400">
                <div className="w-24 shrink-0"><div className="font-num font-semibold">{timeOnly(b.startAt)}</div><div className="text-sm text-subtle">{dateShort(b.startAt)}</div></div>
                <Avatar name={b.customerName} tone="violet" className="size-10 rounded-full" />
                <div className="min-w-0 flex-[1_1_200px]">
                  <div className="font-semibold">{b.customerName}</div>
                  <div className="text-[15px] text-muted">{b.serviceName}{b.assignedTo && ` · ${b.assignedTo.name}`}</div>
                </div>
                <span className="font-num text-sm text-subtle">{b.bookingNumber}</span>
                {b.reschedule?.status === "pending" && <Badge tone="amber"><CalendarClock className="size-3.5" aria-hidden />সময় বদলের অনুরোধ</Badge>}
                {b.status === "confirmed" && b.progress && <Badge tone="blue">{b.progress === "on_the_way" ? "রওনা দিয়েছেন" : "কাজ চলছে"}</Badge>}
                <Badge tone={BOOKING_STATUS[b.status].tone}>{BOOKING_STATUS[b.status].label}</Badge>
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState icon={<CalendarDays className="size-8" aria-hidden />} title="এখনও কোনো বুকিং নেই।"
          description="আপনার পাবলিক পেজটি গ্রাহকদের সাথে শেয়ার করুন, অথবা ফিল্টার পরিবর্তন করে দেখুন।" />
      )}
      {openId && biz && <BookingDetail businessId={biz.id} bookingId={openId} permissions={biz.myPermissions} onClose={() => set("open", null)} />}
    </>
  );
}

function BookingDetail({ businessId, bookingId, permissions, onClose }: { businessId: string; bookingId: string; permissions: string[]; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [reasonFor, setReasonFor] = useState<"reject" | "cancel" | "reschedule" | null>(null);
  const [reason, setReason] = useState("");
  const [finalPrice, setFinalPrice] = useState("");
  const [note, setNote] = useState<string | null>(null);
  const manage = permissions.includes("bookings.manage");
  const canComplete = permissions.includes("bookings.complete");

  const booking = useQuery({
    queryKey: ["business", businessId, "booking", bookingId],
    queryFn: () => api.get<Booking>(`/api/businesses/${businessId}/bookings/${bookingId}`),
  });
  const members = useQuery({
    queryKey: ["business", businessId, "members"],
    queryFn: () => api.get<Member[]>(`/api/businesses/${businessId}/members`),
    enabled: manage && permissions.includes("members.view"),
  });

  const act = useMutation({
    mutationFn: ({ action, body }: { action: string; body?: unknown }) =>
      action === "assignee" ? api.put<Booking>(`/api/businesses/${businessId}/bookings/${bookingId}/assignee`, body)
      : action === "note" ? api.put<Booking>(`/api/businesses/${businessId}/bookings/${bookingId}/note`, body)
      : api.post<Booking>(`/api/businesses/${businessId}/bookings/${bookingId}/${action}`, body ?? {}),
    onSuccess: (updated, { action }) => {
      qc.setQueryData(["business", businessId, "booking", bookingId], updated);
      qc.invalidateQueries({ queryKey: ["business", businessId, "bookings"] });
      qc.invalidateQueries({ queryKey: ["business", businessId, "summary"] });
      setReasonFor(null); setReason("");
      toast({ confirm: "বুকিং নিশ্চিত করা হয়েছে।", reject: "বুকিং প্রত্যাখ্যান করা হয়েছে।", cancel: "বুকিং বাতিল করা হয়েছে।", complete: "সেবা সম্পন্ন হিসেবে চিহ্নিত হয়েছে।",
        "no-show": "উপস্থিত হননি হিসেবে চিহ্নিত।", assignee: "টেকনিশিয়ান নির্ধারণ করা হয়েছে।", note: "নোট সংরক্ষিত হয়েছে।" }[action] ?? "সংরক্ষিত হয়েছে।");
    },
  });

  // Reschedule answers return no body, so refetch instead of replacing the cached booking.
  const reschedule = useMutation({
    mutationFn: (answer: { accept: true } | { accept: false; reason: string }) =>
      api.post(`/api/businesses/${businessId}/bookings/${bookingId}/reschedule/${answer.accept ? "accept" : "reject"}`, answer.accept ? {} : { reason: answer.reason }),
    onSuccess: (_, answer) => {
      qc.invalidateQueries({ queryKey: ["business", businessId] });
      setReasonFor(null); setReason("");
      toast(answer.accept ? "নতুন সময় নিশ্চিত হয়েছে। গ্রাহককে জানানো হয়েছে।" : "অনুরোধটি প্রত্যাখ্যান করা হয়েছে।");
    },
  });

  const b = booking.data;
  const started = b ? new Date(b.startAt) <= new Date() : false;

  return (
    <Modal open onClose={onClose} title={b ? `বুকিং ${b.bookingNumber}` : "বুকিং"} wide
      footer={b && (
        <>
          {manage && b.status === "pending" && <><Button variant="danger" onClick={() => setReasonFor("reject")}>প্রত্যাখ্যান করুন</Button><Button loading={act.isPending} onClick={() => act.mutate({ action: "confirm" })}>নিশ্চিত করুন</Button></>}
          {b.status === "confirmed" && (
            <>
              {manage && <Button variant="danger" onClick={() => setReasonFor("cancel")}>বাতিল</Button>}
              {manage && started && <Button variant="outline" loading={act.isPending} onClick={() => act.mutate({ action: "no-show" })}>উপস্থিত হননি</Button>}
              {canComplete && <Button disabled={!started} title={started ? undefined : "অ্যাপয়েন্টমেন্টের সময় শুরু হলে সম্পন্ন করা যাবে"} loading={act.isPending}
                onClick={() => act.mutate({ action: "complete", body: { finalPrice: finalPrice ? Number(finalPrice) : null } })}>সম্পন্ন করুন</Button>}
            </>
          )}
        </>
      )}>
      {!b ? <LoadingList rows={2} /> : (
        <div className="flex flex-col gap-5">
          <ErrorBox error={act.error ?? reschedule.error} />
          <div className="flex flex-wrap items-center gap-3">
            <Badge tone={BOOKING_STATUS[b.status].tone}>{BOOKING_STATUS[b.status].label}</Badge>
            <span className="text-muted">{dateLong(b.startAt)}, {timeOnly(b.startAt)}</span>
            {b.status === "confirmed" && b.progress && (
              <Badge tone="blue">{b.assignedTo?.name ?? "টিম"} {b.progress === "on_the_way" ? "রওনা দিয়েছেন" : "কাজ শুরু করেছেন"} · {timeOnly(b.progressAt!)}</Badge>
            )}
          </div>
          {b.reschedule?.status === "pending" && (
            <div className="flex flex-col gap-3 rounded-md border border-amber-200 bg-amber-50 p-4">
              <div className="flex items-start gap-2">
                <CalendarClock className="mt-0.5 size-5 shrink-0 text-amber-700" aria-hidden />
                <div>
                  <div className="font-semibold">গ্রাহক সময় বদলাতে চান</div>
                  <div className="text-[15px]">{dateLong(b.reschedule.previousStartAt)}, {timeOnly(b.reschedule.previousStartAt)} → <strong>{dateLong(b.reschedule.proposedStartAt)}, {timeOnly(b.reschedule.proposedStartAt)}</strong></div>
                  {b.reschedule.reason && <div className="text-[15px] text-muted">কারণ: {b.reschedule.reason}</div>}
                </div>
              </div>
              {manage && reasonFor !== "reschedule" && (
                <div className="flex flex-wrap gap-2">
                  <Button size="sm" loading={reschedule.isPending} onClick={() => reschedule.mutate({ accept: true })}>নতুন সময় মেনে নিন</Button>
                  <Button size="sm" variant="danger" onClick={() => setReasonFor("reschedule")}>প্রত্যাখ্যান</Button>
                </div>
              )}
            </div>
          )}
          <dl className="grid gap-3 sm:grid-cols-2">
            <div className="rounded-md bg-bg p-4"><dt className="text-sm text-subtle">গ্রাহক</dt><dd className="font-semibold">{b.customerName}</dd>
              <dd><a href={`tel:${b.customerPhone}`} className="inline-flex min-h-9 items-center gap-1.5 font-num text-primary"><Phone className="size-4" aria-hidden />{b.customerPhone}</a></dd></div>
            <div className="rounded-md bg-bg p-4"><dt className="text-sm text-subtle">সেবা</dt><dd className="font-semibold">{b.serviceName}</dd><dd className="text-[15px] text-muted">{priceLabel(b.priceType, b.quotedPrice)}{b.finalPrice != null && ` · চূড়ান্ত ${taka(b.finalPrice)}`}</dd></div>
            {b.serviceAddress && <div className="rounded-md bg-bg p-4 sm:col-span-2"><dt className="text-sm text-subtle">ঠিকানা</dt><dd className="font-semibold">{b.serviceAddress}</dd></div>}
            {b.answers.map((a) => <div key={a.label} className="rounded-md bg-bg p-4"><dt className="text-sm text-subtle">{a.label}</dt><dd className="font-semibold">{a.value}</dd></div>)}
            {b.customerNote && <div className="rounded-md bg-bg p-4 sm:col-span-2"><dt className="text-sm text-subtle">গ্রাহকের নোট</dt><dd>{b.customerNote}</dd></div>}
          </dl>

          {manage && members.data && (b.status === "pending" || b.status === "confirmed") && (
            <label className="label">টেকনিশিয়ান / স্টাফ
              <select className="input" value={b.assignedTo?.memberId ?? ""} onChange={(e) => act.mutate({ action: "assignee", body: { memberId: e.target.value || null } })}>
                <option value="">কাউকে নির্ধারণ করা হয়নি</option>
                {members.data.filter((m) => m.status === "active").map((m) => <option key={m.id} value={m.id}>{m.fullName} — {m.roleName}</option>)}
              </select>
            </label>
          )}
          {b.status === "confirmed" && canComplete && started && (
            <label className="label">চূড়ান্ত মূল্য <span className="text-sm font-normal text-subtle">(ঐচ্ছিক, না দিলে উদ্ধৃত মূল্য)</span>
              <input className="input max-w-48" type="number" min={0} value={finalPrice} onChange={(e) => setFinalPrice(e.target.value)} />
            </label>
          )}
          {manage && (
            <label className="label">ব্যবসার গোপন নোট
              <textarea className="input" rows={2} value={note ?? b.businessNote ?? ""} onChange={(e) => setNote(e.target.value)} onBlur={() => note !== null && note !== (b.businessNote ?? "") && act.mutate({ action: "note", body: { note } })} placeholder="শুধু আপনার টিম দেখবে" />
            </label>
          )}

          {reasonFor && (
            <div className="flex flex-col gap-3 rounded-md border border-red-200 bg-red-50 p-4">
              <label className="label">{reasonFor === "reject" ? "প্রত্যাখ্যানের কারণ" : reasonFor === "reschedule" ? "অনুরোধ প্রত্যাখ্যানের কারণ" : "বাতিলের কারণ"} <span className="text-sm font-normal">(গ্রাহক দেখবেন)</span>
                <textarea className="input" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />
              </label>
              <div className="flex gap-2">
                <Button variant="ghost" onClick={() => setReasonFor(null)}>ফিরে যান</Button>
                <Button variant="danger" disabled={reason.trim().length < 3} loading={act.isPending || reschedule.isPending}
                  onClick={() => reasonFor === "reschedule" ? reschedule.mutate({ accept: false, reason }) : act.mutate({ action: reasonFor, body: { reason } })}>নিশ্চিত করুন</Button>
              </div>
            </div>
          )}

          <div>
            <h3 className="mb-2 font-semibold">অগ্রগতি</h3>
            <ol className="flex flex-col gap-2 border-l-2 border-line pl-4">
              {b.history.map((h, i) => (
                <li key={i} className="text-[15px]">
                  <span className="font-semibold">{h.from === h.to ? "সময় পরিবর্তন" : BOOKING_STATUS[h.to].label}</span> <span className="text-subtle">· {dateTime(h.at)}</span>
                  {h.reason && <div className="text-muted">{h.from === h.to ? h.reason.replace(/^সময় পরিবর্তন:\s*/, "") : `কারণ: ${h.reason}`}</div>}
                </li>
              ))}
            </ol>
          </div>
          <p className="text-sm text-subtle">মোট {bn(b.history.length)}টি পরিবর্তন</p>
        </div>
      )}
    </Modal>
  );
}
