import { useState } from "react";
import { Link } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, CalendarDays, Car, Clock, Heart, ImagePlus, Star, Wrench, X } from "lucide-react";
import { api } from "../../api/client";
import type { Booking, CustomerStats, DayAvailability, Paged } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Avatar, Badge, Button, EmptyState, ErrorBox, LinkButton, LoadingList, Modal, PAGE_SIZE, PageHeader, Pagination, Segmented, usePage, useToast } from "../../components/ui";
import { BOOKING_STATUS, bn, clock, dateLong, priceLabel, relative, taka, timeOnly, todayBd } from "../../lib/format";
import { FilePicker, useUpload } from "../../components/ImageUpload";

type Scope = "upcoming" | "past" | "cancelled";
const TIMELINE = ["বুক করা হয়েছে", "নিশ্চিত", "সেবা", "রিভিউ"];

function progress(b: Booking) {
  if (b.hasReview) return 4;
  if (b.status === "completed") return 3;
  if (b.status === "confirmed") return 2;
  return 1;
}

export default function MyBookings() {
  const { user } = useAuth();
  const [scope, setScope] = useState<Scope>("upcoming");
  const [cancelling, setCancelling] = useState<Booking | null>(null);
  const [reviewing, setReviewing] = useState<Booking | null>(null);
  const [moving, setMoving] = useState<Booking | null>(null);
  const qc = useQueryClient();
  const toast = useToast();
  const [page, setPage] = usePage(scope);
  const list = useQuery({
    queryKey: ["me", "bookings", scope, page],
    queryFn: () => api.get<Paged<Booking>>("/api/me/bookings", { scope, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
    // Upcoming bookings refresh every 30 s so "on the way" / "started" shows up without reloading.
    refetchInterval: scope === "upcoming" ? 30_000 : false,
  });
  const withdraw = useMutation({
    mutationFn: (id: string) => api.del(`/api/me/bookings/${id}/reschedule`),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["me", "bookings"] }); toast("সময় পরিবর্তনের অনুরোধ তুলে নেওয়া হয়েছে।"); },
  });

  const stats = useQuery({ queryKey: ["me", "stats"], queryFn: () => api.get<CustomerStats>("/api/me/stats") });
  const st = stats.data;

  return (
    <>
      <PageHeader title="আমার বুকিং" subtitle={`স্বাগতম, ${user?.fullName.split(" ")[0]}! আপনার সব বুকিং এক জায়গায়।`}
        actions={<><LinkButton to="/me/saved" variant="outline" className="md:hidden"><Heart className="size-4" aria-hidden />সংরক্ষিত</LinkButton><LinkButton to="/search">সেবা খুঁজুন</LinkButton></>} />
      {st && st.totalBookings > 0 && (
        <section aria-label="আমার পরিসংখ্যান" className="mb-6 grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-line bg-line sm:grid-cols-4">
          {[
            ["মোট বুকিং", bn(st.totalBookings), `${bn(st.upcoming)}টি আসন্ন`],
            ["সম্পন্ন সেবা", bn(st.completed), st.cancelled ? `${bn(st.cancelled)}টি বাতিল` : "কোনো বাতিল নেই"],
            ["মোট খরচ", taka(st.totalSpent), "সম্পন্ন সেবায়"],
            ["ব্যবহৃত ব্যবসা", bn(st.businessesUsed), st.topCategory ? `প্রিয়: ${st.topCategory}` : `${bn(st.reviewsGiven)}টি রিভিউ দিয়েছেন`],
          ].map(([label, value, note]) => (
            <div key={label} className="bg-surface p-4">
              <div className="text-sm text-muted">{label}</div>
              <div className="mt-1 text-2xl font-semibold">{value}</div>
              <div className="mt-0.5 truncate text-xs text-subtle">{note}</div>
            </div>
          ))}
        </section>
      )}
      <div className="mb-5">
        <Segmented label="বুকিংয়ের ধরন" value={scope} onChange={setScope}
          options={[{ value: "upcoming", label: "আসন্ন" }, { value: "past", label: "সম্পন্ন" }, { value: "cancelled", label: "বাতিল" }]} />
      </div>
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <ul className="flex flex-col gap-4">
          {list.data.items.map((b) => (
            <li key={b.id} className="card flex flex-col gap-4 p-5">
              <div className="flex flex-wrap items-center gap-4">
                <Avatar name={b.businessName} className="size-13 text-lg" />
                <div className="min-w-0 flex-[1_1_220px]">
                  <div className="text-base font-semibold">{b.serviceName}</div>
                  <Link to={`/b/${b.businessSlug}`} className="text-[15px] text-muted hover:text-primary">{b.businessName}</Link>
                </div>
                <div className="flex flex-col items-end gap-1">
                  <Badge tone={BOOKING_STATUS[b.status].tone}>{b.status === "pending" ? "নিশ্চিতকরণের অপেক্ষায়" : BOOKING_STATUS[b.status].label}</Badge>
                  <span className="font-num text-[13px] text-subtle">{b.bookingNumber}</span>
                </div>
              </div>
              <div className="flex flex-wrap gap-x-6 gap-y-2 text-[15px] text-zinc-700">
                <span className="flex items-center gap-1.5"><CalendarDays className="size-4 text-subtle" aria-hidden />{dateLong(b.startAt)}</span>
                <span className="flex items-center gap-1.5"><Clock className="size-4 text-subtle" aria-hidden />{timeOnly(b.startAt)}</span>
                <span>{priceLabel(b.priceType, b.finalPrice ?? b.quotedPrice)}</span>
              </div>
              {(b.status === "pending" || b.status === "confirmed" || b.status === "completed") && (
                <ol aria-label="বুকিংয়ের অগ্রগতি" className="flex gap-2 rounded-md bg-bg p-4">
                  {TIMELINE.map((label, i) => (
                    <li key={label} className="flex flex-1 flex-col gap-1.5">
                      <span className={`h-1.5 rounded-full ${i < progress(b) ? "bg-primary" : "bg-line"}`} />
                      <span className={`text-[13px] ${i < progress(b) ? "font-semibold text-primary-hover" : "text-subtle"}`}>{label}</span>
                    </li>
                  ))}
                </ol>
              )}
              {b.status === "confirmed" && b.progress && (
                <div className="flex items-center gap-3 rounded-md border border-primary/30 bg-primary-light px-4 py-3 text-[15px]" role="status">
                  {b.progress === "on_the_way" ? <Car className="size-5 shrink-0 text-primary" aria-hidden /> : <Wrench className="size-5 shrink-0 text-primary" aria-hidden />}
                  <span className="font-semibold">{b.progress === "on_the_way" ? `${b.assignedTo?.name ?? b.businessName} রওনা দিয়েছেন` : `${b.assignedTo?.name ?? b.businessName} কাজ শুরু করেছেন`}</span>
                  {b.progressAt && <span className="ml-auto text-sm text-subtle">{relative(b.progressAt)}</span>}
                </div>
              )}
              {b.reschedule?.status === "pending" && (
                <div className="flex flex-wrap items-center gap-3 rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-[15px]">
                  <CalendarClock className="size-5 shrink-0 text-amber-700" aria-hidden />
                  <span className="min-w-0 flex-1">নতুন সময়ের অনুরোধ: <strong>{dateLong(b.reschedule.proposedStartAt)}, {timeOnly(b.reschedule.proposedStartAt)}</strong> — ব্যবসার উত্তরের অপেক্ষায়</span>
                  <Button size="sm" variant="ghost" loading={withdraw.isPending} onClick={() => withdraw.mutate(b.id)}><X className="size-4" aria-hidden />তুলে নিন</Button>
                </div>
              )}
              {b.reschedule?.status === "rejected" && scope === "upcoming" && (
                <p className="rounded-md bg-bg px-4 py-3 text-[15px] text-muted">সময় পরিবর্তনের অনুরোধ গ্রহণ হয়নি{b.reschedule.responseNote && `: ${b.reschedule.responseNote}`}। বুকিং আগের সময়েই আছে।</p>
              )}
              {b.cancellationReason && <p className="text-[15px] text-muted">কারণ: {b.cancellationReason}</p>}
              <div className="flex flex-wrap gap-2">
                {b.status === "completed" && !b.hasReview && <Button size="sm" onClick={() => setReviewing(b)}><Star className="size-4" aria-hidden />রিভিউ দিন</Button>}
                {(b.status === "pending" || b.status === "confirmed") && new Date(b.startAt) > new Date() && (
                  <>
                    {b.reschedule?.status !== "pending" && <Button size="sm" variant="outline" onClick={() => setMoving(b)}><CalendarClock className="size-4" aria-hidden />সময় বদলান</Button>}
                    <Button size="sm" variant="danger" onClick={() => setCancelling(b)}>বাতিল করুন</Button>
                  </>
                )}
                {(b.status === "completed" || b.status === "cancelled") && <LinkButton size="sm" variant="outline" to={`/b/${b.businessSlug}/book?serviceId=${b.serviceId}`}>আবার বুক করুন</LinkButton>}
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState title={scope === "upcoming" ? "এখনও কোনো আসন্ন বুকিং নেই।" : "এখনও কোনো বুকিং নেই।"} description="বিশ্বস্ত স্থানীয় সেবা খুঁজে প্রথম বুকিং করুন।"
          action={<LinkButton to="/search">সেবা খুঁজুন</LinkButton>} />
      )}
      {list.data && <Pagination page={page} total={list.data.total} onChange={setPage} label="বুকিংয়ের পাতা" />}
      {cancelling && <CancelModal booking={cancelling} onClose={() => setCancelling(null)} />}
      {reviewing && <ReviewModal booking={reviewing} onClose={() => setReviewing(null)} />}
      {moving && <RescheduleModal booking={moving} onClose={() => setMoving(null)} />}
    </>
  );
}

