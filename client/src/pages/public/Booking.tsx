import { useMemo, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Check } from "lucide-react";
import { api } from "../../api/client";
import type { Booking as BookingDto, BookingField, DayAvailability, PublicBusiness } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Avatar, Badge, Button, ErrorBox, LinkButton, Skeleton } from "../../components/ui";
import { SuccessArt } from "../../components/brand";
import { DAY_NAMES, bdDayOfWeek, bn, clock, dateLong, duration, priceLabel, todayBd } from "../../lib/format";

const STEPS = ["সেবা", "তারিখ ও সময়", "তথ্য", "পর্যালোচনা"];

export default function Booking() {
  const { slug } = useParams();
  const [params] = useSearchParams();
  const { user } = useAuth();
  const [step, setStep] = useState(0);
  const [serviceId, setServiceId] = useState<string | null>(params.get("serviceId"));
  const [date, setDate] = useState(todayBd(1));
  const [time, setTime] = useState<string | null>(null);
  const [info, setInfo] = useState({ customerName: user?.fullName ?? "", customerPhone: user?.phone ?? "", serviceAddress: "", note: "" });
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [clientRequestId] = useState(() => crypto.randomUUID());

  const business = useQuery({
    queryKey: ["public", "business", slug],
    queryFn: () => api.get<PublicBusiness>(`/api/public/businesses/${slug}`),
  });
  const services = business.data?.services.filter((s) => s.availability === "available") ?? [];
  const service = services.find((s) => s.id === serviceId) ?? null;

  const availability = useQuery({
    queryKey: ["availability", business.data?.id, serviceId, date],
    queryFn: () => api.get<DayAvailability>(`/api/public/businesses/${business.data!.id}/availability`, { serviceId: serviceId!, date }),
    enabled: !!business.data && !!serviceId && step >= 1,
  });

  const days = useMemo(() => Array.from({ length: 14 }, (_, i) => todayBd(i)), []);

  const book = useMutation({
    mutationFn: () => api.post<BookingDto>("/api/bookings", {
      serviceId, date, time, ...info, clientRequestId,
      answers: Object.entries(answers).filter(([, v]) => v.trim()).map(([fieldId, value]) => ({ fieldId, value })),
    }),
  });

  if (business.isLoading) return <div className="mx-auto max-w-5xl px-4 py-10"><Skeleton className="h-96" /></div>;
  if (!business.data) return <div className="mx-auto max-w-3xl px-4 py-16"><ErrorBox error={business.error ?? new Error("ব্যবসাটি পাওয়া যায়নি।")} /></div>;
  const b = business.data;

  if (book.data) return <Success booking={book.data} />;

  const missingRequired = (service?.bookingFields ?? []).filter((f) => f.isRequired && !answers[f.id!]?.trim());
  const canNext = [!!service, !!time, missingRequired.length === 0 && info.customerName.trim().length > 1, true][step];

  return (
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6">
      <div className="mb-6 flex items-center gap-3">
        <Avatar name={b.name} className="size-11" />
        <div><div className="text-sm text-subtle">বুকিং করছেন</div><Link to={`/b/${b.slug}`} className="font-semibold hover:text-primary">{b.name}</Link></div>
      </div>

      <ol aria-label="বুকিংয়ের ধাপ" className="mb-6 grid grid-cols-2 gap-2 sm:grid-cols-4">
        {STEPS.map((label, i) => (
          <li key={label} aria-current={i === step ? "step" : undefined}
            className={`flex items-center gap-2.5 rounded-md border px-3 py-2.5 ${i === step ? "border-zinc-900 bg-zinc-50" : "border-line bg-surface"}`}>
            <span className={`flex size-7 items-center justify-center rounded-full text-sm font-semibold ${i < step ? "bg-green-600 text-white" : i === step ? "bg-primary text-white" : "bg-zinc-100 text-muted"}`}>
              {i < step ? <Check className="size-4" aria-hidden /> : bn(i + 1)}
            </span>
            <span className={`font-semibold ${i === step ? "text-primary-hover" : "text-zinc-700"}`}>{label}</span>
          </li>
        ))}
      </ol>

      <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
        <section className="card flex min-w-0 flex-1 flex-col gap-6 p-6 sm:p-8">
          {step === 0 && (
            <div className="flex flex-col gap-3">
              <h1 className="mb-1 text-lg font-semibold tracking-tight">কোন সেবাটি নিতে চান?</h1>
              {services.length === 0 && <p className="text-muted">এই মুহূর্তে কোনো সেবা বুকিংয়ের জন্য পাওয়া যাচ্ছে না।</p>}
              {services.map((s) => (
                <button key={s.id} onClick={() => { setServiceId(s.id); setTime(null); }} aria-pressed={s.id === serviceId}
                  className={`flex w-full items-center gap-4 rounded-lg border-[1.5px] p-4 text-left transition-colors ${s.id === serviceId ? "border-zinc-900 bg-zinc-50" : "border-line hover:border-zinc-400"}`}>
                  <span className="flex-1">
                    <span className="block font-semibold">{s.name}</span>
                    {s.shortDescription && <span className="block text-[15px] text-muted">{s.shortDescription}</span>}
                    <span className="block text-sm text-subtle">{duration(s.durationMinutes)}{s.avgRating > 0 && ` · ★ ${bn(s.avgRating)}`}</span>
                  </span>
                  <span className="text-lg font-semibold">{priceLabel(s.priceType, s.price, s.priceMax)}</span>
                </button>
              ))}
            </div>
          )}

          {step === 1 && (
            <div className="flex flex-col gap-6">
              <h1 className="text-lg font-semibold tracking-tight">তারিখ ও সময় বেছে নিন</h1>
              <div className="flex gap-2 overflow-x-auto pb-1" role="group" aria-label="তারিখ">
                {days.map((d) => {
                  const dow = bdDayOfWeek(d);
                  const open = b.hours.some((h) => h.dayOfWeek === dow);
                  return (
                    <button key={d} onClick={() => { setDate(d); setTime(null); }} disabled={!open} aria-pressed={d === date}
                      aria-label={`${DAY_NAMES[dow]}, ${dateLong(`${d}T06:00:00Z`)}${open ? "" : ", বন্ধ"}`}
                      className={`flex min-h-18 min-w-18 shrink-0 flex-col items-center justify-center rounded-lg border-[1.5px] ${
                        d === date ? "border-primary bg-primary text-white" : open ? "border-line bg-surface hover:border-zinc-400" : "cursor-not-allowed border-dashed border-line bg-zinc-100 text-subtle"}`}>
                      <span className="text-xs">{DAY_NAMES[dow].replace("বার", "")}</span>
                      <span className="font-num text-xl font-semibold">{bn(Number(d.slice(8)))}</span>
                      <span className="text-[11px]">{d === todayBd() ? "আজ" : open ? "" : "বন্ধ"}</span>
                    </button>
                  );
                })}
              </div>
              <div>
                <div className="mb-2.5 flex items-baseline justify-between"><span className="font-semibold">ফাঁকা সময়</span><span className="text-sm text-subtle">কাটা দাগ = বুকড</span></div>
                {availability.isLoading ? <Skeleton className="h-32" /> : availability.data && !availability.data.isOpen ? (
                  <p className="rounded-md bg-zinc-100 p-4 text-muted">এই দিন বুকিং নেওয়া হচ্ছে না: {availability.data.closedReason}</p>
                ) : (
                  <div className="grid grid-cols-[repeat(auto-fill,minmax(96px,1fr))] gap-2" role="group" aria-label="সময়">
                    {availability.data?.slots.map((s) => (
                      <button key={s.time} disabled={!s.available} onClick={() => setTime(s.time)} aria-pressed={s.time === time}
                        className={`min-h-11 rounded-md border-[1.5px] font-num text-[15px] font-semibold ${
                          s.time === time ? "border-primary bg-primary text-white" : s.available ? "border-line bg-surface hover:border-zinc-400" : "cursor-not-allowed border-line bg-zinc-100 text-subtle line-through"}`}>
                        {clock(s.time)}
                      </button>
                    ))}
                    {availability.data?.slots.every((s) => !s.available) && <p className="col-span-full text-muted">এই দিনে আর কোনো সময় ফাঁকা নেই। অন্য দিন বেছে নিন।</p>}
                  </div>
                )}
                <ErrorBox error={availability.error} />
              </div>
            </div>
          )}

          {step === 2 && service && (
            <div className="flex flex-col gap-4">
              <div><h1 className="text-lg font-semibold tracking-tight">কিছু তথ্য দিন</h1><p className="text-muted">এই প্রশ্নগুলো ব্যবসাটি এই সেবার জন্য ঠিক করেছে</p></div>
              <div className="grid gap-4 sm:grid-cols-2">
                <label className="label">নাম<input className="input" value={info.customerName} onChange={(e) => setInfo({ ...info, customerName: e.target.value })} autoComplete="name" /></label>
                <label className="label">মোবাইল নম্বর<input className="input" type="tel" value={info.customerPhone} onChange={(e) => setInfo({ ...info, customerPhone: e.target.value })} autoComplete="tel" /></label>
              </div>
              <label className="label">সেবার ঠিকানা {!b.offersHomeService && <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>}
                <input className="input" value={info.serviceAddress} onChange={(e) => setInfo({ ...info, serviceAddress: e.target.value })} placeholder="যেমন: বাসা ১২, রোড ৫, সেক্টর ৭, উত্তরা" autoComplete="street-address" />
              </label>
              {service.bookingFields.map((f) => <DynamicField key={f.id} field={f} value={answers[f.id!] ?? ""} onChange={(v) => setAnswers({ ...answers, [f.id!]: v })} />)}
              <label className="label">অতিরিক্ত নোট <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>
                <textarea className="input" rows={3} value={info.note} onChange={(e) => setInfo({ ...info, note: e.target.value })} />
              </label>
            </div>
          )}

          {step === 3 && service && time && (
            <div className="flex flex-col gap-4">
              <h1 className="text-lg font-semibold tracking-tight">বুকিং পর্যালোচনা করুন</h1>
              <dl className="grid gap-3 sm:grid-cols-2">
                {[["ব্যবসা", b.name], ["সেবা", service.name], ["তারিখ ও সময়", `${dateLong(`${date}T06:00:00Z`)}, ${clock(time)}`],
                  ["সময়কাল · মূল্য", `${duration(service.durationMinutes)} · ${priceLabel(service.priceType, service.price, service.priceMax)}`],
                  ["নাম ও মোবাইল", `${info.customerName} · ${info.customerPhone}`], ...(info.serviceAddress ? [["ঠিকানা", info.serviceAddress]] : [])]
                  .map(([k, v]) => <div key={k} className="rounded-md bg-bg p-4"><dt className="text-sm text-subtle">{k}</dt><dd className="font-semibold">{v}</dd></div>)}
              </dl>
              {b.cancellationPolicy && <div className="rounded-md border border-amber-200 bg-amber-50 p-4 text-[15px] text-amber-900"><div className="mb-1 font-semibold">ব্যবসার নীতিমালা</div>{b.cancellationPolicy}</div>}
              <ErrorBox error={book.error} />
            </div>
          )}

          <div className="flex justify-between gap-3 border-t border-zinc-100 pt-5">
            {step > 0 ? <Button variant="outline" onClick={() => { setStep(step - 1); book.reset(); }}>← আগের ধাপ</Button> : <span />}
            {step < 3
              ? <Button disabled={!canNext} onClick={() => setStep(step + 1)}>{step === 0 ? "এই সেবাটি নির্বাচন করুন" : "পরবর্তী ধাপ →"}</Button>
              : <Button loading={book.isPending} onClick={() => book.mutate()}>{book.isPending ? "নিশ্চিত করা হচ্ছে..." : "বুকিং নিশ্চিত করুন"}</Button>}
          </div>
          {step === 2 && missingRequired.length > 0 && <p className="text-sm text-subtle">আবশ্যক: {missingRequired.map((f) => f.label).join(", ")}</p>}
        </section>

        <aside className="card flex w-full flex-col gap-3.5 p-6 lg:w-80 lg:shrink-0">
          <span className="text-lg font-semibold">সারসংক্ষেপ</span>
          <div className="flex justify-between gap-3"><span className="text-subtle">সেবা</span><span className="text-right font-semibold">{service?.name ?? "—"}</span></div>
          <div className="flex justify-between gap-3"><span className="text-subtle">তারিখ</span><span className="font-semibold">{step >= 1 ? dateLong(`${date}T06:00:00Z`) : "—"}</span></div>
          <div className="flex justify-between gap-3"><span className="text-subtle">সময়</span><span className="font-semibold">{time ? clock(time) : "—"}</span></div>
          <div className="flex justify-between gap-3 border-t border-zinc-100 pt-3.5"><span className="font-semibold">মোট</span><span className="text-xl font-semibold">{service ? priceLabel(service.priceType, service.price, service.priceMax) : "—"}</span></div>
          <p className="text-sm text-subtle">বুকিং বিনামূল্যে। ব্যবসা নিশ্চিত করলে আপনি নোটিফিকেশন পাবেন।</p>
        </aside>
      </div>
    </div>
  );
}

