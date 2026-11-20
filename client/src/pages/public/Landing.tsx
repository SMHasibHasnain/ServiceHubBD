import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Link, useLocation, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { BadgeCheck, Pause, Play, Search } from "lucide-react";
import { api } from "../../api/client";
import type { BusinessCard, Category, Paged, Zone } from "../../api/types";
import { Awning, Band, CategoryTile, CoverArt, Fringe, PetalPattern, Rosette, initialFont, initialOf, paintFor } from "../../components/brand";
import { BusinessCardView, useCategories, useZones } from "../../components/shared";
import { Skeleton, Stars } from "../../components/ui";
import { bn, taka } from "../../lib/format";
import { group, list, mergeHome, safeHref, str, useHomeContent, type Data, type HomeContent, type Section } from "../../lib/homeContent";

/** Built-in defaults, used if the saved content can't be loaded. */
const FALLBACK = mergeHome(undefined);

export default function Landing() {
  const { hash } = useLocation();
  const home = useHomeContent();
  // "/#business" from the topbar: scroll once the section exists.
  useEffect(() => {
    if (hash && home.data) document.getElementById(hash.slice(1))?.scrollIntoView({ behavior: "smooth" });
  }, [hash, home.data]);

  if (home.data) return <HomeView content={home.data} />;
  // While the saved copy loads, show the hero's colour instead of flashing the default text.
  return home.isError ? <HomeView content={FALLBACK} /> : <div className="min-h-[70vh] bg-primary" aria-busy="true" />;
}

type Ctx = {
  categories: Category[]; categoriesLoading: boolean; divisions: Zone[];
  featured: BusinessCard[]; featuredLoading: boolean; showcase: BusinessCard[];
};

/** The homepage for a content document. Also the live preview in the admin editor. */
export function HomeView({ content }: { content: HomeContent }) {
  const categories = useCategories();
  const divisions = useZones(null);
  const featured = useQuery({
    queryKey: ["public", "featured"],
    queryFn: () => api.get<Paged<BusinessCard>>("/api/public/businesses", { sort: "highest_rated", pageSize: 6 }),
  });
  const items = featured.data?.items ?? [];
  const verified = items.filter((b) => b.isVerified);
  const visible = content.sections.filter((s) => s.visible);
  const ctx: Ctx = {
    categories: categories.data ?? [], categoriesLoading: categories.isLoading,
    divisions: divisions.data ?? [], featured: items, featuredLoading: featured.isLoading,
    showcase: verified.length ? verified : items,
  };

  return (
    <div className="bg-cream">
      <Band />
      {visible.map((s) => <SectionView key={s.id} section={s} ctx={ctx} />)}
      {/* The business section ends on its own fringe; otherwise close with the band before the footer. */}
      {visible.at(-1)?.type !== "business" && <Band />}
    </div>
  );
}

function SectionView({ section: s, ctx }: { section: Section; ctx: Ctx }) {
  const d = s.data;
  switch (s.type) {
    case "hero": return <Hero d={d} categories={ctx.categories} />;
    case "verified": return ctx.showcase.length ? <ShopShowcase d={d} businesses={ctx.showcase} /> : null;
    case "categories": return <Categories d={d} categories={ctx.categories} loading={ctx.categoriesLoading} />;
    case "steps": return <Steps d={d} />;
    case "featured": return <Featured d={d} items={ctx.featured} loading={ctx.featuredLoading} />;
    case "seasonal": return <Seasonal d={d} />;
    case "testimonials": return list(d, "items").length ? <Testimonials d={d} /> : null;
    case "areas": return ctx.divisions.length ? <Areas d={d} divisions={ctx.divisions} /> : null;
    case "faq": return list(d, "items").length ? <Faq d={d} /> : null;
    case "business": return <Business d={d} />;
    case "text": return <TextSection d={d} />;
    case "cards": return <CardsSection d={d} />;
    case "banner": return <BannerSection d={d} />;
  }
}

// ---------- Shared bits ----------

/** Text the admin may have cleared: renders nothing when blank. */
function Opt({ v, children }: { v: string; children: (v: string) => ReactNode }) {
  return v.trim() ? <>{children(v)}</> : null;
}

