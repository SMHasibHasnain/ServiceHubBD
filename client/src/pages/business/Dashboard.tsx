import { Link, useOutletContext } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, CalendarDays, CheckCircle2, Circle, Clock, ImagePlus, Package, Settings, Star, Users, XCircle } from "lucide-react";
import { api } from "../../api/client";
import type { Booking, BusinessDetail, Paged, Service } from "../../api/types";
import { Badge, EmptyState, LinkButton, Skeleton, Stat } from "../../components/ui";
import { BOOKING_STATUS, VERIFICATION_STATUS, bn, dateLong, timeOnly, todayBd } from "../../lib/format";

export const useBiz = () => useOutletContext<BusinessDetail | undefined>();

export default function Dashboard() {
  const biz = useBiz();
  const id = biz?.id;
  const can = (p: string) => !!biz?.myPermissions.includes(p);

  const summary = useQuery({
    queryKey: ["business", id, "summary"],
    queryFn: () => api.get<Record<string, number>>(`/api/businesses/${id}/bookings/summary`),
    enabled: !!id,
  });
  const today = useQuery({
    queryKey: ["business", id, "bookings", "today"],
    queryFn: () => api.get<Paged<Booking>>(`/api/businesses/${id}/bookings`, { from: todayBd(), to: todayBd(), pageSize: 50 }),
    enabled: !!id,
  });
  const services = useQuery({
    queryKey: ["business", id, "services"],
    queryFn: () => api.get<Service[]>(`/api/businesses/${id}/services`),
    enabled: !!id,
  });

  if (!biz) return <Skeleton className="h-96" />;
  const s = summary.data ?? {};
  const checklist = [
    { done: !!biz.shortDescription || !!biz.description, label: "ব্যবসার বিবরণ", to: "settings" },
    { done: !!biz.zoneId && !!biz.addressLine, label: "ঠিকানা ও এলাকা", to: "settings" },
    { done: biz.hours.length > 0, label: "কাজের সময়", to: "settings" },
    { done: (services.data?.filter((x) => x.status === "active").length ?? 0) > 0, label: "অন্তত একটি সক্রিয় সেবা", to: "services" },
  ];
  const v = VERIFICATION_STATUS[biz.verificationStatus];

  return (
    <div className="flex flex-col gap-6">
      <div className="bg-brand relative flex flex-wrap items-center justify-between gap-4 overflow-hidden rounded-xl p-5 text-white shadow-lift sm:p-7">
        <div className="absolute inset-0 opacity-25 [background-image:radial-gradient(rgb(255_255_255/0.35)_1px,transparent_1px)] [background-size:20px_20px]" aria-hidden />
        <div className="absolute -right-10 -bottom-16 size-56 rounded-full bg-brand-lime/25 blur-xl" aria-hidden />
        <div className="relative min-w-0">
          <p className="text-sm text-blue-100">{dateLong(new Date().toISOString())}</p>
          <h1 className="mt-1 truncate text-2xl font-bold sm:text-3xl">স্বাগতম, {biz.name}</h1>
          <p className="mt-1 text-sm text-blue-100">আজকের ব্যবসার এক নজর</p>
        </div>
        <div className="relative flex flex-wrap gap-2">
          {(s.pending_all ?? 0) > 0 && (
            <Link to="bookings?status=pending" className="inline-flex min-h-11 items-center rounded-md bg-white px-4 text-sm font-semibold text-brand-navy shadow-lift hover:bg-blue-50">
              অপেক্ষমাণ বুকিং ({bn(s.pending_all)})
            </Link>
          )}
          {biz.status === "published" && (
            <Link to={`/b/${biz.slug}`} className="inline-flex min-h-11 items-center rounded-md border border-white/30 px-4 text-sm font-semibold text-white hover:bg-white/10">পাবলিক পেজ</Link>
          )}
        </div>
      </div>

      {biz.verificationStatus !== "approved" && (
        <section className="card flex flex-col gap-3 border-amber-200 bg-amber-50/40 p-5">
          <div className="flex flex-wrap items-center gap-3">
            <AlertTriangle className="size-4 text-amber-600" aria-hidden />
            <h2 className="font-semibold">আপনার পেজ এখনও প্রকাশিত হয়নি</h2>
            <Badge tone={v.tone}>{v.label}</Badge>
          </div>
          {biz.verificationStatus === "submitted" || biz.verificationStatus === "under_review" ? (
            <p className="text-sm text-muted">আপনার যাচাইকরণের আবেদন জমা হয়েছে। মডারেটর পর্যালোচনা করছেন — প্রয়োজনে এলাকার পরিদর্শক সরেজমিনে আসতে পারেন।</p>
          ) : (
            <>
              <ul className="grid gap-2 sm:grid-cols-2">
                {checklist.map((c) => (
                  <li key={c.label}>
                    <Link to={c.to} className="flex min-h-9 items-center gap-2.5 rounded-md px-2 text-sm hover:bg-white">
                      {c.done ? <CheckCircle2 className="size-4 text-emerald-600" aria-label="সম্পন্ন" /> : <Circle className="size-4 text-zinc-400" aria-label="বাকি" />}
                      <span className={c.done ? "text-muted line-through" : "font-medium"}>{c.label}</span>
                    </Link>
                  </li>
                ))}
              </ul>
              {can("business.verification.submit") && (
                <div><LinkButton to="settings#verification" variant={checklist.every((c) => c.done) ? "primary" : "outline"}>যাচাইকরণের জন্য জমা দিন</LinkButton></div>
              )}
            </>
          )}
        </section>
      )}

      <section aria-label="আজকের পরিসংখ্যান" className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-6">
        <Stat label="আজকের অ্যাপয়েন্টমেন্ট" value={bn(s.today ?? 0)} icon={<CalendarDays className="size-[18px]" />} />
        <Stat label="অপেক্ষমাণ" value={bn(s.pending_all ?? 0)} tone="amber" icon={<Clock className="size-[18px]" />} note="সব দিনের" />
        <Stat label="নিশ্চিত (আজ)" value={bn(s.confirmed ?? 0)} tone="blue" icon={<CheckCircle2 className="size-[18px]" />} />
        <Stat label="সম্পন্ন (আজ)" value={bn(s.completed ?? 0)} tone="green" icon={<CheckCircle2 className="size-[18px]" />} />
        <Stat label="বাতিল (আজ)" value={bn((s.cancelled ?? 0) + (s.rejected ?? 0))} tone="red" icon={<XCircle className="size-[18px]" />} />
        <Stat label="গড় রেটিং" value={biz.reviewCount ? bn(biz.avgRating) : "—"} tone="amber" icon={<Star className="size-[18px]" />} note={`${bn(biz.reviewCount)} রিভিউ`} />
      </section>

      <div className="grid gap-6 xl:grid-cols-[2fr_1fr]">
        <section className="card p-5">
          <div className="mb-4 flex items-center justify-between gap-3">
            <h2 className="font-semibold">আজকের সময়সূচি</h2>
            <Link to="bookings" className="text-sm font-medium text-muted hover:text-ink">সব অ্যাপয়েন্টমেন্ট →</Link>
          </div>
          {today.isLoading ? <Skeleton className="h-40" /> : today.data?.items.length ? (
            <ol className="flex flex-col gap-2">
              {today.data.items.map((b) => (
                <li key={b.id}>
                  <Link to={`bookings?open=${b.id}`} className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-md border border-line px-4 py-2.5 text-sm hover:bg-zinc-50">
                    <span className="w-16 font-num text-sm text-subtle">{timeOnly(b.startAt)}</span>
                    <span className="min-w-0 flex-1"><span className="font-medium">{b.customerName}</span> <span className="text-muted">· {b.serviceName}</span>
                      {b.assignedTo && <span className="block text-sm text-subtle">টেকনিশিয়ান: {b.assignedTo.name}</span>}</span>
                    <Badge tone={BOOKING_STATUS[b.status].tone}>{BOOKING_STATUS[b.status].label}</Badge>
                  </Link>
                </li>
              ))}
            </ol>
          ) : (
            <EmptyState title="আজ কোনো অ্যাপয়েন্টমেন্ট নেই।" description={biz.status === "published" ? "আপনার পাবলিক পেজটি শেয়ার করুন যাতে গ্রাহকরা বুক করতে পারেন।" : "পেজ প্রকাশিত হলে গ্রাহকরা বুক করতে পারবেন।"} />
          )}
        </section>

        <section className="card self-start p-2">
          <h2 className="px-3 pt-3 pb-2 text-sm font-medium text-subtle">দ্রুত কাজ</h2>
          <div className="flex flex-col">
            {[
              { to: "services", label: "নতুন সেবা যোগ করুন", icon: Package, show: can("services.manage") },
              { to: "bookings", label: "অ্যাপয়েন্টমেন্ট", icon: CalendarDays, show: true },
              { to: "customers", label: "গ্রাহক ব্যবস্থাপনা", icon: Users, show: can("customers.view") },
              { to: "reviews", label: "রিভিউর উত্তর দিন", icon: Star, show: true },
              { to: "settings", label: "ব্যবসার তথ্য", icon: Settings, show: can("business.update") },
              { to: "team", label: "টিম সদস্য যোগ করুন", icon: ImagePlus, show: can("members.manage") },
            ].filter((q) => q.show).map(({ to, label, icon: Icon }) => (
              <Link key={to} to={to} className="flex min-h-10 items-center gap-3 rounded-md px-3 text-sm hover:bg-zinc-100">
                <Icon className="size-4 text-zinc-400" aria-hidden />
                <span className="flex-1">{label}</span>
                <span className="text-zinc-300" aria-hidden>→</span>
              </Link>
            ))}
          </div>
        </section>
      </div>
    </div>
  );
}
