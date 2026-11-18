import type { ReactNode } from "react";
import { Link } from "react-router";
import {
  AirVent, BadgeCheck, Camera, CalendarCheck, GraduationCap, HeartPulse, Laptop, MapPin, MessagesSquare,
  Refrigerator, Scissors, Sparkles, Star, Wrench, Zap, type LucideIcon,
} from "lucide-react";
import { useCategories } from "./shared";

// ---------- Logo ----------

/** Pin mark + wordmark from the brand artwork. `compact` hides the wordmark on very small screens. */
export function Logo({ to = "/", compact = false, className = "" }: { to?: string; compact?: boolean; className?: string }) {
  return (
    <Link to={to} className={`flex shrink-0 items-center gap-2 ${className}`} aria-label="ServiceHub BD হোম">
      <img src="/brand/logo-mark.png" alt="" width={143} height={192} className="h-9 w-auto" />
      <img src="/brand/logo-wordmark.png" alt="ServiceHub BD" width={703} height={96}
        className={`h-[18px] w-auto sm:h-5 ${compact ? "max-[380px]:hidden" : ""}`} />
    </Link>
  );
}

export function LogoFull({ className = "" }: { className?: string }) {
  return <img src="/brand/logo-full.png" alt="ServiceHub BD — Discover · Book · Connect · Grow" width={757} height={640} className={className} />;
}

// ---------- Categories ----------

type CatMeta = { icon: LucideIcon; tile: string; cover: string; ink: string };

const CATEGORY_META: Record<string, CatMeta> = {
  zap: { icon: Zap, tile: "bg-amber-50 text-amber-600 ring-amber-100", cover: "from-amber-100 via-orange-50 to-white", ink: "text-amber-500" },
  sparkles: { icon: Sparkles, tile: "bg-teal-50 text-teal-600 ring-teal-100", cover: "from-teal-100 via-cyan-50 to-white", ink: "text-teal-500" },
  scissors: { icon: Scissors, tile: "bg-rose-50 text-rose-600 ring-rose-100", cover: "from-rose-100 via-pink-50 to-white", ink: "text-rose-400" },
  wrench: { icon: Wrench, tile: "bg-blue-50 text-blue-600 ring-blue-100", cover: "from-blue-100 via-sky-50 to-white", ink: "text-blue-500" },
  "air-vent": { icon: AirVent, tile: "bg-sky-50 text-sky-600 ring-sky-100", cover: "from-sky-100 via-blue-50 to-white", ink: "text-sky-500" },
  refrigerator: { icon: Refrigerator, tile: "bg-indigo-50 text-indigo-600 ring-indigo-100", cover: "from-indigo-100 via-blue-50 to-white", ink: "text-indigo-400" },
  camera: { icon: Camera, tile: "bg-violet-50 text-violet-600 ring-violet-100", cover: "from-violet-100 via-fuchsia-50 to-white", ink: "text-violet-400" },
  laptop: { icon: Laptop, tile: "bg-slate-100 text-slate-700 ring-slate-200", cover: "from-slate-200 via-slate-50 to-white", ink: "text-slate-500" },
  "heart-pulse": { icon: HeartPulse, tile: "bg-red-50 text-red-600 ring-red-100", cover: "from-red-100 via-rose-50 to-white", ink: "text-red-400" },
  "graduation-cap": { icon: GraduationCap, tile: "bg-lime-50 text-lime-700 ring-lime-100", cover: "from-lime-100 via-green-50 to-white", ink: "text-lime-600" },
  "messages-square": { icon: MessagesSquare, tile: "bg-emerald-50 text-emerald-600 ring-emerald-100", cover: "from-emerald-100 via-teal-50 to-white", ink: "text-emerald-500" },
};
const FALLBACK: CatMeta = { icon: Wrench, tile: "bg-primary-light text-primary ring-blue-100", cover: "from-blue-100 via-sky-50 to-white", ink: "text-blue-500" };

export const categoryMeta = (icon?: string | null) => (icon && CATEGORY_META[icon]) || FALLBACK;