function SectionTitle({ eyebrow, title, light = false }: { eyebrow?: string; title: string; light?: boolean }) {
  return (
    <div className="mb-10 flex flex-col items-center gap-1 text-center">
      {eyebrow?.trim() && <span className={`font-painted text-2xl ${light ? "text-marigold" : "text-paint-red"}`}>{eyebrow}</span>}
      {title.trim() && <h2 className={`text-[32px] leading-tight font-semibold text-balance sm:text-[40px] ${light ? "text-cream" : "text-primary"}`}>{title}</h2>}
    </div>
  );
}

/** Admin-entered link: same-site paths use the router, https opens a new tab, anything else isn't rendered. */
function SmartLink({ to, className, children }: { to: string; className: string; children: ReactNode }) {
  const h = safeHref(to);
  if (!h) return null;
  return h.external
    ? <a href={h.href} target="_blank" rel="noopener noreferrer" className={className}>{children}</a>
    : <Link to={h.href} className={className}>{children}</Link>;
}

const TONE: Record<string, { bg: string; title: string; eyebrow: string; body: string; light: boolean; fringe?: "navy" | "soft" }> = {
  cream: { bg: "bg-cream", title: "text-primary", eyebrow: "text-paint-red", body: "text-muted", light: false },
  sand: { bg: "bg-sand-deep border-y-[1.5px] border-sand", title: "text-primary", eyebrow: "text-paint-red", body: "text-muted", light: false },
  navy: { bg: "bg-primary text-cream", title: "text-cream", eyebrow: "text-marigold", body: "text-[#cfd8ea]", light: true, fringe: "navy" },
  marigold: { bg: "bg-marigold-soft", title: "text-ink", eyebrow: "text-paint-red", body: "text-[#3a3426]", light: false, fringe: "soft" },
};
const tone = (d: Data) => TONE[str(d, "tone")] ?? TONE.cream;

// ---------- Built-in sections ----------

function Hero({ d, categories }: { d: Data; categories: Category[] }) {
  const navigate = useNavigate();
  const [q, setQ] = useState("");
  const submit = (e: FormEvent) => {
    e.preventDefault();
    navigate(`/search${q.trim() ? `?q=${encodeURIComponent(q.trim())}` : ""}`);
  };
  return (
    <>
      <section className="relative overflow-hidden bg-primary text-cream">
        <PetalPattern />
        <div className="relative mx-auto flex max-w-4xl flex-col items-center gap-7 px-4 pt-20 pb-20 text-center sm:px-6 md:pt-24 md:pb-24">
          <Opt v={str(d, "eyebrow")}>{(v) => (
            <p className="flex items-center gap-3 text-[15px] font-semibold text-marigold"><Rosette className="size-5" />{v}<Rosette className="size-5" /></p>
          )}</Opt>
          <h1 className="text-[44px] leading-[1.06] font-extrabold tracking-[-0.01em] sm:text-6xl lg:text-[84px]">
            {str(d, "titleLine1")}
            {str(d, "titleLine2").trim() && <><br /><span className="painted mt-3 inline-block text-[1.18em] text-marigold">{str(d, "titleLine2")}</span></>}
          </h1>
          <Opt v={str(d, "subtitle")}>{(v) => <p className="max-w-2xl text-lg text-pretty whitespace-pre-line text-[#cfd8ea] sm:text-xl">{v}</p>}</Opt>

          <form role="search" onSubmit={submit}
            className="flex w-full max-w-2xl flex-wrap gap-1.5 rounded-full bg-cream p-1.5 text-left shadow-[0_0_0_3px_var(--color-marigold)]">
            <label className="flex min-h-14 min-w-0 flex-[1_1_220px] items-center gap-3 px-4">
              <Search className="size-5 shrink-0 text-primary" aria-hidden />
              <span className="sr-only">কী সেবা খুঁজছেন</span>
              <input value={q} onChange={(e) => setQ(e.target.value)} placeholder={str(d, "searchPlaceholder")}
                className="w-full min-w-0 bg-transparent text-lg text-ink outline-none placeholder:text-[#8a8170]" />
            </label>
            <button className="min-h-14 flex-[1_0_auto] rounded-full bg-paint-red px-9 font-display text-xl font-semibold text-cream transition-colors duration-150 hover:bg-paint-red-hover sm:flex-none">{str(d, "searchButton") || "খুঁজুন"}</button>
          </form>

          {categories.length > 0 && (
            <p className="text-[15px] text-[#cfd8ea]">
              {str(d, "popularLabel")}{" "}
              {categories.slice(0, 5).map((c, i) => (
                <span key={c.id}>
                  {i > 0 && " · "}
                  <Link to={`/search?categoryId=${c.id}`} className="text-cream underline decoration-marigold/50 underline-offset-4 transition-colors duration-150 hover:text-marigold">{c.nameBn}</Link>
                </span>
              ))}
            </p>
          )}
        </div>
      </section>
      <Fringe tone="navy" />
    </>
  );
}

