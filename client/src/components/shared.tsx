import { useEffect, useState, type CSSProperties } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { BadgeCheck } from "lucide-react";
import { api } from "../api/client";
import type { BusinessCard as Card, Category, Zone } from "../api/types";
import { bn, taka } from "../lib/format";
import { Stars } from "./ui";
import { CoverArt } from "./brand";

export function useCategories() {
  return useQuery({ queryKey: ["categories"], queryFn: () => api.get<Category[]>("/api/categories"), staleTime: 10 * 60_000 });
}

/** Flattened list "মেরামত › এসি মেরামত" for selects. */
export function flattenCategories(tree: Category[] = [], prefix = ""): { id: number; label: string }[] {
  return tree.flatMap((c) => [{ id: c.id, label: prefix + c.nameBn }, ...flattenCategories(c.children, `${prefix}${c.nameBn} › `)]);
}

export function useZones(parentId: number | null | undefined, enabled = true) {
  return useQuery({
    queryKey: ["zones", parentId ?? "root"],
    queryFn: () => api.get<Zone[]>("/api/zones", { parentId: parentId ?? undefined }),
    enabled,
    staleTime: 30 * 60_000,
  });
}

const LEVEL_LABELS = ["বিভাগ", "জেলা", "উপজেলা / থানা", "ইউনিয়ন / ওয়ার্ড"];

/**
 * Cascading picker: division → district → upazila/thana → union/ward. Reports the deepest
 * selected zone. Pass `initialPath` (e.g. "1.102.10202") to preselect.
 */
export function ZonePicker({ value, onChange, initialPath, minDepth = 1 }: {
  value: number | null; onChange: (zoneId: number | null) => void; initialPath?: string | null; minDepth?: number;
}) {
  const [chain, setChain] = useState<number[]>(() => (initialPath ? initialPath.split(".").map(Number) : value ? [value] : []));
  useEffect(() => {
    if (initialPath) setChain(initialPath.split(".").map(Number));
  }, [initialPath]);

  const levels = [null, ...chain].slice(0, 4);
  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
      {levels.map((parent, i) => (
        <ZoneLevelSelect key={`${i}-${parent}`} parentId={parent} label={LEVEL_LABELS[i] ?? "এলাকা"} value={chain[i] ?? null}
          required={i < minDepth}
          onChange={(id) => {
            const next = id === null ? chain.slice(0, i) : [...chain.slice(0, i), id];
            setChain(next);
            onChange(next.at(-1) ?? null);
          }} />
      ))}
    </div>
  );
}

function ZoneLevelSelect({ parentId, label, value, onChange, required }: {
  parentId: number | null; label: string; value: number | null; onChange: (id: number | null) => void; required: boolean;
}) {
  const { data = [] } = useZones(parentId);
  if (parentId !== null && data.length === 0) return null;
  return (
    <label className="label">
      <span>{label} {!required && <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>}</span>
      <select className="input" value={value ?? ""} onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}>
        <option value="">নির্বাচন করুন</option>
        {data.map((z) => <option key={z.id} value={z.id}>{z.nameBn}</option>)}
      </select>
    </label>
  );
}

const CARD_SHADOWS = ["var(--color-primary)", "var(--color-paint-red)", "var(--color-leaf)"];

/** Signboard-style business card: photo on top, ink outline, hard shadow cycling through the paint colours. */
export function BusinessCardView({ b, index }: { b: Card; index?: number }) {
  const shadow = CARD_SHADOWS[(index ?? 0) % CARD_SHADOWS.length];
  return (
    <article className="signboard group" style={{ "--signboard-shadow": shadow } as CSSProperties}>
      <Link to={`/b/${b.slug}`} className="flex flex-1 flex-col">
        <div className="relative aspect-[16/9] border-b-[2.5px] border-ink">
          {b.coverUrl ? <img src={b.coverUrl} alt="" className="size-full object-cover" loading="lazy" /> : <CoverArt categoryId={b.categoryId} className="size-full" />}
          {b.isVerified && (
            <span className="absolute top-3 left-3 inline-flex items-center gap-1 rounded-full bg-leaf px-2.5 py-0.5 text-xs font-semibold text-white">
              <BadgeCheck className="size-3.5" aria-hidden />যাচাইকৃত
            </span>
          )}
        </div>
        <div className="flex flex-1 flex-col gap-0.5 px-5 pt-4 pb-3">
          <div className="flex items-center gap-2.5">
            {b.logoUrl && <img src={b.logoUrl} alt="" className="size-9 rounded-full border-2 border-ink object-cover" />}
            <h3 className="min-w-0 truncate font-display text-[22px] leading-tight font-bold text-primary group-hover:text-paint-red">{b.name}</h3>
          </div>
          <span className="text-[15px] text-muted">{b.categoryName}{b.zoneName && ` · ${b.zoneName}`}</span>
          <div className="mt-1 flex flex-wrap gap-1.5">
            {b.offersHomeService && <span className="rounded-full bg-sand-deep px-2.5 py-0.5 text-xs font-medium text-ink">হোম সার্ভিস</span>}
            {b.offersEmergencyService && <span className="rounded-full bg-sand-deep px-2.5 py-0.5 text-xs font-medium text-ink">জরুরি সেবা</span>}
          </div>
        </div>
      </Link>
      <div className="flex items-center justify-between gap-2 px-5 pb-4 font-semibold">
        <span className="text-[15px]">
          {b.reviewCount > 0 ? <><Stars value={b.avgRating} /> <span className="font-normal text-subtle">({bn(b.reviewCount)})</span></> : <span className="font-normal text-subtle">নতুন</span>}
        </span>
        {b.startingPrice != null
          ? <span className="text-[15px] text-paint-red">{taka(b.startingPrice)} থেকে</span>
          : <span className="text-sm font-normal text-subtle">মূল্য জানতে যোগাযোগ</span>}
      </div>
    </article>
  );
}

/** Photos a customer attached to a review; each opens full size in a new tab. */
export function ReviewPhotos({ photos }: { photos: string[] }) {
  return (
    <ul className="flex flex-wrap gap-2" aria-label="রিভিউয়ের ছবি">
      {photos.map((url, i) => (
        <li key={url}><a href={url} target="_blank" rel="noopener" aria-label={`ছবি ${bn(i + 1)} বড় করে দেখুন`}>
          <img src={url} alt="" loading="lazy" className="size-20 rounded-md object-cover hover:opacity-90" />
        </a></li>
      ))}
    </ul>
  );
}
