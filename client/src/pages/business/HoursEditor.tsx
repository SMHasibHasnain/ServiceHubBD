import type { Hour } from "../../api/types";
import { DAY_NAMES } from "../../lib/format";

export type DayHours = { open: boolean; opensAt: string; closesAt: string };

export const defaultWeek = (): DayHours[] =>
  DAY_NAMES.map((_, d) => ({ open: d !== 6, opensAt: "09:00", closesAt: "20:00" })); // Friday closed

export const weekFromHours = (hours: Hour[]): DayHours[] =>
  DAY_NAMES.map((_, d) => {
    const h = hours.find((x) => x.dayOfWeek === d);
    return h ? { open: true, opensAt: h.opensAt.slice(0, 5), closesAt: h.closesAt.slice(0, 5) } : { open: false, opensAt: "09:00", closesAt: "20:00" };
  });

export const hoursFromWeek = (week: DayHours[]): Hour[] =>
  week.flatMap((d, i) => (d.open ? [{ dayOfWeek: i, opensAt: `${d.opensAt}:00`, closesAt: `${d.closesAt}:00` }] : []));

/** One opening interval per day (the API also supports split shifts). */
export function HoursEditor({ week, onChange }: { week: DayHours[]; onChange: (w: DayHours[]) => void }) {
  const set = (i: number, patch: Partial<DayHours>) => onChange(week.map((d, j) => (j === i ? { ...d, ...patch } : d)));
  return (
    <div className="flex flex-col divide-y divide-zinc-100 rounded-md border border-line">
      {week.map((d, i) => (
        <div key={DAY_NAMES[i]} className="flex flex-wrap items-center gap-3 px-4 py-2.5">
          <label className="flex min-h-11 w-40 items-center gap-2.5 font-medium">
            <input type="checkbox" className="size-5 accent-primary" checked={d.open} onChange={(e) => set(i, { open: e.target.checked })} />
            {DAY_NAMES[i]}
          </label>
          {d.open ? (
            <div className="flex items-center gap-2">
              <input type="time" className="input min-h-11 w-32" value={d.opensAt} onChange={(e) => set(i, { opensAt: e.target.value })} aria-label={`${DAY_NAMES[i]} খোলার সময়`} />
              <span className="text-subtle">থেকে</span>
              <input type="time" className="input min-h-11 w-32" value={d.closesAt} onChange={(e) => set(i, { closesAt: e.target.value })} aria-label={`${DAY_NAMES[i]} বন্ধের সময়`} />
            </div>
          ) : <span className="text-subtle">বন্ধ</span>}
        </div>
      ))}
    </div>
  );
}