function Categories({ d, categories, loading }: { d: Data; categories: Category[]; loading: boolean }) {
  return (
    <section className="mx-auto max-w-6xl px-4 pt-20 pb-24 sm:px-6">
      <SectionTitle eyebrow={str(d, "eyebrow")} title={str(d, "title")} />
      <div className="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-4">
        {loading && Array.from({ length: 8 }, (_, i) => <Skeleton key={i} className="h-[92px] rounded-lg" />)}
        {categories.map((c) => (
          <Link key={c.id} to={`/search?categoryId=${c.id}`}
            className="flex flex-col items-start gap-3 rounded-lg border-[1.5px] border-sand bg-paper p-4 transition-colors duration-150 hover:border-ink sm:flex-row sm:items-center sm:gap-4">
            <CategoryTile id={c.id} name={c.nameBn} className="size-12 text-2xl sm:size-[60px] sm:text-[30px]" />
            <span className="min-w-0">
              <span className="block font-display text-lg leading-snug font-semibold [overflow-wrap:anywhere]">{c.nameBn}</span>
              <span className="line-clamp-1 text-sm text-muted">
                {c.children.length ? c.children.slice(0, 3).map((x) => x.nameBn).join(" · ") : c.nameEn}
              </span>
            </span>
          </Link>
        ))}
      </div>
    </section>
  );
}

function Steps({ d }: { d: Data }) {
  const items = list(d, "items");
  if (!items.length) return null;
  const cols = items.length >= 4 ? "md:grid-cols-4" : items.length === 3 ? "md:grid-cols-3" : "md:grid-cols-2";
  return (
    <section className="border-y-[1.5px] border-sand bg-paper">
      <div className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
        <SectionTitle eyebrow={str(d, "eyebrow")} title={str(d, "title")} />
        <ol className={`relative grid gap-10 md:gap-8 ${cols}`}>
          <span aria-hidden className="absolute top-8 right-[14%] left-[14%] hidden border-t-2 border-dashed border-sand md:block" />
          {items.map((s, i) => {
            const p = paintFor(i);
            return (
              <li key={i} className="relative flex flex-col items-center gap-3 text-center">
                <span aria-hidden className="flex size-16 items-center justify-center rounded-full font-painted text-[34px] leading-none"
                  style={{ background: p.bg, color: p.fg, boxShadow: `inset 0 0 0 4px var(--color-paper), 0 0 0 2px ${p.bg}` }}>
                  <span className="translate-y-[0.06em]">{bn(i + 1)}</span>
                </span>
                <span className="font-display text-2xl font-semibold text-primary">{str(s, "title")}</span>
                <span className="max-w-xs whitespace-pre-line text-muted">{str(s, "body")}</span>
              </li>
            );
          })}
        </ol>
      </div>
    </section>
  );
}