function useCategoryIcon(categoryId?: number) {
  const { data } = useCategories();
  const all = data?.flatMap((c) => [c, ...c.children]) ?? [];
  return all.find((c) => c.id === categoryId)?.icon ?? null;
}

export function CategoryTile({ icon, className = "" }: { icon?: string | null; className?: string }) {
  const m = categoryMeta(icon);
  const I = m.icon;
  return (
    <span className={`flex size-12 shrink-0 items-center justify-center rounded-md ring-1 ${m.tile} ${className}`} aria-hidden>
      <I className="size-6" strokeWidth={1.75} />
    </span>
  );
}

/** Illustrated cover used when a business hasn't uploaded one: category tint, dot grid and a big line icon. */
export function CoverArt({ categoryId, className = "" }: { categoryId?: number; className?: string }) {
  const m = categoryMeta(useCategoryIcon(categoryId));
  const I = m.icon;
  return (
    <div className={`relative overflow-hidden bg-gradient-to-br ${m.cover} ${className}`} aria-hidden>
      <div className="bg-dots absolute inset-0 opacity-70" />
      <svg className="absolute -bottom-10 -left-8 h-40 w-40 text-white/70" viewBox="0 0 100 100"><circle cx="50" cy="50" r="50" fill="currentColor" /></svg>
      <svg className="absolute -top-6 right-16 h-24 w-24 text-white/60" viewBox="0 0 100 100"><circle cx="50" cy="50" r="50" fill="currentColor" /></svg>
      <I className={`absolute right-6 bottom-4 size-20 rotate-[-8deg] opacity-80 ${m.ink}`} strokeWidth={1.25} />
    </div>
  );
}

// ---------- Stickers ----------

export function Sticker({ children, tilt = 0, className = "" }: { children: ReactNode; tilt?: number; className?: string }) {
  return (
    <span className={`sticker ${className}`} style={{ rotate: `${tilt}deg` }}>
      {children}
    </span>
  );
}

// ---------- Illustrations ----------

/** Landing hero composition: a booking card on a brand panel, ringed by stickers. Decorative only. */
export function HeroArt() {
  const slots = ["১০:০০ AM", "১১:৩০ AM", "২:০০ PM", "৪:৩০ PM"];
  return (
    <div aria-hidden className="relative mx-auto w-full max-w-[460px] select-none">
      <div className="bg-brand relative aspect-[4/4.2] overflow-hidden rounded-xl shadow-pop sm:aspect-[4/3.9]">
        <div className="absolute inset-0 opacity-30 [background-image:radial-gradient(rgb(255_255_255/0.35)_1px,transparent_1px)] [background-size:20px_20px]" />
        <svg className="absolute -right-16 -bottom-20 h-72 w-72 text-brand-lime/30" viewBox="0 0 200 200"><circle cx="100" cy="100" r="100" fill="currentColor" /></svg>
        <svg className="absolute -top-10 -left-10 h-40 w-40 text-white/10" viewBox="0 0 200 200"><circle cx="100" cy="100" r="100" fill="currentColor" /></svg>
        {/* skyline */}
        <svg className="absolute inset-x-0 bottom-0 h-24 w-full text-white/10" viewBox="0 0 400 100" preserveAspectRatio="none">
          <path fill="currentColor" d="M0 100V60h30V40h25v20h20V25h30v35h15V45h28v55h12V30l20-15 20 15v70h18V50h30v50h14V35h26v65h22V55h30v45h20V70h20v30z" />
        </svg>

        <div className="absolute inset-x-6 top-8 rounded-lg bg-white p-4 shadow-lift sm:inset-x-10">
          <div className="relative -mx-4 -mt-4 mb-3 h-20 overflow-hidden rounded-t-lg bg-gradient-to-br from-sky-100 via-blue-50 to-white">
            <div className="bg-dots absolute inset-0" />
            <AirVent className="absolute right-4 bottom-2 size-12 text-sky-400" strokeWidth={1.25} />
          </div>
          <div className="flex items-center gap-2">
            <span className="font-display text-base font-semibold text-ink">রহমান কুলিং সার্ভিস</span>
            <BadgeCheck className="size-4 text-brand-green" />
          </div>
          <div className="mt-0.5 flex items-center gap-2 text-xs text-subtle">
            <span className="flex items-center gap-0.5"><Star className="size-3.5 fill-amber-400 text-amber-400" />৪.৯</span>·
            <span className="flex items-center gap-0.5"><MapPin className="size-3.5" />উত্তরা, ঢাকা</span>
          </div>
          <div className="mt-3 text-xs font-medium text-muted">আগামীকাল ফাঁকা সময়</div>
          <div className="mt-1.5 grid grid-cols-4 gap-1.5">
            {slots.map((s, i) => (
              <span key={s} className={`rounded-md border py-1.5 text-center font-num text-[11px] font-medium ${i === 1 ? "border-primary bg-primary text-white" : "border-line text-ink"}`}>{s}</span>
            ))}
          </div>
          <div className="mt-3 rounded-md bg-primary py-2 text-center text-sm font-semibold text-white">বুকিং নিশ্চিত করুন</div>
        </div>
      </div>

      <Sticker tilt={-6} className="float-slow absolute -top-3 -left-2 text-brand-green sm:-left-6">
        <BadgeCheck className="size-4" /> যাচাইকৃত ব্যবসা
      </Sticker>
      <Sticker tilt={5} className="float-slower absolute top-1/2 -right-2 text-amber-600 sm:-right-6">
        <Star className="size-4 fill-amber-400 text-amber-400" /> ৪.৯ রেটিং
      </Sticker>
      <Sticker tilt={-3} className="float-slow absolute -bottom-4 left-6 text-primary">
        <CalendarCheck className="size-4" /> ২ মিনিটে বুকিং
      </Sticker>
    </div>
  );
}

