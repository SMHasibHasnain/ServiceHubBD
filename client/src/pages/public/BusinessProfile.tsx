import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BadgeCheck, Clock, Flag, Heart, Home, MapPin, Share2, Siren } from "lucide-react";
import { api } from "../../api/client";
import type { PublicBusiness } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Avatar, Badge, Button, EmptyState, ErrorBox, LinkButton, Modal, Skeleton, Stars, useToast } from "../../components/ui";
import { CoverArt } from "../../components/brand";
import { DAY_NAMES, REPORT_REASONS, bdDayOfWeek, bn, clock, dateShort, duration, priceLabel, todayBd } from "../../lib/format";

export default function BusinessProfile() {
  const { slug } = useParams();
  const { status } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const qc = useQueryClient();
  const [reportOpen, setReportOpen] = useState(false);

  const { data: b, isLoading, error } = useQuery({
    queryKey: ["public", "business", slug],
    queryFn: () => api.get<PublicBusiness>(`/api/public/businesses/${slug}`),
  });
  const favourites = useQuery({
    queryKey: ["favourites"],
    queryFn: () => api.get<string[]>("/api/me/favourites"),
    enabled: status === "authenticated",
  });
  const isFav = !!b && !!favourites.data?.includes(b.id);
  const fav = useMutation({
    mutationFn: () => (isFav ? api.del(`/api/me/favourites/${b!.id}`) : api.put(`/api/me/favourites/${b!.id}`, {})),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["favourites"] }); toast(isFav ? "সংরক্ষিত তালিকা থেকে সরানো হয়েছে।" : "সংরক্ষণ করা হয়েছে।"); },
    onError: (e) => toast((e as Error).message, "error"),
  });

  if (isLoading) return <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6"><Skeleton className="h-64 w-full" /><Skeleton className="mt-6 h-96 w-full" /></div>;
  if (error || !b) return <div className="mx-auto max-w-3xl px-4 py-16"><ErrorBox error={error ?? new Error("ব্যবসাটি পাওয়া যায়নি।")} /></div>;

  const today = bdDayOfWeek(todayBd());
  const todayHours = b.hours.filter((h) => h.dayOfWeek === today);
  const totalReviews = Object.values(b.ratingDistribution).reduce((s, n) => s + n, 0);
  const firstService = b.services.find((s) => s.availability === "available");
  const bookUrl = (serviceId?: string) => `/b/${b.slug}/book${serviceId ? `?serviceId=${serviceId}` : ""}`;
  const requireLogin = (fn: () => void) => (status === "authenticated" ? fn() : navigate(`/login?next=${encodeURIComponent(`/b/${b.slug}`)}`));

  const share = async () => {
    const url = window.location.href;
    try {
      if (navigator.share) await navigator.share({ title: b.name, url });
      else { await navigator.clipboard.writeText(url); toast("লিংক কপি করা হয়েছে।"); }
    } catch { /* cancelled */ }
  };

  return (
    <div className="mx-auto max-w-7xl px-4 pt-6 pb-28 sm:px-6 lg:pb-6">
      <section>
        <div className="h-40 overflow-hidden rounded-xl border border-line sm:h-64">
          {b.coverUrl ? <img src={b.coverUrl} alt={`${b.name} কভার ছবি`} className="size-full object-cover" /> : <CoverArt categoryId={b.categoryId} className="size-full" />}
        </div>
        <div className="relative z-10 -mt-10 flex flex-wrap items-end gap-4 px-2 sm:-mt-12 sm:gap-5 sm:px-6">
          <span className="rounded-xl border-4 border-white bg-white shadow-lift">
            {b.logoUrl ? <img src={b.logoUrl} alt="" className="size-24 rounded-lg object-cover sm:size-28" /> : <Avatar name={b.name} className="size-20 text-2xl sm:size-28 sm:text-3xl" />}
          </span>
          <div className="min-w-0 flex-[1_1_320px] pb-1">
            <div className="flex flex-wrap items-center gap-2.5">
              <h1 className="text-2xl font-bold text-brand-navy sm:text-3xl">{b.name}</h1>
              <Badge tone="teal"><BadgeCheck className="size-4" aria-hidden />যাচাইকৃত ব্যবসা</Badge>
            </div>
            <div className="mt-1 flex flex-wrap gap-x-5 gap-y-1 text-muted">
              <span>{b.categoryName}</span>
              {b.reviewCount > 0 && <span><Stars value={b.avgRating} /> ({bn(b.reviewCount)} রিভিউ)</span>}
              {b.zoneName && <span className="flex items-center gap-1"><MapPin className="size-4" aria-hidden />{b.zoneName}</span>}
            </div>
          </div>
          <div className="flex gap-2 pb-1">
            <Button variant="outline" size="sm" onClick={() => requireLogin(() => fav.mutate())} loading={fav.isPending} aria-pressed={isFav}
              className={isFav ? "border-rose-300 bg-rose-50 text-rose-700" : ""}>
              <Heart className="size-4" fill={isFav ? "currentColor" : "none"} aria-hidden />{isFav ? "সংরক্ষিত" : "সংরক্ষণ করুন"}
            </Button>
            <Button variant="outline" size="sm" onClick={share}><Share2 className="size-4" aria-hidden />শেয়ার করুন</Button>
          </div>
        </div>
      </section>

      <div className="mt-8 flex flex-col gap-6 lg:flex-row lg:items-start">
        <div className="flex min-w-0 flex-1 flex-col gap-6">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {[
              ["সম্পন্ন বুকিং", b.completedBookingCount > 0 ? `${bn(b.completedBookingCount)}+` : "নতুন"],
              ["সাড়া দেওয়ার সময়", b.avgResponseMinutes ? `~${bn(b.avgResponseMinutes)} মিনিট` : "—"],
              ["প্রতিষ্ঠার বছর", b.establishedYear ? bn(b.establishedYear) : "—"],
              ["আজ", todayHours.length ? todayHours.map((h) => `${clock(h.opensAt)}–${clock(h.closesAt)}`).join(", ") : "বন্ধ"],
            ].map(([k, v]) => <div key={k} className="card p-4"><div className="text-sm text-subtle">{k}</div><div className="font-semibold">{v}</div></div>)}
          </div>

          {(b.offersHomeService || b.offersEmergencyService) && (
            <div className="flex flex-wrap gap-2">
              {b.offersHomeService && <Badge tone="blue"><Home className="size-4" aria-hidden />হোম সার্ভিস</Badge>}
              {b.offersEmergencyService && <Badge tone="amber"><Siren className="size-4" aria-hidden />জরুরি সেবা</Badge>}
            </div>
          )}

          <section className="card p-6" aria-labelledby="services">
            <h2 id="services" className="mb-4 text-base font-semibold">সেবাসমূহ</h2>
            {b.services.length === 0 ? <p className="text-muted">এখনও কোনো সেবা যোগ করা হয়নি।</p> : (
              <ul className="flex flex-col gap-3">
                {b.services.map((s) => (
                  <li key={s.id} className="flex flex-wrap items-center gap-4 rounded-md border border-line p-4">
                    <div className="min-w-0 flex-[1_1_240px]">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-semibold">{s.name}</span>
                        {s.isFeatured && <Badge tone="amber" className="text-xs">ফিচার্ড</Badge>}
                        {s.availability !== "available" && <Badge className="text-xs">এখন পাওয়া যাচ্ছে না</Badge>}
                      </div>
                      {s.shortDescription && <p className="text-[15px] text-muted">{s.shortDescription}</p>}
                      <p className="text-sm text-subtle"><Clock className="mr-1 inline size-4" aria-hidden />{duration(s.durationMinutes)}{s.avgRating > 0 && <> · ★ {bn(s.avgRating)}</>}</p>
                    </div>
                    <div className="min-w-24 text-lg font-semibold">{priceLabel(s.priceType, s.price, s.priceMax)}</div>
                    <LinkButton to={bookUrl(s.id)} size="sm" className={s.availability !== "available" ? "pointer-events-none opacity-50" : ""} aria-disabled={s.availability !== "available"}>বুক করুন</LinkButton>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="card p-6" aria-labelledby="reviews">
            <h2 id="reviews" className="mb-5 text-base font-semibold">গ্রাহক রিভিউ</h2>
            {totalReviews === 0 ? <p className="text-muted">এখনও কোনো রিভিউ নেই। শুধুমাত্র সম্পন্ন বুকিংয়ের গ্রাহকরাই রিভিউ দিতে পারেন।</p> : (
              <>
                <div className="mb-6 flex flex-wrap items-center gap-8">
                  <div className="text-center">
                    <div className="font-num text-4xl font-semibold leading-none">{bn(b.avgRating)}</div>
                    <div className="text-lg tracking-widest text-amber-600" aria-hidden>{"★".repeat(Math.round(b.avgRating))}</div>
                    <div className="text-sm text-subtle">{bn(b.reviewCount)} রিভিউ</div>
                  </div>
                  <div className="flex min-w-60 flex-1 flex-col gap-1.5">
                    {["5", "4", "3", "2", "1"].map((star) => {
                      const n = b.ratingDistribution[star] ?? 0;
                      return (
                        <div key={star} className="flex items-center gap-2.5 text-sm">
                          <span className="w-7">{bn(star)}★</span>
                          <span className="h-2 flex-1 overflow-hidden rounded-full bg-zinc-100"><span className="block h-full rounded-full bg-warning" style={{ width: `${(n / totalReviews) * 100}%` }} /></span>
                          <span className="w-9 text-right text-subtle">{bn(n)}</span>
                        </div>
                      );
                    })}
                  </div>
                </div>
                <ul className="flex flex-col gap-4">
                  {b.latestReviews.map((r) => (
                    <li key={r.id} className="flex flex-col gap-2 border-t border-zinc-100 pt-4">
                      <div className="flex items-center gap-3">
                        <Avatar name={r.customerName} tone="violet" className="size-10 rounded-full" />
                        <div className="flex-1"><div className="font-semibold">{r.customerName}</div><div className="text-sm text-subtle">{r.serviceName} · {dateShort(r.createdAt)}</div></div>
                        <Stars value={r.rating} />
                      </div>
                      {r.comment && <p className="text-zinc-700">{r.comment}</p>}
                      <span className="text-[13px] font-medium text-teal-700">✓ সম্পন্ন বুকিংয়ের রিভিউ</span>
                      {r.replyBody && <div className="rounded-md bg-bg px-4 py-3 text-[15px]"><span className="font-semibold">ব্যবসার উত্তর: </span>{r.replyBody}</div>}
                    </li>
                  ))}
                </ul>
              </>
            )}
          </section>

          <section className="card p-6" aria-labelledby="about">
            <h2 id="about" className="mb-3 text-base font-semibold">পরিচিতি</h2>
            {(b.description || b.shortDescription) && <p className="mb-5 whitespace-pre-line text-zinc-700">{b.description || b.shortDescription}</p>}
            <dl className="grid gap-4 text-[15px] sm:grid-cols-2">
              {b.addressLine && <div><dt className="text-subtle">ঠিকানা</dt><dd className="font-semibold">{b.addressLine}{b.zoneName && `, ${b.zoneName}`}</dd></div>}
              {b.coverageNote && <div><dt className="text-subtle">সেবা এলাকা</dt><dd className="font-semibold">{b.coverageNote}</dd></div>}
              {b.cancellationPolicy && <div><dt className="text-subtle">বাতিলের নীতি</dt><dd className="font-semibold">{b.cancellationPolicy}</dd></div>}
            </dl>
            <h3 className="mt-6 mb-2 font-semibold">কাজের সময়</h3>
            <ul className="grid gap-1 text-[15px] sm:grid-cols-2">
              {DAY_NAMES.map((name, d) => {
                const hrs = b.hours.filter((h) => h.dayOfWeek === d);
                return (
                  <li key={name} className={`flex justify-between gap-4 rounded px-2 py-1 ${d === today ? "bg-zinc-100 font-semibold" : ""}`}>
                    <span>{name}</span><span>{hrs.length ? hrs.map((h) => `${clock(h.opensAt)}–${clock(h.closesAt)}`).join(", ") : "বন্ধ"}</span>
                  </li>
                );
              })}
            </ul>
            <button onClick={() => requireLogin(() => setReportOpen(true))} className="mt-6 flex min-h-11 items-center gap-2 font-medium text-red-700 hover:underline">
              <Flag className="size-4" aria-hidden />ব্যবসাটি রিপোর্ট করুন
            </button>
          </section>
        </div>

        <aside className="hidden w-full max-w-sm shrink-0 lg:sticky lg:top-24 lg:block">
          <div className="card flex flex-col gap-4 p-6 shadow-card">
            <div className="flex items-center gap-2">
              <span className={`size-2.5 rounded-full ${todayHours.length ? "bg-success" : "bg-zinc-400"}`} />
              <span className="font-semibold">{todayHours.length ? "আজ খোলা" : "আজ বন্ধ"}</span>
            </div>
            {firstService && <div className="text-muted">সেবা শুরু <strong className="text-lg text-ink">{priceLabel(firstService.priceType, firstService.price)}</strong></div>}
            <LinkButton to={bookUrl(firstService?.id)} size="lg" className="w-full">অ্যাপয়েন্টমেন্ট বুক করুন</LinkButton>
            <p className="text-sm text-subtle">বুকিং বিনামূল্যে। ব্যবসা নিশ্চিত করলে আপনি নোটিফিকেশন পাবেন।</p>
          </div>
        </aside>
      </div>

      <div className="fixed inset-x-0 bottom-0 z-20 border-t border-line bg-surface p-3 lg:hidden">
        <LinkButton to={bookUrl(firstService?.id)} size="lg" className="w-full">অ্যাপয়েন্টমেন্ট বুক করুন</LinkButton>
      </div>

      <ReportModal open={reportOpen} onClose={() => setReportOpen(false)} businessId={b.id} />
      {b.services.length === 0 && <div className="mt-6"><EmptyState title="এখনও কোনো সেবা যোগ করা হয়নি।" /></div>}
      <p className="mt-8 text-center text-sm text-subtle"><Link to="/search" className="hover:text-primary">← আরও ব্যবসা খুঁজুন</Link></p>
    </div>
  );
}

function ReportModal({ open, onClose, businessId }: { open: boolean; onClose: () => void; businessId: string }) {
  const [reason, setReason] = useState("misleading_information");
  const [description, setDescription] = useState("");
  const toast = useToast();
  const m = useMutation({
    mutationFn: () => api.post("/api/reports", { targetType: "business", targetBusinessId: businessId, reason, description }),
    onSuccess: () => { toast("রিপোর্ট জমা হয়েছে। মডারেটর পর্যালোচনা করবেন।"); onClose(); setDescription(""); },
  });
  return (
    <Modal open={open} onClose={onClose} title="ব্যবসাটি রিপোর্ট করুন"
      footer={<><Button variant="ghost" onClick={onClose}>বাতিল</Button><Button variant="danger" loading={m.isPending} onClick={() => m.mutate()}>রিপোর্ট জমা দিন</Button></>}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={m.error} />
        <label className="label">কারণ
          <select className="input" value={reason} onChange={(e) => setReason(e.target.value)}>
            {Object.entries(REPORT_REASONS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </label>
        <label className="label">বিস্তারিত <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>
          <textarea className="input" rows={4} value={description} onChange={(e) => setDescription(e.target.value)} />
        </label>
      </div>
    </Modal>
  );
}