function Featured({ d, items, loading }: { d: Data; items: BusinessCard[]; loading: boolean }) {
  return (
    <section className="mx-auto max-w-6xl px-4 pt-20 pb-24 sm:px-6">
      <div className="mb-10 flex flex-wrap items-end justify-between gap-3">
        <div>
          <Opt v={str(d, "eyebrow")}>{(v) => <span className="block font-painted text-2xl text-paint-red">{v}</span>}</Opt>
          <h2 className="text-[32px] leading-tight font-semibold text-primary sm:text-[40px]">{str(d, "title")}</h2>
        </div>
        <Opt v={str(d, "linkLabel")}>{(v) => (
          <Link to="/search?sort=highest_rated" className="font-semibold text-primary underline decoration-paint-red decoration-2 underline-offset-4 transition-colors duration-150 hover:text-paint-red">{v}</Link>
        )}</Opt>
      </div>
      {loading ? (
        <div className="grid gap-7 sm:grid-cols-2 lg:grid-cols-3">{Array.from({ length: 3 }, (_, i) => <Skeleton key={i} className="h-80" />)}</div>
      ) : items.length ? (
        <div className="grid gap-7 sm:grid-cols-2 lg:grid-cols-3">{items.map((b) => <BusinessCardView key={b.id} b={b} />)}</div>
      ) : (
        <div className="signboard items-center gap-3 p-10 text-center">
          <Rosette className="size-10" />
          <p className="whitespace-pre-line text-muted">{str(d, "emptyText")}</p>
          <Opt v={str(d, "emptyButton")}>{(v) => <Link to="/business/new" className="inline-flex min-h-11 items-center rounded-full bg-primary px-6 font-semibold text-cream transition-colors duration-150 hover:bg-primary-hover">{v}</Link>}</Opt>
        </div>
      )}
    </section>
  );
}

/** Winter and wedding season (Nov–Feb), summer (Mar–Jun) or monsoon (Jul–Oct). */
function currentSeason(month = new Date().getMonth()) {
  return month >= 2 && month <= 5 ? "summer" : month >= 6 && month <= 9 ? "monsoon" : "winter";
}

function Seasonal({ d }: { d: Data }) {
  const now = group(d, currentSeason());
  const picks = list(now, "picks");
  if (!picks.length) return null;
  return (
    <section className="border-y-[1.5px] border-sand bg-sand-deep">
      <div className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
        <div className="mb-10 flex flex-wrap items-end justify-between gap-3">
          <div>
            <Opt v={str(d, "eyebrow")}>{(v) => <span className="block font-painted text-2xl text-paint-red">{v}</span>}</Opt>
            <h2 className="text-[32px] leading-tight font-semibold text-primary sm:text-[40px]">{str(now, "title")}</h2>
          </div>
          <span className="text-[15px] text-muted">{str(now, "note")}</span>
        </div>
        <div className="grid gap-6 md:grid-cols-3">
          {picks.map((p, i) => {
            const paint = paintFor(i + 1);
            const q = str(p, "query").trim() || str(p, "title");
            return (
              <Link key={i} to={`/search?q=${encodeURIComponent(q)}`}
                className="group flex flex-col gap-3 rounded-lg border-[1.5px] border-sand bg-paper p-6 transition-colors duration-150 hover:border-ink">
                <span aria-hidden className="h-1.5 w-14 rounded-full" style={{ background: paint.bg }} />
                <span className="font-display text-xl font-semibold text-primary transition-colors duration-150 group-hover:text-paint-red">{str(p, "title")}</span>
                <span className="whitespace-pre-line text-muted">{str(p, "body")}</span>
                <Opt v={str(d, "linkLabel")}>{(v) => <span className="mt-auto pt-2 text-[15px] font-semibold text-paint-red">{v}</span>}</Opt>
              </Link>
            );
          })}
        </div>
      </div>
    </section>
  );
}