function CancelModal({ booking, onClose }: { booking: Booking; onClose: () => void }) {
  const [reason, setReason] = useState("");
  const qc = useQueryClient();
  const toast = useToast();
  const m = useMutation({
    mutationFn: () => api.post(`/api/me/bookings/${booking.id}/cancel`, { reason }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["me", "bookings"] }); toast("আপনার বুকিং বাতিল করা হয়েছে।"); onClose(); },
  });
  return (
    <Modal open onClose={onClose} title="বুকিং বাতিল করবেন?"
      footer={<><Button variant="ghost" onClick={onClose}>ফিরে যান</Button><Button variant="danger" disabled={reason.trim().length < 3} loading={m.isPending} onClick={() => m.mutate()}>বাতিল নিশ্চিত করুন</Button></>}>
      <div className="flex flex-col gap-4">
        <p className="text-muted">{booking.serviceName} — {dateLong(booking.startAt)}, {timeOnly(booking.startAt)}</p>
        <ErrorBox error={m.error} />
        <label className="label">বাতিলের কারণ<textarea className="input" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="যেমন: পরিকল্পনা পরিবর্তন হয়েছে" /></label>
      </div>
    </Modal>
  );
}

function ReviewModal({ booking, onClose }: { booking: Booking; onClose: () => void }) {
  const [rating, setRating] = useState(5);
  const [comment, setComment] = useState("");
  const [photos, setPhotos] = useState<string[]>([]);
  const { upload, busy } = useUpload("/api/me/uploads");
  const addPhotos = async (files: File[]) => {
    const added: string[] = [];
    for (const f of files.slice(0, 4 - photos.length)) { const url = await upload(f); if (url) added.push(url); }
    setPhotos((p) => [...p, ...added]);
  };
  const qc = useQueryClient();
  const toast = useToast();
  const m = useMutation({
    mutationFn: () => api.post(`/api/me/bookings/${booking.id}/review`, { rating, comment: comment || null, photoUrls: photos }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["me", "bookings"] }); toast("ধন্যবাদ! আপনার রিভিউ জমা হয়েছে।"); onClose(); },
  });
  return (
    <Modal open onClose={onClose} title={`${booking.businessName} কেমন ছিল?`}
      footer={<><Button variant="ghost" onClick={onClose}>পরে দেব</Button><Button loading={m.isPending} onClick={() => m.mutate()}>রিভিউ জমা দিন</Button></>}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={m.error} />
        <div role="radiogroup" aria-label="রেটিং" className="flex gap-1">
          {[1, 2, 3, 4, 5].map((n) => (
            <button key={n} role="radio" aria-checked={rating === n} aria-label={`${bn(n)} তারকা`} onClick={() => setRating(n)}
              className={`flex size-12 items-center justify-center rounded-md text-3xl ${n <= rating ? "text-amber-500" : "text-zinc-300"} hover:bg-amber-50`}>★</button>
          ))}
        </div>
        <label className="label">আপনার অভিজ্ঞতা <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>
          <textarea className="input" rows={4} value={comment} onChange={(e) => setComment(e.target.value)} placeholder="সেবা কেমন লেগেছে লিখুন" />
        </label>
        <div className="flex flex-col gap-2">
          <span className="text-sm font-medium">ছবি <span className="font-normal text-subtle">(ঐচ্ছিক, সর্বোচ্চ ৪টি)</span></span>
          <div className="flex flex-wrap gap-2">
            {photos.map((url) => (
              <div key={url} className="relative">
                <img src={url} alt="" className="size-20 rounded-md object-cover" />
                <button onClick={() => setPhotos(photos.filter((p) => p !== url))} aria-label="ছবি সরান"
                  className="absolute -top-2 -right-2 flex size-7 items-center justify-center rounded-full bg-zinc-900 text-white"><X className="size-4" aria-hidden /></button>
              </div>
            ))}
            {photos.length < 4 && (
              <FilePicker multiple disabled={busy} onFiles={addPhotos} className="size-20 flex-col !gap-1 !px-2 text-xs">
                <ImagePlus className="size-5" aria-hidden />{busy ? "আপলোড..." : "ছবি দিন"}
              </FilePicker>
            )}
          </div>
        </div>
      </div>
    </Modal>
  );
}

