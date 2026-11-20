import { useId } from "react";
import { Link } from "react-router";
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

// ---------- Rickshaw-art ornaments ----------
// Paint colours are repeated here as literals because SVG fills can't read Tailwind classes.

const PAINT = {
  navy: "#0b2350",
  red: "#c4373f",
  marigold: "#e9b24a",
  leaf: "#2f7f55",
  rose: "#d4607f",
  cream: "#fbf3e2",
  ink: "#17192b",
} as const;

const PETAL_ANGLES = [0, 45, 90, 135, 180, 225, 270, 315];

function RosetteShape({ color, center = PAINT.marigold }: { color: string; center?: string }) {
  return (
    <>
      <g fill={color}>
        {PETAL_ANGLES.map((a) => <ellipse key={a} cx="20" cy="9" rx="4.6" ry="8.5" transform={`rotate(${a} 20 20)`} />)}
      </g>
      <circle cx="20" cy="20" r="6.5" fill={center} />
      <circle cx="20" cy="20" r="2.6" fill={PAINT.ink} />
    </>
  );
}

/** Eight-petal flower from rickshaw panels. Decorative. */
export function Rosette({ color = PAINT.rose, className = "size-6" }: { color?: string; className?: string }) {
  return <svg viewBox="0 0 40 40" className={className} aria-hidden><RosetteShape color={color} /></svg>;
}

/** Diamond strip in the four paint colours, like the trim on a rickshaw hood. */
export function Band({ className = "" }: { className?: string }) {
  const id = useId();
  return (
    <svg width="100%" height="26" className={`block ${className}`} aria-hidden>
      <defs>
        <pattern id={id} width="64" height="26" patternUnits="userSpaceOnUse">
          <rect width="64" height="26" fill={PAINT.navy} />
          <path d="M16 3 L27 13 L16 23 L5 13 Z" fill={PAINT.red} />
          <path d="M16 8 L21 13 L16 18 L11 13 Z" fill={PAINT.marigold} />
          <path d="M48 3 L59 13 L48 23 L37 13 Z" fill={PAINT.leaf} />
          <path d="M48 8 L53 13 L48 18 L43 13 Z" fill={PAINT.cream} />
          <circle cx="32" cy="13" r="2.6" fill={PAINT.rose} />
          <circle cx="0" cy="13" r="2.6" fill={PAINT.rose} />
          <circle cx="64" cy="13" r="2.6" fill={PAINT.rose} />
        </pattern>
      </defs>
      <rect width="100%" height="26" fill={`url(#${id})`} />
    </svg>
  );
}

const FRINGE = {
  navy: { base: PAINT.navy, outer: PAINT.red, inner: PAINT.marigold, tassel: PAINT.rose },
  marigold: { base: PAINT.marigold, outer: PAINT.red, inner: PAINT.leaf, tassel: PAINT.red },
  soft: { base: "#f6e3b4", outer: PAINT.marigold, inner: PAINT.red, tassel: PAINT.leaf },
} as const;

/** Scalloped hood fringe with tassels, hung below a section of the given colour. */
export function Fringe({ tone = "navy", className = "" }: { tone?: keyof typeof FRINGE; className?: string }) {
  const id = useId();
  const c = FRINGE[tone];
  return (
    <svg width="100%" height="30" className={`block ${className}`} aria-hidden>
      <defs>
        <pattern id={id} width="44" height="30" patternUnits="userSpaceOnUse">
          <path d="M0 0 H44 V6 A22 18 0 0 1 0 6 Z" fill={c.base} />
          <path d="M6 6 A16 12 0 0 0 38 6 Z" fill={c.outer} />
          <path d="M13 6 A9 6 0 0 0 31 6 Z" fill={c.inner} />
          <line x1="22" y1="22" x2="22" y2="25" stroke={c.base} strokeWidth="1.5" />
          <circle cx="22" cy="27" r="2.6" fill={c.tassel} />
        </pattern>
      </defs>
      <rect width="100%" height="30" fill={`url(#${id})`} />
    </svg>
  );
}

/** Striped shop awning with a scalloped edge, the canopy over a para dokan. */
export function Awning({ className = "" }: { className?: string }) {
  const id = useId();
  return (
    <svg width="100%" height="64" className={`block ${className}`} aria-hidden>
      <defs>
        <pattern id={id} width="80" height="64" patternUnits="userSpaceOnUse">
          <rect y="8" width="40" height="38" fill={PAINT.red} />
          <rect x="40" y="8" width="40" height="38" fill={PAINT.cream} />
          <path d="M0 45 A20 17 0 0 0 40 45 Z" fill={PAINT.red} />
          <path d="M40 45 A20 17 0 0 0 80 45 Z" fill={PAINT.cream} />
          <path d="M40 8 V45" stroke="#00000014" strokeWidth="1" />
        </pattern>
      </defs>
      <rect width="100%" height="64" fill={`url(#${id})`} />
      <rect width="100%" height="9" fill={PAINT.navy} />
      <rect y="9" width="100%" height="3" fill={PAINT.marigold} />
    </svg>
  );
}

