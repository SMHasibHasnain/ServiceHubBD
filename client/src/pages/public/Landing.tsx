import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import {
  ArrowRight, BadgeCheck, CalendarCheck, LineChart, MapPin, MessageSquareHeart, Search, ShieldCheck, Star, Users, UsersRound,
} from "lucide-react";
import { api } from "../../api/client";
import type { BusinessCard, Paged } from "../../api/types";
import { CategoryTile, HeroArt, Sticker } from "../../components/brand";
import { BusinessCardView, useCategories } from "../../components/shared";
import { LinkButton, Skeleton } from "../../components/ui";

const CUSTOMER_STEPS = [
  { icon: Search, t: "অনুসন্ধান করুন", d: "সেবা, ক্যাটাগরি বা এলাকা দিয়ে খুঁজুন" },
  { icon: BadgeCheck, t: "তুলনা করুন", d: "যাচাইকৃত ব্যাজ, রিভিউ ও মূল্য দেখে বেছে নিন" },
  { icon: CalendarCheck, t: "বুক করুন", d: "তারিখ ও সময় বেছে দুই মিনিটে বুকিং" },
  { icon: Star, t: "রিভিউ দিন", d: "সেবা শেষে অভিজ্ঞতা শেয়ার করুন" },
];
const BUSINESS_STEPS = [
  { icon: UsersRound, t: "পেজ তৈরি করুন", d: "একই অ্যাকাউন্ট থেকে এক বা একাধিক ব্যবসা পেজ" },
  { icon: ShieldCheck, t: "যাচাইকরণ", d: "ডকুমেন্ট জমা দিন — প্রয়োজনে মাঠ পরিদর্শন" },
  { icon: CalendarCheck, t: "পরিচালনা করুন", d: "অ্যাপয়েন্টমেন্ট, গ্রাহক, সেবা ও টিম" },
  { icon: LineChart, t: "প্রবৃদ্ধি", d: "রিভিউ ও বিশ্লেষণ দিয়ে ব্যবসা বাড়ান" },
];
const FEATURES: [typeof Star, string, string][] = [
  [CalendarCheck, "অ্যাপয়েন্টমেন্ট", "স্লট, নিশ্চিতকরণ ও টেকনিশিয়ান"],
  [Users, "গ্রাহক CRM", "ইতিহাস, নোট ও ট্যাগ"],
  [MessageSquareHeart, "রিভিউ ও উত্তর", "বিশ্বাস গড়ে তুলুন"],
  [BadgeCheck, "যাচাইকৃত ব্যাজ", "মডারেটর ও পরিদর্শক যাচাই"],
  [MapPin, "এলাকাভিত্তিক খোঁজ", "বিভাগ থেকে ইউনিয়ন পর্যন্ত"],
  [UsersRound, "টিম ও ভূমিকা", "ম্যানেজার, স্টাফ, মডারেটর"],
];