function Testimonials({ d }: { d: Data }) {
  return (
    <section className="mx-auto max-w-6xl px-4 pt-20 pb-24 sm:px-6">
      <SectionTitle eyebrow={str(d, "eyebrow")} title={str(d, "title")} />
      <ul className="columns-1 gap-6 md:columns-2 lg:columns-3">
        {list(d, "items").map((t, i) => {
          const p = paintFor(i);
          return (
            <li key={i} className="mb-6 break-inside-avoid">
              <figure className="flex flex-col gap-4 rounded-lg border-[1.5px] border-sand bg-paper p-6">
                <span aria-hidden className="h-6 font-painted text-6xl leading-none" style={{ color: p.bg }}>“</span>
                <blockquote className="text-[17px] leading-relaxed whitespace-pre-line text-ink">{str(t, "quote")}</blockquote>
                <figcaption className="flex items-center gap-3 border-t border-sand pt-4">
                  <span aria-hidden className={`flex size-11 shrink-0 items-center justify-center rounded-full text-xl leading-none ${initialFont(initialOf(str(t, "name")))}`} style={{ background: p.bg, color: p.fg }}>
                    <span className="translate-y-[0.06em]">{initialOf(str(t, "name"))}</span>
                  </span>
                  <span className="min-w-0 text-sm">
                    <span className="block text-[15px] font-semibold text-ink">{str(t, "name")}</span>
                    <span className="block text-muted">{[str(t, "area"), str(t, "service")].filter((x) => x.trim()).join(" · ")}</span>
                  </span>
                </figcaption>
              </figure>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function Areas({ d, divisions }: { d: Data; divisions: Zone[] }) {
  return (
    <>
      <section className="relative overflow-hidden bg-primary text-cream">
        <PetalPattern />
        <div className="relative mx-auto max-w-6xl px-4 py-20 text-center sm:px-6">
          <Opt v={str(d, "eyebrow")}>{(v) => <span className="block font-painted text-2xl text-marigold">{v}</span>}</Opt>
          <h2 className="text-[32px] leading-tight font-semibold sm:text-[40px]">{str(d, "title")}</h2>
          <ul className="mx-auto mt-10 flex max-w-4xl flex-wrap justify-center gap-3">
            {divisions.map((z) => (
              <li key={z.id}>
                <Link to={`/search?zoneId=${z.id}`}
                  className="inline-flex min-h-12 items-center gap-2 rounded-full border-[1.5px] border-cream/30 px-5 text-[17px] font-medium text-cream transition-colors duration-150 hover:border-marigold hover:bg-marigold hover:text-ink">
                  {z.nameBn}
                </Link>
              </li>
            ))}
          </ul>
        </div>
      </section>
      <Fringe tone="navy" />
    </>
  );
}

function Faq({ d }: { d: Data }) {
  return (
    <section className="mx-auto max-w-3xl px-4 pt-20 pb-24 sm:px-6">
      <SectionTitle eyebrow={str(d, "eyebrow")} title={str(d, "title")} />
      <div className="flex flex-col gap-3">
        {list(d, "items").map((f, i) => (
          <details key={i} className="group rounded-lg border-[1.5px] border-sand bg-paper open:border-ink">
            <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-4 px-5 py-3 font-display text-lg font-semibold text-primary [&::-webkit-details-marker]:hidden">
              {str(f, "q")}
              <Rosette color="#c4373f" className="size-5 shrink-0 transition-transform duration-150 group-open:rotate-45" />
            </summary>
            <p className="px-5 pb-5 whitespace-pre-line text-muted">{str(f, "a")}</p>
          </details>
        ))}
      </div>
    </section>
  );
}

function Business({ d }: { d: Data }) {
  const points = list(d, "points");
  return (
    <>
      <section id="business" className="relative scroll-mt-20 overflow-hidden border-t-[1.5px] border-sand bg-marigold-soft">
        <PetalPattern className="opacity-[0.5]" color="#fbf3e2" />
        <div className={`relative mx-auto grid max-w-6xl items-center gap-14 px-4 py-20 sm:px-6 md:py-24 ${points.length ? "lg:grid-cols-2" : ""}`}>
          <div className="flex min-w-0 flex-col items-start gap-6">
            <h2 className="text-[36px] leading-[1.1] font-semibold text-ink sm:text-5xl">
              {str(d, "titleLine1")}
              {str(d, "titleLine2").trim() && <><br /><span className="painted-soft text-[1.12em] text-paint-red">{str(d, "titleLine2")}</span></>}
            </h2>
            <Opt v={str(d, "body")}>{(v) => <p className="max-w-md whitespace-pre-line text-[#3a3426]">{v}</p>}</Opt>
            <div className="flex flex-wrap gap-3">
              <Opt v={str(d, "primaryButton")}>{(v) => <Link to="/business/new" className="inline-flex min-h-12 items-center rounded-full bg-primary px-7 text-lg font-semibold text-cream transition-colors duration-150 hover:bg-primary-hover">{v}</Link>}</Opt>
              <Opt v={str(d, "secondaryButton")}>{(v) => <Link to="/pricing" className="inline-flex min-h-12 items-center rounded-full border-2 border-ink px-6 font-semibold text-ink transition-colors duration-150 hover:bg-ink hover:text-cream">{v}</Link>}</Opt>
            </div>
          </div>
          {points.length > 0 && (
            <ol className="flex min-w-0 flex-col gap-6 rounded-xl bg-paper p-6 shadow-[0_0_0_2px_var(--color-ink)] sm:p-8">
              {points.map((p, i) => {
                const paint = paintFor(i);
                return (
                  <li key={i} className="flex items-start gap-4">
                    <span aria-hidden className="flex size-12 shrink-0 items-center justify-center rounded-full font-painted text-[26px] leading-none" style={{ background: paint.bg, color: paint.fg }}>
                      <span className="translate-y-[0.06em]">{bn(i + 1)}</span>
                    </span>
                    <span><b className="block font-display text-xl font-semibold">{str(p, "title")}</b><span className="whitespace-pre-line">{str(p, "body")}</span></span>
                  </li>
                );
              })}
            </ol>
          )}
        </div>
      </section>
      <Fringe tone="soft" className="bg-primary" />
    </>
  );
}

// ---------- Custom sections ----------

function TextSection({ d }: { d: Data }) {
  const t = tone(d);
  return (
    <>
      <section className={`relative overflow-hidden ${t.bg}`}>
        {t.light && <PetalPattern />}
        <div className="relative mx-auto flex max-w-3xl flex-col items-center gap-5 px-4 py-20 text-center sm:px-6">
          <Opt v={str(d, "eyebrow")}>{(v) => <span className={`font-painted text-2xl ${t.eyebrow}`}>{v}</span>}</Opt>
          <Opt v={str(d, "title")}>{(v) => <h2 className={`text-[32px] leading-tight font-semibold sm:text-[40px] ${t.title}`}>{v}</h2>}</Opt>
          <Opt v={str(d, "body")}>{(v) => <p className={`text-lg whitespace-pre-line ${t.body}`}>{v}</p>}</Opt>
          {str(d, "buttonLabel").trim() && (
            <SmartLink to={str(d, "buttonLink")} className="mt-2 inline-flex min-h-12 items-center rounded-full bg-paint-red px-7 text-lg font-semibold text-cream transition-colors duration-150 hover:bg-paint-red-hover">{str(d, "buttonLabel")}</SmartLink>
          )}
        </div>
      </section>
      {t.fringe && <Fringe tone={t.fringe} />}
    </>
  );
}

function CardsSection({ d }: { d: Data }) {
  const t = tone(d);
  const items = list(d, "items");
  return (
    <>
      <section className={`relative overflow-hidden ${t.bg}`}>
        {t.light && <PetalPattern />}
        <div className="relative mx-auto max-w-6xl px-4 py-20 sm:px-6">
          {(str(d, "title").trim() || str(d, "eyebrow").trim()) && <SectionTitle eyebrow={str(d, "eyebrow")} title={str(d, "title")} light={t.light} />}
          <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {items.map((c, i) => {
              const paint = paintFor(i);
              return (
                <div key={i} className="flex flex-col gap-3 rounded-lg border-[1.5px] border-sand bg-paper p-6 text-ink">
                  <span aria-hidden className="h-1.5 w-14 rounded-full" style={{ background: paint.bg }} />
                  <span className="font-display text-xl font-semibold text-primary">{str(c, "title")}</span>
                  <Opt v={str(c, "body")}>{(v) => <span className="whitespace-pre-line text-muted">{v}</span>}</Opt>
                  {str(c, "linkLabel").trim() && (
                    <SmartLink to={str(c, "link")} className="mt-auto pt-2 text-[15px] font-semibold text-paint-red hover:underline">{str(c, "linkLabel")}</SmartLink>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      </section>
      {t.fringe && <Fringe tone={t.fringe} />}
    </>
  );
}

function BannerSection({ d }: { d: Data }) {
  const t = tone(d);
  if (!str(d, "text").trim()) return null;
  return (
    <section className={t.bg}>
      <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-center gap-x-6 gap-y-3 px-4 py-6 text-center sm:px-6">
        <Rosette className="size-6 shrink-0" />
        <p className={`font-display text-xl font-semibold ${t.title}`}>{str(d, "text")}</p>
        {str(d, "buttonLabel").trim() && (
          <SmartLink to={str(d, "buttonLink")} className="inline-flex min-h-11 items-center rounded-full bg-paint-red px-6 font-semibold text-cream transition-colors duration-150 hover:bg-paint-red-hover">{str(d, "buttonLabel")}</SmartLink>
        )}
      </div>
    </section>
  );
}

// ---------- Verified provider drawn as a para shop ----------

const ROTATE_MS = 5000;

/**
 * One verified business drawn as a para shop (signboard, striped awning, window with its cover, counter with
 * details). It cycles through the list; the list doubles as tabs. Rotation pauses on hover or focus, can be paused
 * (WCAG 2.2.2), and stays still for reduced-motion users.
 */
function ShopShowcase({ d, businesses }: { d: Data; businesses: BusinessCard[] }) {
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const [hovering, setHovering] = useState(false);
  const [reduced] = useState(() => typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches);
  const count = businesses.length;
  const rotating = count > 1 && !paused && !hovering && !reduced;

  useEffect(() => {
    if (!rotating) return;
    const t = window.setInterval(() => setIndex((i) => (i + 1) % count), ROTATE_MS);
    return () => window.clearInterval(t);
  }, [rotating, count]);

  const current = index % count;
  const b = businesses[current];
  const hover = { onMouseEnter: () => setHovering(true), onMouseLeave: () => setHovering(false), onFocus: () => setHovering(true), onBlur: () => setHovering(false) };

  return (
    <section className="mx-auto max-w-6xl px-4 pt-20 pb-24 sm:px-6" aria-labelledby="shop-title">
      <div className="grid items-center gap-12 lg:grid-cols-[minmax(0,0.8fr)_minmax(0,1.2fr)]">
        <div className="flex min-w-0 flex-col gap-5" {...hover}>
          <div>
            <Opt v={str(d, "eyebrow")}>{(v) => <span className="block font-painted text-2xl text-paint-red">{v}</span>}</Opt>
            <h2 id="shop-title" className="text-[32px] leading-tight font-semibold text-primary sm:text-[40px]">{str(d, "title")}</h2>
          </div>
          <Opt v={str(d, "body")}>{(v) => <p className="whitespace-pre-line text-muted">{v}</p>}</Opt>
          {count > 1 && (
            <ul className="flex flex-col gap-2">
              {businesses.map((x, i) => (
                <li key={x.id}>
                  <button type="button" onClick={() => setIndex(i)} aria-pressed={i === current}
                    className={`flex w-full items-center gap-3 rounded-md border-[1.5px] px-3 py-2.5 text-left transition-colors duration-150 ${i === current ? "border-ink bg-paper" : "border-transparent hover:border-sand hover:bg-paper/60"}`}>
                    <CategoryTile id={x.categoryId} name={x.categoryName} className="size-10 text-xl" />
                    <span className="min-w-0 flex-1">
                      <span className={`block truncate font-display text-lg font-semibold ${i === current ? "text-paint-red" : "text-primary"}`}>{x.name}</span>
                      <span className="block truncate text-sm text-muted">{x.categoryName}{x.zoneName && ` · ${x.zoneName}`}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          {count > 1 && !reduced && (
            <button type="button" onClick={() => setPaused((p) => !p)}
              className="inline-flex min-h-10 items-center gap-2 self-start rounded-full px-3 text-sm font-semibold text-muted hover:bg-sand-deep hover:text-ink">
              {paused ? <Play className="size-4" aria-hidden /> : <Pause className="size-4" aria-hidden />}
              {paused ? "নিজে থেকে বদলানো চালু করুন" : "নিজে থেকে বদলাচ্ছে, থামান"}
            </button>
          )}
        </div>

        <div className="order-first mx-auto w-full max-w-[620px] lg:order-none" {...hover}>
          <div key={b.id} className="sign-swap">
            {/* Signboard */}
            <div className="relative z-20 mx-3 rounded-md border-4 border-marigold bg-primary px-12 py-4 text-center shadow-[0_6px_0_var(--color-night)] sm:mx-8">
              <Rosette className="absolute top-1/2 left-3 size-7 -translate-y-1/2" />
              <Rosette color="#e9b24a" className="absolute top-1/2 right-3 size-7 -translate-y-1/2" />
              <Link to={`/b/${b.slug}`} className="block truncate font-display text-[26px] leading-tight font-extrabold text-cream hover:text-marigold sm:text-[32px]">{b.name}</Link>
              {b.categoryName && <span className="block truncate font-painted text-xl text-marigold">{b.categoryName}</span>}
            </div>
            <div aria-hidden className="mx-12 flex justify-between sm:mx-20"><span className="h-4 w-2 bg-night" /><span className="h-4 w-2 bg-night" /></div>
            {/* Awning over the shopfront */}
            <div className="relative z-10 -mx-3"><Awning /></div>
            <div className="-mt-6 border-x-[10px] border-primary bg-[#e7d3a8] px-4 pt-10 pb-5 sm:px-6">
              <div className="grid gap-5 sm:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)]">
                <Link to={`/b/${b.slug}`} tabIndex={-1} aria-hidden className="relative block min-h-[220px] overflow-hidden border-[6px] border-primary bg-cream">
                  {b.coverUrl ? <img src={b.coverUrl} alt="" className="absolute inset-0 size-full object-cover" /> : <CoverArt categoryId={b.categoryId} className="absolute inset-0 size-full" />}
                </Link>
                <div className="flex min-w-0 flex-col gap-2 rounded-md border-2 border-ink bg-paper p-4">
                  {b.isVerified && <span className="inline-flex items-center gap-1 self-start rounded-full bg-leaf px-2.5 py-0.5 text-[13px] font-semibold text-white"><BadgeCheck className="size-3.5" aria-hidden />যাচাইকৃত</span>}
                  {b.zoneName && <span className="text-[15px] font-semibold text-ink">{b.zoneName}</span>}
                  <span className="text-[15px]">{b.reviewCount > 0 ? <><Stars value={b.avgRating} /> <span className="text-subtle">({bn(b.reviewCount)} রিভিউ)</span></> : <span className="text-subtle">{str(d, "newLabel")}</span>}</span>
                  {b.startingPrice != null && <span className="font-display text-2xl font-semibold text-paint-red tabular-nums">{taka(b.startingPrice)} <span className="text-base font-semibold text-muted">থেকে</span></span>}
                  <div className="mt-auto flex flex-col gap-2 pt-2">
                    <Link to={`/b/${b.slug}/book`} className="inline-flex min-h-11 items-center justify-center rounded-full bg-paint-red px-5 font-semibold text-cream transition-colors duration-150 hover:bg-paint-red-hover">{str(d, "bookButton") || "বুক করুন"}</Link>
                    <Link to={`/b/${b.slug}`} className="inline-flex min-h-11 items-center justify-center rounded-full border-2 border-ink px-5 font-semibold text-ink transition-colors duration-150 hover:bg-ink hover:text-cream">{str(d, "profileButton") || "প্রোফাইল দেখুন"}</Link>
                  </div>
                </div>
              </div>
            </div>
            {/* Plinth and footpath */}
            <div aria-hidden className="h-3 bg-primary" />
            <div aria-hidden className="-mx-5 h-4 rounded-b-md bg-sand" />
          </div>
        </div>
      </div>
    </section>
  );
}
