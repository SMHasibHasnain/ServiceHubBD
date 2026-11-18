import { useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { useSearchParams } from "react-router";
import { ArrowDownRight, ArrowUpRight, BarChart3, Minus, Table2 } from "lucide-react";
import type { Metric, Slice, TimeSeries } from "../api/types";
import { bn, num, taka } from "../lib/format";
import { Segmented } from "./ui";

/*
 * Chart kit for every analytics page. Colours come from the validated reference palette
 * (dataviz skill): categorical slots in fixed order, one-hue sequential/ordinal blues,
 * status mix keyed by entity so a status keeps its colour on every chart.
 * Every chart ships a legend (≥2 series), a hover/focus tooltip and a table view.
 */

export const VIZ = {
  series: ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6"],
  seq: ["#cde2fb", "#b7d3f6", "#9ec5f4", "#86b6ef", "#6da7ec", "#5598e7", "#3987e5", "#2a78d6", "#256abf", "#1c5cab", "#184f95", "#104281", "#0d366b"],
  ordinal: ["#86b6ef", "#5598e7", "#2a78d6", "#1c5cab", "#104281"],
  ordinal4: ["#86b6ef", "#3987e5", "#1c5cab", "#0d366b"],
  grid: "#eceef3",
  axis: "#c9cdd8",
  muted: "#7d8494",
  empty: "#f2f4f8",
};

/** Booking status → colour. Ordered completed…no_show this sequence passes the adjacent-pair gates. */
export const STATUS_COLOR: Record<string, string> = {
  completed: "#008300", confirmed: "#2a78d6", pending: "#eda100", rescheduled: "#6250d6",
  cancelled: "#e34948", rejected: "#1baf7a", no_show: "#eb6834",
};
const DARK_LABEL_FILLS = new Set(["#eda100"]); // yellow takes near-black text, everything else white

// ---------- Formatting ----------

export type Fmt = (n: number) => string;
const compactFmt = new Intl.NumberFormat("bn-BD", { notation: "compact", maximumFractionDigits: 1 });
export const fmt = {
  count: ((n) => num(n)) as Fmt,
  taka: ((n) => taka(n)) as Fmt,
  pct: ((n) => `${num(n, n % 1 === 0 ? 0 : 1)}%`) as Fmt,
  rating: ((n) => (n ? num(n, 1) : "—")) as Fmt,
  minutes: ((n) => (n >= 60 ? `${num(n / 60, 1)} ঘণ্টা` : `${num(n)} মিনিট`)) as Fmt,
  hours: ((n) => (n >= 48 ? `${num(n / 24, 1)} দিন` : n < 1 ? `${num(Math.round(n * 60))} মিনিট` : `${num(n, n < 10 ? 1 : 0)} ঘণ্টা`)) as Fmt,
  compact: ((n) => (Math.abs(n) < 10_000 ? num(n) : compactFmt.format(n))) as Fmt,
};
const dayLabel = (iso: string) => new Date(iso + "T00:00:00").toLocaleDateString("bn-BD", { day: "numeric", month: "short" });
const dayLong = (iso: string) => new Date(iso + "T00:00:00").toLocaleDateString("bn-BD", { weekday: "long", day: "numeric", month: "long" });

function niceMax(max: number) {
  if (max <= 0) return 4;
  const pow = 10 ** Math.floor(Math.log10(max));
  const step = [1, 2, 2.5, 5, 10].find((s) => (s * pow * 4) >= max)! * pow;
  return step * 4;
}

function useWidth<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  const [width, setWidth] = useState(0);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const ro = new ResizeObserver(([e]) => setWidth(Math.floor(e.contentRect.width)));
    ro.observe(el);
    setWidth(el.clientWidth);
    return () => ro.disconnect();
  }, []);
  return [ref, width] as const;
}

// ---------- Range filter (one row, above the charts) ----------

export function useRange(defaultDays = 30) {
  const [params, setParams] = useSearchParams();
  const days = [7, 30, 90].includes(Number(params.get("days"))) ? Number(params.get("days")) : defaultDays;
  const setDays = (d: number) => setParams((p) => { p.set("days", String(d)); return p; }, { replace: true });
  return [days, setDays] as const;
}

