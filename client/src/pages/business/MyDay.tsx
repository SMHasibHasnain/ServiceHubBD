import { useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Car, CheckCircle2, Clock, MapPin, Phone, Plane, Plus, Wrench, X } from "lucide-react";
import { api } from "../../api/client";
import type { Booking, Leave, MyDay as MyDayData, Paged, WorkProgress } from "../../api/types";
import { Badge, Button, EmptyState, ErrorBox, LoadingList, Modal, PageHeader, Pagination, Segmented, Skeleton, usePage, useToast } from "../../components/ui";
import { BOOKING_STATUS, bn, timeOnly, todayBd } from "../../lib/format";
import { useBiz } from "./Dashboard";

export const LEAVE_STATUS: Record<Leave["status"], { label: string; tone: "amber" | "green" | "red" | "neutral" }> = {
  pending: { label: "অপেক্ষমাণ", tone: "amber" },
  approved: { label: "মঞ্জুর", tone: "green" },
  rejected: { label: "নামঞ্জুর", tone: "red" },
  cancelled: { label: "বাতিল", tone: "neutral" },
};

export const leaveRange = (l: Leave) => {
  const fmt = (d: string) => new Date(`${d}T00:00:00+06:00`).toLocaleDateString("bn-BD", { day: "numeric", month: "short", timeZone: "Asia/Dhaka" });
  return l.startDate === l.endDate ? fmt(l.startDate) : `${fmt(l.startDate)} – ${fmt(l.endDate)}`;
};

/** A team member's own day: their assigned jobs with everything needed in the field, plus their leave. Built for phones. */
export default function MyDay() {
  const biz = useBiz();
  const [day, setDay] = useState<"today" | "tomorrow">("today");
  const date = todayBd(day === "today" ? 0 : 1);
  const data = useQuery({
    queryKey: ["business", biz?.id, "my-day", date],
    queryFn: () => api.get<MyDayData>(`/api/businesses/${biz!.id}/my-day`, { date }),
    enabled: !!biz,
    refetchInterval: 60_000,
  });
  if (!biz) return <Skeleton className="h-96" />;
  const open = data.data?.bookings.filter((b) => b.status !== "completed").length ?? 0;

  return (
    <>
      <PageHeader title="আমার কাজ" subtitle={data.data ? (open ? `${bn(open)}টি কাজ বাকি` : "এই দিনের সব কাজ শেষ") : undefined} />
      <div className="mb-5">
        <Segmented label="দিন" value={day} onChange={setDay} options={[{ value: "today", label: "আজ" }, { value: "tomorrow", label: "আগামীকাল" }]} />
      </div>
      <ErrorBox error={data.error} />
      {data.data?.leave && (
        <div className="mb-4 flex items-center gap-3 rounded-md border border-emerald-200 bg-emerald-50 px-4 py-3 text-[15px]">
          <Plane className="size-5 text-emerald-700" aria-hidden />আপনি এই দিন ছুটিতে আছেন ({leaveRange(data.data.leave)})। নতুন কাজ বরাদ্দ হবে না।
        </div>
      )}
      {data.isLoading ? <LoadingList /> : data.data?.bookings.length ? (
        <ul className="flex flex-col gap-4">
          {data.data.bookings.map((b) => <JobCard key={b.id} booking={b} businessId={biz.id} />)}
        </ul>
      ) : (
        <EmptyState icon={<CheckCircle2 aria-hidden />} title={day === "today" ? "আজ আপনাকে কোনো কাজ দেওয়া হয়নি" : "আগামীকাল কোনো কাজ নেই"}
          description="ম্যানেজার আপনাকে অ্যাপয়েন্টমেন্ট বরাদ্দ করলে এখানে দেখাবে।" />
      )}
      <LeaveSection businessId={biz.id} />
    </>
  );
}