/** Small "nothing here yet" illustration: an open box with sparkles in brand colours. */
export function EmptyArt({ className = "size-28" }: { className?: string }) {
  return (
    <svg viewBox="0 0 120 120" className={className} aria-hidden>
      <circle cx="60" cy="62" r="50" fill="#eaf1fc" />
      <path d="M30 56l30-12 30 12-30 12z" fill="#cfe0fa" />
      <path d="M30 56v28l30 12V68z" fill="#1f62c9" />
      <path d="M90 56v28L60 96V68z" fill="#164b98" />
      <path d="M30 56l-8 12 30 12 8-12z" fill="#5b8fe0" />
      <path d="M90 56l8 12-30 12-8-12z" fill="#3a74d6" />
      <circle cx="84" cy="30" r="4" fill="#81b83a" />
      <path d="M40 24v10M35 29h10" stroke="#81b83a" strokeWidth="3" strokeLinecap="round" />
      <path d="M96 44v6M93 47h6" stroke="#2a8fb5" strokeWidth="2.5" strokeLinecap="round" />
    </svg>
  );
}

/** Booking confirmed: check badge with confetti. */
export function SuccessArt({ className = "size-36" }: { className?: string }) {
  const confetti = [
    [18, 30, "#81b83a"], [104, 24, "#1f62c9"], [110, 78, "#f59e0b"], [14, 86, "#2a8fb5"], [60, 8, "#f43f5e"], [92, 108, "#81b83a"], [30, 110, "#1f62c9"],
  ] as const;
  return (
    <svg viewBox="0 0 124 124" className={className} aria-hidden>
      <circle cx="62" cy="62" r="46" fill="#eff7e8" />
      <circle cx="62" cy="62" r="32" fill="#3f8f24" />
      <path d="M48 63l10 10 19-21" fill="none" stroke="#fff" strokeWidth="7" strokeLinecap="round" strokeLinejoin="round" />
      {confetti.map(([x, y, c], i) => (
        <rect key={i} x={x} y={y} width="8" height="4" rx="2" fill={c} transform={`rotate(${i * 37} ${x + 4} ${y + 2})`} />
      ))}
    </svg>
  );
}