export function RangeTabs({ days, onChange }: { days: number; onChange: (d: number) => void }) {
  return (
    <Segmented label="সময়সীমা" value={String(days)} onChange={(v) => onChange(Number(v))}
      options={[{ value: "7", label: "গত ৭ দিন" }, { value: "30", label: "গত ৩০ দিন" }, { value: "90", label: "গত ৯০ দিন" }]} />
  );
}

export function AnalyticsToolbar({ days, onChange, from, to, children }: { days: number; onChange: (d: number) => void; from?: string; to?: string; children?: ReactNode }) {
  return (
    <div className="mb-5 flex flex-wrap items-center gap-3">
      <RangeTabs days={days} onChange={onChange} />
      {from && to && <span className="text-sm text-subtle">{dayLabel(from)} – {dayLabel(to)} · আগের {bn(days)} দিনের সাথে তুলনা</span>}
      {children}
    </div>
  );
}

// ---------- KPI tile ----------

export function KpiTile({ label, metric, format = fmt.count, upIsGood = true, deltaMode = "relative", note, icon }: {
  label: string; metric?: Metric; format?: Fmt; upIsGood?: boolean | null; deltaMode?: "relative" | "points"; note?: ReactNode; icon?: ReactNode;
}) {
  const v = metric?.value ?? 0;
  const prev = metric?.previous;
  let delta: ReactNode = null;
  if (prev != null) {
    const diff = v - prev;
    const text = deltaMode === "points"
      ? `${num(Math.abs(diff), 1)} পয়েন্ট`
      : prev === 0 ? null : `${num(Math.abs((diff / prev) * 100), Math.abs(diff / prev) < 0.1 ? 1 : 0)}%`;
    const dir = Math.abs(diff) < 1e-9 ? 0 : diff > 0 ? 1 : -1;
    const good = upIsGood == null || dir === 0 ? null : (dir > 0) === upIsGood;
    const Icon = dir > 0 ? ArrowUpRight : dir < 0 ? ArrowDownRight : Minus;
    const tone = good == null ? "text-muted" : good ? "text-[#006300]" : "text-red-700";
    delta = prev === 0 && deltaMode === "relative"
      ? v > 0 ? <span className="text-muted">আগের সময়ে ছিল না</span> : null
      : (
        <span className={`inline-flex items-center gap-0.5 font-medium ${tone}`}>
          <Icon className="size-3.5" aria-hidden />
          <span className="sr-only">{dir > 0 ? "বেড়েছে" : dir < 0 ? "কমেছে" : "অপরিবর্তিত"}</span>
          {dir === 0 ? "অপরিবর্তিত" : text}
        </span>
      );
  }
  return (
    <div className="card flex min-w-0 flex-col gap-1.5 p-4">
      <div className="flex items-start justify-between gap-2 text-sm text-muted">
        <span>{label}</span>
        {icon && <span className="text-zinc-400 [&>svg]:size-4" aria-hidden>{icon}</span>}
      </div>
      <span className="truncate text-[26px] leading-tight font-semibold text-ink">{metric ? format(v) : "—"}</span>
      <span className="flex flex-wrap items-center gap-x-1.5 text-xs text-subtle">
        {delta}{delta && prev != null && !(prev === 0 && deltaMode === "relative") && <span>আগের তুলনায়</span>}{note}
      </span>
    </div>
  );
}

// ---------- Card with table view ----------

export type TableSpec = { columns: string[]; rows: (string | number)[][] };