export default function Landing() {
  const navigate = useNavigate();
  const [q, setQ] = useState("");
  const categories = useCategories();
  const featured = useQuery({
    queryKey: ["public", "featured"],
    queryFn: () => api.get<Paged<BusinessCard>>("/api/public/businesses", { sort: "highest_rated", pageSize: 6 }),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    navigate(`/search${q.trim() ? `?q=${encodeURIComponent(q.trim())}` : ""}`);
  };

  const allCategories = categories.data?.flatMap((c) => [c, ...c.children]) ?? [];

  return (
    <>
      {/* ---------- Hero ---------- */}
      <section className="bg-hero relative overflow-hidden border-b border-line">
        <div className="bg-dots pointer-events-none absolute inset-0 [mask-image:linear-gradient(to_bottom,black,transparent_70%)]" aria-hidden />
        <div className="relative mx-auto grid max-w-6xl items-center gap-14 px-4 pt-12 pb-16 sm:px-6 md:pt-20 md:pb-24 lg:grid-cols-[1.1fr_1fr]">
          <div className="flex flex-col items-start gap-6">
            <span className="inline-flex items-center gap-2 rounded-full border border-blue-100 bg-white/80 py-1 pr-3.5 pl-1 text-sm font-medium text-brand-navy shadow-soft backdrop-blur">
              <span className="rounded-full bg-secondary-light px-2 py-0.5 text-xs font-semibold text-brand-green">নতুন</span>
              বাংলাদেশের স্থানীয় সেবার এক ঠিকানা
            </span>
            <h1 className="text-[34px] leading-[1.2] font-bold text-brand-navy sm:text-5xl sm:leading-[1.15]">
              আপনার এলাকার <span className="text-brand-gradient">বিশ্বস্ত সেবা</span>, এক ক্লিকে বুক করুন
            </h1>
            <p className="max-w-xl text-base text-muted sm:text-lg">
              যাচাইকৃত ইলেকট্রিশিয়ান, এসি মেরামত, ক্লিনিং, সেলুন থেকে শুরু করে আইটি সাপোর্ট — রিভিউ দেখে বেছে নিন, সময় ঠিক করুন।
            </p>

            <form role="search" onSubmit={submit} className="flex w-full max-w-xl flex-col gap-2 rounded-xl border border-line bg-white p-2 shadow-lift sm:flex-row">
              <label className="flex min-h-12 flex-1 items-center gap-2.5 px-3">
                <Search className="size-5 text-primary" aria-hidden />
                <span className="sr-only">কী সেবা খুঁজছেন</span>
                <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="যেমন: এসি মেরামত, ইলেকট্রিশিয়ান, গাজীপুর"
                  className="w-full bg-transparent text-base text-ink outline-none placeholder:text-zinc-400" />
              </label>
              <button className="min-h-12 rounded-md bg-primary px-6 text-base font-semibold text-white shadow-[inset_0_1px_0_rgb(255_255_255/0.18)] hover:bg-primary-hover">সেবা খুঁজুন</button>
            </form>

            {allCategories.length > 0 && (
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <span className="text-subtle">জনপ্রিয়:</span>
                {allCategories.slice(0, 4).map((c) => (
                  <Link key={c.id} to={`/search?categoryId=${c.id}`} className="rounded-full border border-line bg-white/80 px-3 py-1 text-muted hover:border-primary/40 hover:text-primary">{c.nameBn}</Link>
                ))}
              </div>
            )}

            <ul className="mt-2 flex flex-wrap gap-x-6 gap-y-2 text-sm text-muted">
              {[[ShieldCheck, "যাচাইকৃত ব্যবসা"], [CalendarCheck, "ঝামেলাহীন বুকিং"], [Star, "আসল গ্রাহকের রিভিউ"]].map(([I, t]) => {
                const Icon = I as typeof Star;
                return <li key={t as string} className="flex items-center gap-1.5"><Icon className="size-4 text-brand-green" aria-hidden />{t as string}</li>;
              })}
            </ul>
          </div>
          <div className="px-3 sm:px-6"><HeroArt /></div>
        </div>
      </section>

      {/* ---------- Categories ---------- */}
      <section className="mx-auto max-w-6xl px-4 pt-20 sm:px-6">
        <SectionHead eyebrow="ক্যাটাগরি" title="কোন সেবা খুঁজছেন?" subtitle="আপনার প্রয়োজনীয় সব সেবা এক জায়গায়" link={{ to: "/search", label: "সব সেবা দেখুন" }} />
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {categories.isLoading && Array.from({ length: 8 }, (_, i) => <Skeleton key={i} className="h-[76px]" />)}
          {allCategories.map((c) => (
            <Link key={c.id} to={`/search?categoryId=${c.id}`} className="card card-hover group flex items-center gap-3 p-3.5">
              <CategoryTile icon={c.icon} className="transition-transform group-hover:scale-105" />
              <span className="min-w-0">
                <span className="block truncate font-display text-[15px] font-semibold">{c.nameBn}</span>
                <span className="block truncate text-xs text-subtle">{c.nameEn}</span>
              </span>
            </Link>
          ))}
        </div>
      </section>

      {/* ---------- Featured ---------- */}
      <section className="mx-auto max-w-6xl px-4 pt-20 sm:px-6">
        <SectionHead eyebrow="শীর্ষ রেটিং" title="যাচাইকৃত শীর্ষ ব্যবসা" subtitle="মডারেটর ও মাঠ পরিদর্শকের মাধ্যমে যাচাই করা" link={{ to: "/search?sort=highest_rated", label: "আরও দেখুন" }} />
        {featured.isLoading ? (
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">{Array.from({ length: 3 }, (_, i) => <Skeleton key={i} className="h-80" />)}</div>
        ) : featured.data?.items.length ? (
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">{featured.data.items.map((b) => <BusinessCardView key={b.id} b={b} />)}</div>
        ) : (
          <div className="card flex flex-col items-center gap-3 p-10 text-center">
            <Sticker tilt={-4} className="text-brand-green"><BadgeCheck className="size-4" /> শীঘ্রই আসছে</Sticker>
            <p className="text-muted">এখনও কোনো যাচাইকৃত ব্যবসা প্রকাশিত হয়নি। প্রথম ব্যবসা পেজটি আপনিই তৈরি করুন!</p>
            <LinkButton to="/business/new" variant="outline">ব্যবসা পেজ তৈরি করুন</LinkButton>
          </div>
        )}
      </section>

      {/* ---------- How it works ---------- */}
      <section id="how" className="mx-auto max-w-6xl scroll-mt-24 px-4 pt-20 sm:px-6">
        <SectionHead eyebrow="সহজ ধাপ" title="কীভাবে কাজ করে" />
        <div className="grid gap-5 md:grid-cols-2">
          {[{ title: "গ্রাহকের জন্য", steps: CUSTOMER_STEPS, tint: "bg-primary-light text-primary", sticker: "সেবা নিন" },
            { title: "ব্যবসায়ীর জন্য", steps: BUSINESS_STEPS, tint: "bg-secondary-light text-brand-green", sticker: "ব্যবসা বাড়ান" }].map((col) => (
            <div key={col.title} className="card relative p-6 sm:p-8">
              <div className="mb-6 flex items-center justify-between gap-3">
                <h3 className="text-xl font-semibold">{col.title}</h3>
                <span className={`rounded-full px-3 py-1 text-xs font-semibold ${col.tint}`}>{col.sticker}</span>
              </div>
              <ol className="relative flex flex-col gap-5">
                <span className="absolute top-5 bottom-5 left-5 w-px bg-line" aria-hidden />
                {col.steps.map((s) => (
                  <li key={s.t} className="relative flex gap-4">
                    <span className={`flex size-10 shrink-0 items-center justify-center rounded-full ring-4 ring-white ${col.tint}`}><s.icon className="size-[18px]" aria-hidden /></span>
                    <div className="pt-0.5"><div className="font-display text-base font-semibold">{s.t}</div><div className="text-sm text-muted">{s.d}</div></div>
                  </li>
                ))}
              </ol>
            </div>
          ))}
        </div>
      </section>

      {/* ---------- For business ---------- */}
      <section className="mx-auto max-w-6xl px-4 pt-20 sm:px-6">
        <div className="bg-brand relative overflow-hidden rounded-xl p-6 text-white shadow-pop sm:p-10 lg:p-12">
          <div className="absolute inset-0 opacity-25 [background-image:radial-gradient(rgb(255_255_255/0.35)_1px,transparent_1px)] [background-size:22px_22px]" aria-hidden />
          <div className="absolute -right-20 -bottom-24 size-80 rounded-full bg-brand-lime/25 blur-2xl" aria-hidden />
          <div className="relative grid gap-10 lg:grid-cols-[1fr_1.15fr] lg:items-center">
            <div className="flex flex-col items-start gap-4">
              <Sticker tilt={-3} className="text-brand-navy"><img src="/brand/logo-mark.png" alt="" className="h-5 w-auto" /> ব্যবসা ওয়ার্কস্পেস</Sticker>
              <h2 className="text-[28px] font-bold leading-snug sm:text-4xl">শুধু তালিকাভুক্তি নয় — পুরো ব্যবসা এক জায়গা থেকে চালান</h2>
              <p className="max-w-md text-blue-100">একই অ্যাকাউন্ট থেকে এক বা একাধিক ব্যবসা পেজ, টিম সদস্য ও ভূমিকা — লগআউট ছাড়াই ওয়ার্কস্পেস বদলান।</p>
              <div className="mt-2 flex flex-wrap gap-3">
                <Link to="/business/new" className="inline-flex min-h-12 items-center gap-2 rounded-md bg-white px-6 font-semibold text-brand-navy shadow-lift hover:bg-blue-50">
                  ব্যবসা পেজ তৈরি করুন <ArrowRight className="size-4" aria-hidden />
                </Link>
              </div>
            </div>
            <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              {FEATURES.map(([I, t, d]) => (
                <li key={t} className="flex items-start gap-3 rounded-lg border border-white/10 bg-white/[0.07] p-4 backdrop-blur-sm">
                  <span className="flex size-9 shrink-0 items-center justify-center rounded-md bg-white/10 text-brand-lime"><I className="size-[18px]" aria-hidden /></span>
                  <span><span className="block font-display font-semibold">{t}</span><span className="block text-sm text-blue-100/90">{d}</span></span>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </section>

      {/* ---------- Final CTA ---------- */}
      <section className="mx-auto max-w-6xl px-4 pt-20 sm:px-6">
        <div className="card relative flex flex-col items-center gap-4 overflow-hidden px-6 py-14 text-center">
          <div className="bg-dots absolute inset-0 [mask-image:radial-gradient(closest-side,black,transparent)]" aria-hidden />
          <img src="/brand/logo-mark.png" alt="" className="relative h-16 w-auto drop-shadow-md" />
          <h2 className="relative text-3xl font-bold text-brand-navy sm:text-4xl">আজই শুরু করুন</h2>
          <p className="relative max-w-md text-muted">একটি অ্যাকাউন্ট — সেবা খুঁজুন, অথবা আপনার ব্যবসা পেজ তৈরি করুন।</p>
          <div className="relative mt-2 flex flex-wrap justify-center gap-3">
            <LinkButton to="/search" size="lg">সেবা খুঁজুন</LinkButton>
            <LinkButton to="/register" size="lg" variant="outline">অ্যাকাউন্ট তৈরি করুন</LinkButton>
          </div>
          <span className="relative mt-3 font-num text-xs tracking-[0.25em] text-subtle">DISCOVER · BOOK · CONNECT · GROW</span>
        </div>
      </section>
    </>
  );
}

function SectionHead({ eyebrow, title, subtitle, link }: { eyebrow?: string; title: string; subtitle?: string; link?: { to: string; label: string } }) {
  return (
    <div className="mb-7 flex flex-wrap items-end justify-between gap-3">
      <div>
        {eyebrow && <span className="mb-1.5 block text-sm font-semibold text-brand-green">{eyebrow}</span>}
        <h2 className="text-2xl font-bold text-brand-navy sm:text-[32px]">{title}</h2>
        {subtitle && <p className="mt-1 text-muted">{subtitle}</p>}
      </div>
      {link && <Link to={link.to} className="inline-flex items-center gap-1 text-sm font-semibold text-primary hover:text-primary-hover">{link.label} <ArrowRight className="size-4" aria-hidden /></Link>}
    </div>
  );
}
