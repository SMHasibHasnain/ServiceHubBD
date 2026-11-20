import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { SearchX, SlidersHorizontal } from "lucide-react";
import { api } from "../../api/client";
import type { BusinessCard, Paged } from "../../api/types";
import { BusinessCardView, flattenCategories, useCategories, ZonePicker } from "../../components/shared";
import { Button, EmptyState, ErrorBox, Skeleton } from "../../components/ui";
import { bn } from "../../lib/format";

const SORTS = [
  { value: "recommended", label: "প্রস্তাবিত" },
  { value: "highest_rated", label: "সর্বোচ্চ রেটিং" },
  { value: "most_reviewed", label: "সবচেয়ে বেশি রিভিউ" },
  { value: "newest", label: "নতুন" },
];
const PAGE_SIZE = 12;

export default function Search() {
  const [params, setParams] = useSearchParams();
  const [q, setQ] = useState(params.get("q") ?? "");
  const [showFilters, setShowFilters] = useState(false);
  const categories = useCategories();

  const query = {
    q: params.get("q") ?? undefined,
    categoryId: params.get("categoryId") ?? undefined,
    zoneId: params.get("zoneId") ?? undefined,
    homeService: params.get("homeService") ?? undefined,
    emergencyService: params.get("emergencyService") ?? undefined,
    minRating: params.get("minRating") ?? undefined,
    sort: params.get("sort") ?? "recommended",
    page: Number(params.get("page") ?? 1),
    pageSize: PAGE_SIZE,
  };
  const results = useQuery({
    queryKey: ["public", "search", query],
    queryFn: () => api.get<Paged<BusinessCard>>("/api/public/businesses", query),
    placeholderData: keepPreviousData,
  });

  const update = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(patch)) (v ? next.set(k, v) : next.delete(k));
    if (!("page" in patch)) next.delete("page");
    setParams(next);
  };
  const toggle = (k: string) => update({ [k]: params.get(k) ? null : "true" });
  const submit = (e: FormEvent) => { e.preventDefault(); update({ q: q.trim() || null }); };

  const totalPages = results.data ? Math.max(1, Math.ceil(results.data.total / PAGE_SIZE)) : 1;
  const chip = (active: boolean) =>
    `min-h-10 rounded-full border px-4 text-[15px] font-medium ${active ? "border-zinc-900 bg-zinc-900 text-white" : "border-zinc-300 bg-surface text-zinc-700 hover:border-zinc-400"}`;

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6">
      <form role="search" onSubmit={submit} className="mb-5 flex gap-2">
        <label className="flex-1">
          <span className="sr-only">অনুসন্ধান</span>
          <input className="input" value={q} onChange={(e) => setQ(e.target.value)} placeholder="সেবা, ব্যবসা বা এলাকা খুঁজুন" />
        </label>
        <Button type="submit" size="lg">খুঁজুন</Button>
      </form>

      <div className="mb-5 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold tracking-tight">{query.q ? `“${query.q}”` : "সব সেবা"}</h1>
          <p className="text-muted" aria-live="polite">{results.data ? `${bn(results.data.total)}টি ব্যবসা পাওয়া গেছে` : "খোঁজা হচ্ছে..."}</p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" className="lg:hidden" onClick={() => setShowFilters((s) => !s)} aria-expanded={showFilters}>
            <SlidersHorizontal className="size-4" aria-hidden />ফিল্টার
          </Button>
          <label className="flex items-center gap-2 text-zinc-700">
            <span className="hidden sm:inline">সাজান</span>
            <select className="input min-h-11 w-auto" value={query.sort} onChange={(e) => update({ sort: e.target.value })}>
              {SORTS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </label>
        </div>
      </div>

      <div className="mb-6 flex flex-wrap gap-2" role="group" aria-label="দ্রুত ফিল্টার">
        <button className={chip(!!query.homeService)} aria-pressed={!!query.homeService} onClick={() => toggle("homeService")}>হোম সার্ভিস</button>
        <button className={chip(!!query.emergencyService)} aria-pressed={!!query.emergencyService} onClick={() => toggle("emergencyService")}>জরুরি সেবা</button>
        <button className={chip(query.minRating === "4.5")} aria-pressed={query.minRating === "4.5"} onClick={() => update({ minRating: query.minRating === "4.5" ? null : "4.5" })}>শীর্ষ রেটিং (৪.৫+)</button>
      </div>

      <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
        <aside aria-label="ফিল্টার" className={`card flex-col gap-5 p-5 lg:sticky lg:top-24 lg:flex lg:w-72 lg:shrink-0 ${showFilters ? "flex" : "hidden"}`}>
          <div className="flex items-center justify-between">
            <span className="text-lg font-semibold">ফিল্টার</span>
            <button className="min-h-11 font-semibold text-primary hover:text-primary-hover" onClick={() => setParams(query.q ? { q: query.q } : {})}>মুছুন</button>
          </div>
          <label className="label">ক্যাটাগরি
            <select className="input" value={query.categoryId ?? ""} onChange={(e) => update({ categoryId: e.target.value || null })}>
              <option value="">সব ক্যাটাগরি</option>
              {flattenCategories(categories.data).map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}
            </select>
          </label>
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1.5 font-medium">এলাকা</legend>
            <ZonePicker value={query.zoneId ? Number(query.zoneId) : null} minDepth={0} onChange={(id) => update({ zoneId: id ? String(id) : null })} />
          </fieldset>
        </aside>

        <section className="min-w-0 flex-1" aria-busy={results.isFetching}>
          <ErrorBox error={results.error} />
          {results.isLoading ? (
            <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">{Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-80" />)}</div>
          ) : results.data?.items.length ? (
            <>
              <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">{results.data.items.map((b, i) => <BusinessCardView key={b.id} b={b} index={i} />)}</div>
              {totalPages > 1 && (
                <nav aria-label="পৃষ্ঠা" className="mt-8 flex items-center justify-center gap-3">
                  <Button variant="outline" disabled={query.page <= 1} onClick={() => update({ page: String(query.page - 1) })}>← আগের</Button>
                  <span className="text-muted">{bn(query.page)} / {bn(totalPages)}</span>
                  <Button variant="outline" disabled={query.page >= totalPages} onClick={() => update({ page: String(query.page + 1) })}>পরের →</Button>
                </nav>
              )}
            </>
          ) : (
            <EmptyState icon={<SearchX className="size-8" aria-hidden />} title="আপনার অনুসন্ধানের সাথে মিল পাওয়া যায়নি।"
              description="অন্য কোনো শব্দ দিয়ে চেষ্টা করুন অথবা ফিল্টার মুছে দিন।"
              action={<Button variant="outline" onClick={() => setParams({})}>ফিল্টার মুছুন</Button>} />
          )}
        </section>
      </div>
    </div>
  );
}