export function ChartCard({ title, subtitle, table, children, className = "" }: {
  title: string; subtitle?: string; table?: TableSpec; children: ReactNode; className?: string;
}) {
  const [asTable, setAsTable] = useState(false);
  return (
    <section className={`card flex min-w-0 flex-col p-4 sm:p-5 ${className}`} aria-label={title}>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-base font-semibold">{title}</h2>
          {subtitle && <p className="mt-0.5 text-sm text-subtle">{subtitle}</p>}
        </div>
        {table && (
          <button type="button" onClick={() => setAsTable((t) => !t)} aria-pressed={asTable}
            className="inline-flex min-h-9 shrink-0 items-center gap-1.5 rounded-md border border-line px-2.5 text-xs font-medium text-muted hover:bg-zinc-50 hover:text-ink">
            {asTable ? <BarChart3 className="size-3.5" aria-hidden /> : <Table2 className="size-3.5" aria-hidden />}
            {asTable ? "চার্ট" : "টেবিল"}
          </button>
        )}
      </div>
      {asTable && table ? <DataTable {...table} /> : children}
    </section>
  );
}

function DataTable({ columns, rows }: TableSpec) {
  if (!rows.length) return <Empty />;
  return (
    <div className="max-h-80 overflow-auto rounded-md border border-line">
      <table className="w-full text-left text-sm">
        <thead className="sticky top-0 bg-zinc-50 text-xs text-subtle">
          <tr>{columns.map((c, i) => <th key={c} className={`px-3 py-2 font-medium ${i > 0 ? "text-right" : ""}`}>{c}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map((r, i) => (
            <tr key={i} className="border-t border-line">
              {r.map((cell, j) => <td key={j} className={`px-3 py-1.5 ${j > 0 ? "text-right tabular-nums" : ""}`}>{typeof cell === "number" ? num(cell) : cell}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function Empty({ text = "এই সময়ে কোনো তথ্য নেই।" }: { text?: string }) {
  return <div className="flex min-h-32 items-center justify-center rounded-md border border-dashed border-line text-sm text-subtle">{text}</div>;
}

// ---------- Tooltip ----------

type Tip = { x: number; y: number; title: string; rows: { color?: string; label: string; value: string; key?: "line" | "rect" }[] } | null;

function Tooltip({ tip, width }: { tip: Tip; width: number }) {
  if (!tip) return null;
  const left = Math.min(Math.max(tip.x, 80), Math.max(width - 80, 80));
  return (
    <div role="status" className="pointer-events-none absolute z-10 min-w-36 -translate-x-1/2 -translate-y-full rounded-md border border-line bg-white px-3 py-2 text-xs shadow-pop"
      style={{ left, top: tip.y - 10 }}>
      <div className="mb-1 text-subtle">{tip.title}</div>
      {tip.rows.map((r) => (
        <div key={r.label} className="flex items-center gap-2 py-0.5">
          {r.color && (r.key === "rect"
            ? <span className="size-2.5 rounded-[2px]" style={{ background: r.color }} aria-hidden />
            : <span className="h-0.5 w-3 rounded-full" style={{ background: r.color }} aria-hidden />)}
          <span className="text-sm font-semibold text-ink">{r.value}</span>
          <span className="text-muted">{r.label}</span>
        </div>
      ))}
    </div>
  );
}

export function Legend({ items, kind = "line" }: { items: { label: string; color: string }[]; kind?: "line" | "rect" }) {
  return (
    <ul className="mb-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted">
      {items.map((i) => (
        <li key={i.label} className="flex items-center gap-1.5">
          {kind === "line" ? <span className="h-0.5 w-4 rounded-full" style={{ background: i.color }} aria-hidden />
            : <span className="size-2.5 rounded-[2px]" style={{ background: i.color }} aria-hidden />}
          {i.label}
        </li>
      ))}
    </ul>
  );
}

// ---------- Line chart (trend over time, crosshair tooltip) ----------

export type SeriesDef = { key: string; label: string; color: string };

export function LineChart({ data, series, format = fmt.count, height = 220 }: { data: TimeSeries; series: SeriesDef[]; format?: Fmt; height?: number }) {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [idx, setIdx] = useState<number | null>(null);
  const cols = series.map((s) => data.keys.indexOf(s.key));
  const pts = data.points;
  const max = niceMax(Math.max(0, ...pts.flatMap((p) => cols.map((c) => p.values[c]))));
  const m = { l: 40, r: 12, t: 10, b: 26 };
  const w = Math.max(width - m.l - m.r, 10);
  const h = height - m.t - m.b;
  const x = (i: number) => m.l + (pts.length <= 1 ? w / 2 : (i / (pts.length - 1)) * w);
  const y = (v: number) => m.t + h - (v / max) * h;
  const ticks = [0, 1, 2, 3, 4].map((i) => (max / 4) * i);
  const xTicks = useMemo(() => {
    const n = Math.min(pts.length, width < 420 ? 4 : 6);
    return Array.from(new Set(Array.from({ length: n }, (_, i) => Math.round((i / Math.max(n - 1, 1)) * (pts.length - 1)))));
  }, [pts.length, width]);
  const empty = pts.every((p) => cols.every((c) => !p.values[c]));

  const pick = (clientX: number, el: Element) => {
    const r = el.getBoundingClientRect();
    const rel = clientX - r.left - m.l;
    setIdx(Math.max(0, Math.min(pts.length - 1, Math.round((rel / w) * (pts.length - 1)))));
  };
  const onKey = (e: KeyboardEvent) => {
    if (e.key === "ArrowLeft") setIdx((i) => Math.max(0, (i ?? pts.length) - 1));
    if (e.key === "ArrowRight") setIdx((i) => Math.min(pts.length - 1, (i ?? -1) + 1));
  };
  const tip: Tip = idx == null ? null : {
    x: x(idx), y: Math.min(...cols.map((c) => y(pts[idx].values[c]))),
    title: dayLong(pts[idx].date),
    rows: series.map((s, i) => ({ color: s.color, label: s.label, value: format(pts[idx].values[cols[i]]) })),
  };

  return (
    <div>
      {series.length > 1 && <Legend items={series} />}
      <div ref={ref} className="relative" style={{ height }}>
        {width > 0 && (
          <svg width={width} height={height} role="img" tabIndex={0} className="outline-none focus-visible:ring-2 focus-visible:ring-primary/40 rounded-md"
            aria-label={`${series.map((s) => s.label).join(" ও ")} — ${bn(pts.length)} দিনের প্রবণতা। বাম-ডান তীর চেপে দিন বদলান।`}
            onPointerMove={(e) => pick(e.clientX, e.currentTarget)} onPointerLeave={() => setIdx(null)}
            onFocus={() => setIdx(pts.length - 1)} onBlur={() => setIdx(null)} onKeyDown={onKey}>
            {ticks.map((t) => (
              <g key={t}>
                <line x1={m.l} x2={m.l + w} y1={y(t)} y2={y(t)} stroke={t === 0 ? VIZ.axis : VIZ.grid} strokeWidth={1} />
                <text x={m.l - 8} y={y(t)} dy="0.32em" textAnchor="end" fontSize={11} fill={VIZ.muted} className="tabular-nums">{fmt.compact(t)}</text>
              </g>
            ))}
            {xTicks.map((i) => (
              <text key={i} x={x(i)} y={height - 6} textAnchor={i === 0 ? "start" : i === pts.length - 1 ? "end" : "middle"} fontSize={11} fill={VIZ.muted}>{dayLabel(pts[i].date)}</text>
            ))}
            {series.length === 1 && !empty && (
              <path d={`M${x(0)},${y(0)} ${pts.map((p, i) => `L${x(i)},${y(p.values[cols[0]])}`).join(" ")} L${x(pts.length - 1)},${y(0)} Z`} fill={series[0].color} opacity={0.1} />
            )}
            {series.map((s, si) => (
              <path key={s.key} d={pts.map((p, i) => `${i ? "L" : "M"}${x(i)},${y(p.values[cols[si]])}`).join(" ")}
                fill="none" stroke={s.color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
            ))}
            {/* end dots */}
            {idx == null && pts.length > 0 && series.map((s, si) => (
              <circle key={s.key} cx={x(pts.length - 1)} cy={y(pts[pts.length - 1].values[cols[si]])} r={4} fill={s.color} stroke="#fff" strokeWidth={2} />
            ))}
            {idx != null && (
              <g>
                <line x1={x(idx)} x2={x(idx)} y1={m.t} y2={m.t + h} stroke={VIZ.axis} strokeWidth={1} />
                {series.map((s, si) => <circle key={s.key} cx={x(idx)} cy={y(pts[idx].values[cols[si]])} r={4.5} fill={s.color} stroke="#fff" strokeWidth={2} />)}
              </g>
            )}
            {empty && <text x={m.l + w / 2} y={m.t + h / 2} textAnchor="middle" fontSize={13} fill={VIZ.muted}>এই সময়ে কোনো কার্যক্রম নেই</text>}
          </svg>
        )}
        <Tooltip tip={tip} width={width} />
      </div>
    </div>
  );
}

// ---------- Column chart (one measure per day) ----------

export function ColumnChart({ data, valueKey, label, color = VIZ.series[0], format = fmt.count, height = 200 }: {
  data: TimeSeries; valueKey: string; label: string; color?: string; format?: Fmt; height?: number;
}) {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [idx, setIdx] = useState<number | null>(null);
  const c = data.keys.indexOf(valueKey);
  const pts = data.points;
  const max = niceMax(Math.max(0, ...pts.map((p) => p.values[c])));
  const m = { l: 44, r: 8, t: 10, b: 26 };
  const w = Math.max(width - m.l - m.r, 10);
  const h = height - m.t - m.b;
  const band = w / Math.max(pts.length, 1);
  const bw = Math.max(Math.min(24, band - 2), 1);
  const y = (v: number) => m.t + h - (v / max) * h;
  const ticks = [0, 1, 2, 3, 4].map((i) => (max / 4) * i);
  const n = Math.min(pts.length, width < 420 ? 4 : 6);
  const xTicks = Array.from(new Set(Array.from({ length: n }, (_, i) => Math.round((i / Math.max(n - 1, 1)) * (pts.length - 1)))));
  const bar = (i: number, v: number) => {
    const x0 = m.l + i * band + (band - bw) / 2, top = y(v), base = y(0), r = Math.min(4, bw / 2, base - top);
    return `M${x0},${base} V${top + r} Q${x0},${top} ${x0 + r},${top} H${x0 + bw - r} Q${x0 + bw},${top} ${x0 + bw},${top + r} V${base} Z`;
  };
  const empty = pts.every((p) => !p.values[c]);
  const tip: Tip = idx == null ? null : {
    x: m.l + idx * band + band / 2, y: y(pts[idx].values[c]), title: dayLong(pts[idx].date),
    rows: [{ color, key: "rect", label, value: format(pts[idx].values[c]) }],
  };
  return (
    <div ref={ref} className="relative" style={{ height }}>
      {width > 0 && (
        <svg width={width} height={height} role="img" tabIndex={0} aria-label={`${label} — প্রতিদিনের হিসাব`} className="outline-none focus-visible:ring-2 focus-visible:ring-primary/40 rounded-md"
          onFocus={() => setIdx(pts.length - 1)} onBlur={() => setIdx(null)}
          onKeyDown={(e) => { if (e.key === "ArrowLeft") setIdx((i) => Math.max(0, (i ?? pts.length) - 1)); if (e.key === "ArrowRight") setIdx((i) => Math.min(pts.length - 1, (i ?? -1) + 1)); }}
          onPointerLeave={() => setIdx(null)}>
          {ticks.map((t) => (
            <g key={t}>
              <line x1={m.l} x2={m.l + w} y1={y(t)} y2={y(t)} stroke={t === 0 ? VIZ.axis : VIZ.grid} />
              <text x={m.l - 8} y={y(t)} dy="0.32em" textAnchor="end" fontSize={11} fill={VIZ.muted} className="tabular-nums">{fmt.compact(t)}</text>
            </g>
          ))}
          {xTicks.map((i) => <text key={i} x={m.l + i * band + band / 2} y={height - 6} textAnchor="middle" fontSize={11} fill={VIZ.muted}>{dayLabel(pts[i].date)}</text>)}
          {pts.map((p, i) => (
            <g key={p.date} onPointerEnter={() => setIdx(i)}>
              <rect x={m.l + i * band} y={m.t} width={band} height={h} fill="transparent" />
              {p.values[c] > 0 && <path d={bar(i, p.values[c])} fill={color} opacity={idx == null || idx === i ? 1 : 0.55} />}
            </g>
          ))}
          {empty && <text x={m.l + w / 2} y={m.t + h / 2} textAnchor="middle" fontSize={13} fill={VIZ.muted}>এই সময়ে কোনো তথ্য নেই</text>}
        </svg>
      )}
      <Tooltip tip={tip} width={width} />
    </div>
  );
}

// ---------- Ranked horizontal bars ----------

export function BarList({ items, format = fmt.count, secondary, colors, max: forcedMax, showShare }: {
  items: Slice[]; format?: Fmt; secondary?: { label: string; format: Fmt }; colors?: string[]; max?: number; showShare?: boolean;
}) {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [tip, setTip] = useState<Tip>(null);
  if (!items.length) return <Empty />;
  const max = forcedMax ?? Math.max(...items.map((i) => i.value), 1);
  const total = items.reduce((s, i) => s + i.value, 0);
  return (
    <div ref={ref} className="relative">
      <ul className="flex flex-col gap-3">
        {items.map((it, i) => {
          const color = colors?.[i] ?? VIZ.series[0];
          const show = (el: HTMLElement) => {
            const box = el.getBoundingClientRect(), host = ref.current!.getBoundingClientRect();
            setTip({
              x: box.left - host.left + Math.min(box.width, 220) / 2, y: box.top - host.top, title: it.label,
              rows: [
                { color, key: "rect", label: "", value: format(it.value) + (showShare && total ? ` (${fmt.pct(Math.round((it.value / total) * 1000) / 10)})` : "") },
                ...(secondary && it.secondary != null ? [{ label: secondary.label, value: secondary.format(it.secondary) }] : []),
              ],
            });
          };
          return (
            <li key={it.key} tabIndex={0} className="group rounded-sm outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              onPointerEnter={(e) => show(e.currentTarget)} onPointerLeave={() => setTip(null)} onFocus={(e) => show(e.currentTarget)} onBlur={() => setTip(null)}>
              <div className="mb-1 flex items-baseline justify-between gap-3 text-sm">
                <span className="min-w-0 truncate text-ink">{it.label}</span>
                <span className="shrink-0 font-semibold tabular-nums">
                  {format(it.value)}
                  {secondary && it.secondary != null && <span className="ml-1.5 text-xs font-normal text-subtle">· {secondary.format(it.secondary)}</span>}
                </span>
              </div>
              <div className="h-2.5 w-full rounded-r-[4px] bg-transparent">
                <div className="h-full rounded-r-[4px] transition-opacity group-hover:opacity-80" style={{ width: `${Math.max((it.value / max) * 100, it.value ? 1.5 : 0)}%`, background: color }} />
              </div>
            </li>
          );
        })}
      </ul>
      <Tooltip tip={tip} width={width} />
    </div>
  );
}

/** Funnel as ranked bars on an ordinal ramp, each step with its conversion from the previous one. */
export function Funnel({ steps }: { steps: Slice[] }) {
  if (!steps.length) return <Empty />;
  const first = steps[0].value || 1;
  return (
    <ol className="flex flex-col gap-3">
      {steps.map((s, i) => {
        const prev = i ? steps[i - 1].value : null;
        return (
          <li key={s.key}>
            <div className="mb-1 flex items-baseline justify-between gap-3 text-sm">
              <span className="text-ink">{s.label}</span>
              <span className="font-semibold tabular-nums">{num(s.value)}
                {prev != null && <span className="ml-1.5 text-xs font-normal text-subtle">· আগের ধাপের {fmt.pct(prev ? Math.round((s.value / prev) * 1000) / 10 : 0)}</span>}
              </span>
            </div>
            <div className="h-6 rounded-r-[4px]" style={{ width: `${Math.max((s.value / first) * 100, s.value ? 1.5 : 0)}%`, background: VIZ.ordinal[Math.min(i, VIZ.ordinal.length - 1)] }} />
          </li>
        );
      })}
    </ol>
  );
}

// ---------- Tall segmented bar (one whole, ≤ 7 parts) ----------

export function SegmentedBar({ parts, colors, format = fmt.count }: { parts: Slice[]; colors: Record<string, string> | string[]; format?: Fmt }) {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [tip, setTip] = useState<Tip>(null);
  const shown = parts.filter((p) => p.value > 0);
  const total = shown.reduce((s, p) => s + p.value, 0);
  if (!total) return <Empty />;
  const colorOf = (p: Slice, i: number) => (Array.isArray(colors) ? colors[i % colors.length] : colors[p.key] ?? VIZ.series[i % VIZ.series.length]);
  const usable = width - 2 * (shown.length - 1);
  return (
    <div>
      <div ref={ref} className="relative">
        <div className="flex h-7 gap-[2px]" role="img" aria-label={shown.map((p) => `${p.label} ${format(p.value)}`).join(", ")}>
          {shown.map((p) => {
            const share = p.value / total;
            const fill = colorOf(p, parts.indexOf(p));
            const label = `${num(Math.round(share * 100))}%`;
            const fits = usable * share >= 44;
            return (
              <div key={p.key} tabIndex={0} className="flex items-center rounded-[4px] px-2 text-xs font-semibold outline-none focus-visible:ring-2 focus-visible:ring-primary/40 hover:opacity-85"
                style={{ flexGrow: p.value, flexBasis: 0, background: fill, color: DARK_LABEL_FILLS.has(fill) ? "#0b0b0b" : "#fff" }}
                onPointerEnter={(e) => {
                  const b = e.currentTarget.getBoundingClientRect(), host = ref.current!.getBoundingClientRect();
                  setTip({ x: b.left - host.left + b.width / 2, y: 0, title: p.label, rows: [{ color: fill, key: "rect", label: `মোটের ${label}`, value: format(p.value) }] });
                }}
                onFocus={(e) => {
                  const b = e.currentTarget.getBoundingClientRect(), host = ref.current!.getBoundingClientRect();
                  setTip({ x: b.left - host.left + b.width / 2, y: 0, title: p.label, rows: [{ color: fill, key: "rect", label: `মোটের ${label}`, value: format(p.value) }] });
                }}
                onPointerLeave={() => setTip(null)} onBlur={() => setTip(null)}>
                {fits && label}
                <span className="sr-only">{p.label}: {format(p.value)}</span>
              </div>
            );
          })}
        </div>
        <Tooltip tip={tip} width={width} />
      </div>
      <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-muted">
        {shown.map((p) => (
          <li key={p.key} className="flex items-center gap-1.5">
            <span className="size-2.5 rounded-[2px]" style={{ background: colorOf(p, parts.indexOf(p)) }} aria-hidden />{p.label}
          </li>
        ))}
      </ul>
    </div>
  );
}

// ---------- Heatmap (Bangladesh week × hour) ----------

const DAY_SHORT = ["শনি", "রবি", "সোম", "মঙ্গল", "বুধ", "বৃহঃ", "শুক্র"];
const DAY_FULL = ["শনিবার", "রবিবার", "সোমবার", "মঙ্গলবার", "বুধবার", "বৃহস্পতিবার", "শুক্রবার"];
const hourLabel = (h: number) => {
  const part = h < 5 ? "রাত" : h < 12 ? "সকাল" : h < 15 ? "দুপুর" : h < 18 ? "বিকেল" : h < 20 ? "সন্ধ্যা" : "রাত";
  return `${part} ${bn(h % 12 === 0 ? 12 : h % 12)}টা`;
};

export function Heatmap({ grid, unit = "বুকিং" }: { grid: number[][]; unit?: string }) {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [tip, setTip] = useState<Tip>(null);
  const max = Math.max(0, ...grid.flat());
  if (!max) return <Empty text="এই সময়ে কোনো অ্যাপয়েন্টমেন্ট নেই।" />;
  // Show only the hours that ever had demand, padded to whole business-day blocks.
  const used = grid[0].map((_, h) => grid.some((d) => d[h] > 0));
  const firstH = Math.max(0, Math.min(used.indexOf(true), 8));
  const lastH = Math.min(23, Math.max(used.lastIndexOf(true), 20));
  const hours = Array.from({ length: lastH - firstH + 1 }, (_, i) => firstH + i);
  const shade = (v: number) => (v === 0 ? VIZ.empty : VIZ.seq[Math.min(VIZ.seq.length - 1, 2 + Math.round((v / max) * (VIZ.seq.length - 3)))]);
  return (
    <div>
      <div ref={ref} className="relative overflow-x-auto">
        <div className="grid min-w-[480px] gap-[2px]" style={{ gridTemplateColumns: `44px repeat(${hours.length}, minmax(14px, 1fr))` }}>
          <span />
          {hours.map((h) => <span key={h} className="overflow-visible text-[10px] whitespace-nowrap text-subtle">{(h - firstH) % 3 === 0 ? hourLabel(h) : ""}</span>)}
          {grid.map((row, d) => (
            <div key={d} className="contents">
              <span className="pr-1 text-right text-xs leading-6 text-muted">{DAY_SHORT[d]}</span>
              {hours.map((h) => (
                <span key={h} tabIndex={row[h] ? 0 : -1} className="h-6 rounded-[3px] outline-none hover:ring-2 hover:ring-ink/30 focus-visible:ring-2 focus-visible:ring-primary/50"
                  style={{ background: shade(row[h]) }} aria-label={`${DAY_FULL[d]} ${hourLabel(h)}: ${bn(row[h])}টি ${unit}`}
                  onPointerEnter={(e) => {
                    const b = e.currentTarget.getBoundingClientRect(), host = ref.current!.getBoundingClientRect();
                    setTip({ x: b.left - host.left + b.width / 2, y: b.top - host.top, title: `${DAY_FULL[d]}, ${hourLabel(h)}`, rows: [{ label: unit, value: num(row[h]) }] });
                  }}
                  onPointerLeave={() => setTip(null)} />
              ))}
            </div>
          ))}
        </div>
        <Tooltip tip={tip} width={width} />
      </div>
      <div className="mt-3 flex items-center gap-2 text-xs text-subtle">
        <span>কম</span>
        <span className="h-2 w-28 rounded-full" style={{ background: `linear-gradient(90deg, ${VIZ.seq[2]}, ${VIZ.seq[VIZ.seq.length - 1]})` }} aria-hidden />
        <span>বেশি</span>
        <span className="ml-2">সর্বোচ্চ {bn(max)}টি {unit} এক ঘণ্টায়</span>
      </div>
    </div>
  );
}

export const heatmapTable = (grid: number[][]): TableSpec => ({
  columns: ["দিন", "মোট", "ব্যস্ততম সময়"],
  rows: grid.map((row, d) => {
    const total = row.reduce((a, b) => a + b, 0);
    const peak = row.indexOf(Math.max(...row));
    return [DAY_FULL[d], total, total ? hourLabel(peak) : "—"];
  }),
});

/** Table rows for a multi-series trend. */
export const trendTable = (data: TimeSeries, series: SeriesDef[], format: Fmt[] = []): TableSpec => ({
  columns: ["তারিখ", ...series.map((s) => s.label)],
  rows: data.points.map((p) => [dayLabel(p.date), ...series.map((s, i) => (format[i] ?? fmt.count)(p.values[data.keys.indexOf(s.key)]))]),
});

export const sliceTable = (items: Slice[], valueLabel: string, format: Fmt = fmt.count, secondary?: { label: string; format: Fmt }): TableSpec => ({
  columns: ["নাম", valueLabel, ...(secondary ? [secondary.label] : [])],
  rows: items.map((i) => [i.label, format(i.value), ...(secondary ? [i.secondary != null ? secondary.format(i.secondary) : "—"] : [])]),
});