function JobCard({ booking: b, businessId }: { booking: Booking; businessId: string }) {
  const qc = useQueryClient();
  const toast = useToast();
  const refresh = () => qc.invalidateQueries({ queryKey: ["business", businessId] });
  const progress = useMutation({
    mutationFn: (p: WorkProgress) => api.post(`/api/businesses/${businessId}/bookings/${b.id}/progress`, { progress: p }),
    onSuccess: (_, p) => { refresh(); toast(p === "on_the_way" ? "গ্রাহককে জানানো হয়েছে: আপনি রওনা দিয়েছেন।" : "গ্রাহককে জানানো হয়েছে: কাজ শুরু হয়েছে।"); },
  });
  const complete = useMutation({
    mutationFn: () => api.post(`/api/businesses/${businessId}/bookings/${b.id}/complete`, {}),
    onSuccess: () => { refresh(); toast("কাজ সম্পন্ন হয়েছে। গ্রাহককে রিভিউ দিতে বলা হয়েছে।"); },
  });
  const started = new Date(b.startAt) <= new Date();
  const mapUrl = b.serviceAddress ? `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(b.serviceAddress)}` : null;

  return (
    <li className={`card flex flex-col gap-3 p-5 ${b.status === "completed" ? "opacity-70" : ""}`}>
      <div className="flex flex-wrap items-center gap-2">
        <span className="flex items-center gap-1.5 font-num text-lg font-semibold"><Clock className="size-4 text-subtle" aria-hidden />{timeOnly(b.startAt)} – {timeOnly(b.endAt)}</span>
        <Badge tone={BOOKING_STATUS[b.status].tone} className="ml-auto">{BOOKING_STATUS[b.status].label}</Badge>
        {b.status === "confirmed" && b.progress && <Badge tone="blue">{b.progress === "on_the_way" ? "রওনা দিয়েছেন" : "কাজ চলছে"}</Badge>}
      </div>
      <div>
        <div className="text-base font-semibold">{b.serviceName}</div>
        <div className="text-[15px] text-muted">{b.customerName} · {b.bookingNumber}</div>
      </div>
      <div className="flex flex-col gap-2 text-[15px]">
        <a href={`tel:${b.customerPhone}`} className="flex min-h-11 items-center gap-2 font-medium text-primary hover:underline"><Phone className="size-4" aria-hidden />{b.customerPhone}</a>
        {b.serviceAddress && (
          <a href={mapUrl!} target="_blank" rel="noopener" className="flex min-h-11 items-start gap-2 hover:text-primary">
            <MapPin className="mt-1 size-4 shrink-0" aria-hidden /><span>{b.serviceAddress} <span className="text-sm text-primary">(ম্যাপে দেখুন)</span></span>
          </a>
        )}
      </div>
      {(b.answers.length > 0 || b.customerNote || b.businessNote) && (
        <dl className="grid gap-2 rounded-md bg-bg p-3 text-[15px]">
          {b.answers.map((a) => <div key={a.label}><dt className="text-sm text-subtle">{a.label}</dt><dd className="font-medium">{a.value ?? "—"}</dd></div>)}
          {b.customerNote && <div><dt className="text-sm text-subtle">গ্রাহকের নোট</dt><dd>{b.customerNote}</dd></div>}
          {b.businessNote && <div><dt className="text-sm text-subtle">ভেতরের নোট</dt><dd>{b.businessNote}</dd></div>}
        </dl>
      )}
      <ErrorBox error={progress.error ?? complete.error} />
      {b.status === "confirmed" && (
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
          <Button variant={b.progress ? "outline" : "primary"} disabled={!!b.progress} loading={progress.isPending && progress.variables === "on_the_way"}
            onClick={() => progress.mutate("on_the_way")}>{b.progress ? <CheckCircle2 className="size-4 text-success" aria-hidden /> : <Car className="size-4" aria-hidden />}রওনা দিয়েছি</Button>
          <Button variant={b.progress === "on_the_way" ? "primary" : "outline"} disabled={b.progress === "started"} loading={progress.isPending && progress.variables === "started"}
            onClick={() => progress.mutate("started")}>{b.progress === "started" ? <CheckCircle2 className="size-4 text-success" aria-hidden /> : <Wrench className="size-4" aria-hidden />}কাজ শুরু</Button>
          <Button variant={b.progress === "started" ? "primary" : "outline"} disabled={!started} loading={complete.isPending} onClick={() => complete.mutate()}
            title={started ? undefined : "অ্যাপয়েন্টমেন্টের সময় শুরু হলে সম্পন্ন করা যাবে"}><CheckCircle2 className="size-4" aria-hidden />কাজ শেষ</Button>
        </div>
      )}
      {b.status === "confirmed" && !started && <p className="text-sm text-subtle">অ্যাপয়েন্টমেন্টের সময় শুরু হলে “কাজ শেষ” চাপা যাবে।</p>}
      {b.status === "pending" && <p className="text-sm text-subtle">ব্যবসা এখনও বুকিংটি নিশ্চিত করেনি।</p>}
    </li>
  );
}

