import { useEffect, useState } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { BadgeCheck, MapPin } from "lucide-react";
import { api } from "../api/client";
import type { BusinessCard as Card, Category, Zone } from "../api/types";
import { bn, taka } from "../lib/format";
import { Avatar, Stars } from "./ui";
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

export function BusinessCardView({ b }: { b: Card }) {
  return (
    <article className="card card-hover group flex flex-col overflow-hidden">
      <Link to={`/b/${b.slug}`} className="flex flex-1 flex-col">
        <div className="relative aspect-[16/8] border-b border-line">
          {b.coverUrl ? <img src={b.coverUrl} alt="" className="size-full object-cover transition duration-500 group-hover:scale-[1.03]" loading="lazy" /> : <CoverArt categoryId={b.categoryId} className="size-full" />}
          {b.isVerified && <span className="sticker absolute top-3 right-3 border-2 px-2.5 py-0.5 text-xs text-brand-green" style={{ rotate: "3deg" }}><BadgeCheck className="size-3.5" aria-hidden />যাচাইকৃত</span>}
          <span className="absolute -bottom-6 left-4 rounded-lg border-[3px] border-white bg-white shadow-card">
            {b.logoUrl ? <img src={b.logoUrl} alt="" className="size-12 rounded-md object-cover" /> : <Avatar name={b.name} className="size-12 text-sm" />}
          </span>
        </div>
        <div className="flex flex-1 flex-col gap-1 px-4 pt-9 pb-3">
          <div className="flex items-center gap-1.5">
            <h3 className="truncate font-display text-[17px] font-semibold group-hover:text-primary">{b.name}</h3>
            {b.isVerified && <BadgeCheck className="size-4 shrink-0 text-brand-green" aria-label="যাচাইকৃত" />}
          </div>
          <span className="flex items-center gap-1 text-sm text-muted"><MapPin className="size-3.5" aria-hidden />{b.categoryName}{b.zoneName && ` · ${b.zoneName}`}</span>
          <span className="text-sm">
            {b.reviewCount > 0 ? <><Stars value={b.avgRating} /> <span className="text-subtle">({bn(b.reviewCount)} রিভিউ)</span></> : <span className="text-subtle">নতুন</span>}
          </span>
          <div className="mt-1 flex flex-wrap gap-1.5">
            {b.offersHomeService && <span className="rounded-md border border-line px-2 py-0.5 text-xs text-muted">হোম সার্ভিস</span>}
            {b.offersEmergencyService && <span className="rounded-md border border-line px-2 py-0.5 text-xs text-muted">জরুরি সেবা</span>}
          </div>
        </div>
      </Link>
      <div className="flex items-center justify-between gap-2 border-t border-line px-4 py-3">
        <span className="text-sm text-muted">{b.startingPrice != null ? <>শুরু <strong className="font-semibold text-ink">{taka(b.startingPrice)}</strong></> : "মূল্য জানতে যোগাযোগ"}</span>
        <Link to={`/b/${b.slug}`} className="inline-flex min-h-10 items-center rounded-md bg-primary px-4 text-sm font-semibold text-white hover:bg-primary-hover">বুক করুন</Link>
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