/** Pick a new date and a free slot; the business accepts or rejects. My own booking doesn't block its own slots. */
function RescheduleModal({ booking, onClose }: { booking: Booking; onClose: () => void }) {
  const [date, setDate] = useState(() => new Date(booking.startAt).toLocaleDateString("en-CA", { timeZone: "Asia/Dhaka" }));
  const [time, setTime] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const qc = useQueryClient();
  const toast = useToast();
  const slots = useQuery({
    queryKey: ["me", "bookings", booking.id, "reschedule-slots", date],
    queryFn: () => api.get<DayAvailability>(`/api/me/bookings/${booking.id}/reschedule-slots`, { date }),
    enabled: !!date,
  });
  const current = new Date(booking.startAt).getTime();
  const m = useMutation({
    mutationFn: () => api.post(`/api/me/bookings/${booking.id}/reschedule`, { date, time, reason: reason || null }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["me", "bookings"] }); toast("অনুরোধ পাঠানো হয়েছে। ব্যবসা নিশ্চিত করলে নোটিফিকেশন পাবেন।"); onClose(); },
  });
  return (
    <Modal open onClose={onClose} title="সময় বদলানোর অনুরোধ" wide
      footer={<><Button variant="ghost" onClick={onClose}>ফিরে যান</Button><Button disabled={!time} loading={m.isPending} onClick={() => m.mutate()}>অনুরোধ পাঠান</Button></>}>
      <div className="flex flex-col gap-4">
        <p className="text-muted">{booking.serviceName} — এখন: {dateLong(booking.startAt)}, {timeOnly(booking.startAt)}। ব্যবসা রাজি হলে নতুন সময় নিশ্চিত হবে; ততক্ষণ আগের সময়ই থাকবে।</p>
        <ErrorBox error={m.error ?? slots.error} />
        <label className="label">নতুন তারিখ<input className="input w-auto" type="date" min={todayBd()} value={date} onChange={(e) => { setDate(e.target.value); setTime(null); }} /></label>
        <div>
          <div className="mb-2 text-sm font-medium">সময় বেছে নিন</div>
          {slots.isLoading ? <p className="text-subtle">ফাঁকা সময় খোঁজা হচ্ছে...</p> : !slots.data?.isOpen ? (
            <p className="text-muted">{slots.data?.closedReason ?? "এই দিনে বুকিং নেওয়া হয় না।"}</p>
          ) : (
            <div role="radiogroup" aria-label="সময়" className="grid grid-cols-3 gap-2 sm:grid-cols-5">
              {slots.data.slots.map((s) => {
                const isCurrent = new Date(s.startAt).getTime() === current;
                return (
                  <button key={s.time} role="radio" aria-checked={time === s.time} disabled={!s.available || isCurrent} onClick={() => setTime(s.time)}
                    className={`min-h-11 rounded-md border text-sm font-medium disabled:cursor-not-allowed disabled:border-line disabled:bg-zinc-50 disabled:text-zinc-400 ${time === s.time ? "border-primary bg-primary text-white" : "border-line hover:border-primary"}`}>
                    {clock(s.time)}{isCurrent && <span className="block text-[11px] font-normal">বর্তমান</span>}
                  </button>
                );
              })}
            </div>
          )}
        </div>
        <label className="label">কারণ <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>
          <textarea className="input" rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="যেমন: অফিসে জরুরি মিটিং পড়েছে" />
        </label>
      </div>
    </Modal>
  );
}