function LeaveSection({ businessId }: { businessId: string }) {
  const [asking, setAsking] = useState(false);
  const qc = useQueryClient();
  const toast = useToast();
  const [page, setPage] = usePage();
  const leaves = useQuery({
    queryKey: ["business", businessId, "leaves", "mine", page],
    queryFn: () => api.get<Paged<Leave>>(`/api/businesses/${businessId}/leaves`, { mine: true, page, pageSize: 10 }),
    placeholderData: keepPreviousData,
  });
  const cancel = useMutation({
    mutationFn: (id: string) => api.del(`/api/businesses/${businessId}/leaves/${id}`),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["business", businessId] }); toast("ছুটির আবেদন বাতিল হয়েছে।"); },
  });
  const today = todayBd();
  return (
    <section className="card mt-8 flex flex-col gap-4 p-6" aria-labelledby="leave">
      <div className="flex flex-wrap items-center gap-3">
        <h2 id="leave" className="mr-auto text-base font-semibold">আমার ছুটি</h2>
        <Button size="sm" onClick={() => setAsking(true)}><Plus className="size-4" aria-hidden />ছুটির আবেদন</Button>
      </div>
      <ErrorBox error={leaves.error ?? cancel.error} />
      {leaves.data?.items.length ? (
        <>
        <ul className="flex flex-col divide-y divide-zinc-100 rounded-md border border-line">
          {leaves.data.items.map((l) => (
            <li key={l.id} className="flex flex-wrap items-center gap-3 px-4 py-3 text-[15px]">
              <span className="font-medium">{leaveRange(l)}</span>
              {l.reason && <span className="text-muted">{l.reason}</span>}
              <Badge tone={LEAVE_STATUS[l.status].tone} className="ml-auto">{LEAVE_STATUS[l.status].label}</Badge>
              {l.decisionNote && <span className="basis-full text-sm text-subtle">ম্যানেজারের মন্তব্য: {l.decisionNote}</span>}
              {(l.status === "pending" || l.status === "approved") && l.endDate >= today && (
                <Button size="sm" variant="ghost" onClick={() => cancel.mutate(l.id)} aria-label="আবেদন বাতিল করুন"><X className="size-4" aria-hidden /></Button>
              )}
            </li>
          ))}
        </ul>
        <Pagination page={page} total={leaves.data.total} pageSize={10} onChange={setPage} scrollTop={false} label="ছুটির পাতা" className="mt-0" />
        </>
      ) : !leaves.isLoading && <p className="text-muted">কোনো ছুটির আবেদন নেই। মঞ্জুর হলে ওই দিনগুলোতে আপনাকে কাজ দেওয়া হবে না।</p>}
      {asking && <LeaveModal businessId={businessId} onClose={() => setAsking(false)} />}
    </section>
  );
}

function LeaveModal({ businessId, onClose }: { businessId: string; onClose: () => void }) {
  const [start, setStart] = useState(todayBd(1));
  const [end, setEnd] = useState(todayBd(1));
  const [reason, setReason] = useState("");
  const qc = useQueryClient();
  const toast = useToast();
  const m = useMutation({
    mutationFn: () => api.post(`/api/businesses/${businessId}/leaves`, { startDate: start, endDate: end, reason: reason || null }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["business", businessId] }); toast("আবেদন পাঠানো হয়েছে। ম্যানেজার সিদ্ধান্ত নিলে জানানো হবে।"); onClose(); },
  });
  return (
    <Modal open onClose={onClose} title="ছুটির আবেদন"
      footer={<><Button variant="ghost" onClick={onClose}>বাতিল</Button><Button loading={m.isPending} disabled={!start || !end || end < start} onClick={() => m.mutate()}>আবেদন পাঠান</Button></>}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={m.error} />
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="label">শুরু<input className="input" type="date" min={todayBd()} value={start} onChange={(e) => { setStart(e.target.value); if (e.target.value > end) setEnd(e.target.value); }} /></label>
          <label className="label">শেষ<input className="input" type="date" min={start} value={end} onChange={(e) => setEnd(e.target.value)} /></label>
        </div>
        <label className="label">কারণ <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>
          <textarea className="input" rows={2} maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="যেমন: পারিবারিক অনুষ্ঠান" />
        </label>
        <p className="text-sm text-subtle">একবারে সর্বোচ্চ ৩০ দিন। মঞ্জুর হলে ওই দিনগুলোর আগে থেকে দেওয়া কাজ থেকে আপনাকে সরিয়ে অন্য কাউকে দেওয়া হবে।</p>
      </div>
    </Modal>
  );
}