function DynamicField({ field, value, onChange }: { field: BookingField; value: string; onChange: (v: string) => void }) {
  const label = <>{field.label} {field.isRequired ? <span className="text-sm font-normal text-red-700">আবশ্যক</span> : <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>}</>;
  switch (field.fieldType) {
    case "select":
      return (
        <label className="label">{label}
          <select className="input" value={value} onChange={(e) => onChange(e.target.value)} required={field.isRequired}>
            <option value="">নির্বাচন করুন</option>
            {field.options?.map((o) => <option key={o} value={o}>{o}</option>)}
          </select>
        </label>
      );
    case "long_text":
      return <label className="label">{label}<textarea className="input" rows={3} value={value} placeholder={field.placeholder ?? ""} onChange={(e) => onChange(e.target.value)} /></label>;
    default:
      return (
        <label className="label">{label}
          <input className="input" value={value} placeholder={field.placeholder ?? ""} onChange={(e) => onChange(e.target.value)}
            type={field.fieldType === "number" ? "number" : field.fieldType === "phone" ? "tel" : field.fieldType === "date" ? "date" : "text"} />
        </label>
      );
  }
}

function Success({ booking }: { booking: BookingDto }) {
  return (
    <div className="mx-auto flex max-w-xl flex-col items-center gap-4 px-4 py-16 text-center">
      <SuccessArt className="size-36" />
      <h1 className="text-2xl font-semibold text-primary sm:text-[28px]">{booking.status === "confirmed" ? "বুকিং নিশ্চিত হয়েছে" : "বুকিংয়ের অনুরোধ পাঠানো হয়েছে"}</h1>
      <p className="text-muted">{booking.status === "confirmed"
        ? `${booking.businessName} আপনার সময়টি নিশ্চিত করেছে।`
        : `${booking.businessName} আপনার অনুরোধ পেয়েছে। নিশ্চিত হলে আপনাকে জানানো হবে।`}</p>
      <div className="card grid w-full grid-cols-2 gap-3 p-5 text-left">
        <div><div className="text-sm text-subtle">বুকিং নম্বর</div><div className="font-num font-semibold tabular-nums">{booking.bookingNumber}</div></div>
        <div><div className="text-sm text-subtle">অবস্থা</div><Badge tone={booking.status === "confirmed" ? "blue" : "amber"}>{booking.status === "confirmed" ? "নিশ্চিত" : "নিশ্চিতকরণের অপেক্ষায়"}</Badge></div>
        <div><div className="text-sm text-subtle">সেবা</div><div className="font-semibold">{booking.serviceName}</div></div>
        <div><div className="text-sm text-subtle">সময়</div><div className="font-semibold">{dateLong(booking.startAt)}</div></div>
      </div>
      <div className="mt-2 flex flex-wrap justify-center gap-3">
        <LinkButton to="/me/bookings">আমার বুকিং দেখুন</LinkButton>
        <LinkButton to="/" variant="outline">হোমে ফিরে যান</LinkButton>
      </div>
    </div>
  );
}