/** Faint scattered-flower wallpaper. Place inside a `relative` parent; set strength with opacity. */
export function PetalPattern({ className = "opacity-[0.045]", color = PAINT.cream }: { className?: string; color?: string }) {
  const id = useId();
  return (
    <svg className={`pointer-events-none absolute inset-0 size-full ${className}`} aria-hidden>
      <defs>
        <pattern id={id} width="120" height="120" patternUnits="userSpaceOnUse">
          <g transform="translate(10 10) scale(0.85)"><RosetteShape color={color} center={color} /></g>
          <g transform="translate(72 72) scale(0.55)"><RosetteShape color={color} center={color} /></g>
          <circle cx="90" cy="24" r="3" fill={color} />
          <circle cx="26" cy="92" r="3" fill={color} />
        </pattern>
      </defs>
      <rect width="100%" height="100%" fill={`url(#${id})`} />
    </svg>
  );
}

// ---------- Categories ----------

/** Paint pairs (background, letter) cycled across categories. */
const TILE_PAINTS = [
  { bg: PAINT.red, fg: PAINT.cream },
  { bg: PAINT.navy, fg: PAINT.marigold },
  { bg: PAINT.leaf, fg: PAINT.cream },
  { bg: PAINT.rose, fg: PAINT.cream },
  { bg: PAINT.marigold, fg: PAINT.navy },
];

export const paintFor = (id = 0) => TILE_PAINTS[Math.abs(id) % TILE_PAINTS.length];

/** First letter of a Bangla word as a whole grapheme, so conjuncts like "ফ্রি" stay intact. */
export function initialOf(name: string) {
  const text = name.trim();
  if (!text) return "";
  if (typeof Intl !== "undefined" && "Segmenter" in Intl) {
    const first = new Intl.Segmenter("bn", { granularity: "grapheme" }).segment(text)[Symbol.iterator]().next().value;
    if (first) return first.segment;
  }
  return [...text][0];
}

/**
 * Font for a single initial: Galada for Bangla (hand-painted look); Galada has no Latin glyphs loaded,
 * so a Latin initial uses the display face instead of falling back to a browser serif.
 */
export const initialFont = (ch: string) => (/^[\u0980-\u09FF]/.test(ch) ? "font-painted" : "font-display font-extrabold");

/** Category mark: the name's initial, hand-lettered in a painted roundel. */
export function CategoryTile({ id, name, className = "size-14 text-[28px]" }: { id: number; name: string; className?: string }) {
  const p = paintFor(id);
  return (
    <span aria-hidden className={`flex shrink-0 items-center justify-center rounded-full leading-none ${initialFont(initialOf(name))} ${className}`}
      style={{ background: p.bg, color: p.fg, boxShadow: `inset 0 0 0 4px ${PAINT.cream}, 0 0 0 2px ${p.bg}` }}>
      <span className="translate-y-[0.06em]">{initialOf(name)}</span>
    </span>
  );
}

function useCategory(categoryId?: number) {
  const { data } = useCategories();
  const all = data?.flatMap((c) => [c, ...c.children]) ?? [];
  return all.find((c) => c.id === categoryId);
}

/**
 * Panel used when a business hasn't uploaded a cover: warm sand ground with a faint flower wallpaper and the
 * category's painted roundel. Kept quiet so a grid of photo-less cards doesn't turn into blocks of paint.
 */
export function CoverArt({ categoryId, className = "" }: { categoryId?: number; className?: string }) {
  const category = useCategory(categoryId);
  return (
    <div className={`relative flex items-center justify-center overflow-hidden bg-sand-deep ${className}`} aria-hidden>
      <PetalPattern className="opacity-[0.22]" color="#e5d4b0" />
      {category && <CategoryTile id={category.id} name={category.nameBn} className="relative size-20 text-[40px]" />}
    </div>
  );
}

// ---------- Illustrations ----------

/** Small "nothing here yet" illustration: an open box with a rosette rising out of it. */
export function EmptyArt({ className = "size-28" }: { className?: string }) {
  return (
    <svg viewBox="0 0 120 120" className={className} aria-hidden>
      <circle cx="60" cy="62" r="50" fill="#f5e8cc" />
      <g transform="translate(44 18) scale(0.8)"><RosetteShape color={PAINT.rose} /></g>
      <path d="M30 56l30-12 30 12-30 12z" fill="#e5d4b0" />
      <path d="M30 56v28l30 12V68z" fill={PAINT.navy} />
      <path d="M90 56v28L60 96V68z" fill="#163a7a" />
      <path d="M30 56l-8 12 30 12 8-12z" fill={PAINT.red} />
      <path d="M90 56l8 12-30 12-8-12z" fill={PAINT.marigold} />
    </svg>
  );
}

/** Booking confirmed: check roundel with paint-coloured confetti. */
export function SuccessArt({ className = "size-36" }: { className?: string }) {
  const confetti = [
    [18, 30, PAINT.marigold], [104, 24, PAINT.navy], [110, 78, PAINT.red], [14, 86, PAINT.leaf], [60, 8, PAINT.rose], [92, 108, PAINT.marigold], [30, 110, PAINT.navy],
  ] as const;
  return (
    <svg viewBox="0 0 124 124" className={className} aria-hidden>
      <circle cx="62" cy="62" r="46" fill="#eaf4ec" />
      <circle cx="62" cy="62" r="34" fill={PAINT.cream} stroke={PAINT.leaf} strokeWidth="4" />
      <circle cx="62" cy="62" r="27" fill={PAINT.leaf} />
      <path d="M50 63l9 9 17-19" fill="none" stroke="#fff" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" />
      {confetti.map(([x, y, c], i) => (
        <rect key={i} x={x} y={y} width="8" height="4" rx="2" fill={c} transform={`rotate(${i * 37} ${x + 4} ${y + 2})`} />
      ))}
    </svg>
  );
}
