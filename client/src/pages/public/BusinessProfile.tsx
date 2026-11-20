import { useEffect, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BadgeCheck, CheckCircle2, ChevronLeft, ChevronRight, Clock, Flag, Heart, Home, Languages, MapPin, Megaphone, Pin, Share2, Siren, X } from "lucide-react";
import { api } from "../../api/client";
import type { GalleryItem, Paged, PublicBusiness, PublicReview, SectionKey } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Badge, Button, EmptyState, ErrorBox, LinkButton, Modal, Pagination, Skeleton, Stars, usePage, useToast } from "../../components/ui";
import { CoverArt, PetalPattern, Rosette, initialFont, initialOf, paintFor } from "../../components/brand";
import { ReviewPhotos } from "../../components/shared";
import { DAY_NAMES, REPORT_REASONS, bdDayOfWeek, bn, clock, dateShort, duration, priceLabel, todayBd } from "../../lib/format";
import { HIGHLIGHT_ICONS, LANGUAGES, themeStyle } from "../../lib/page";

/**
 * The public business page. With `preview`, it shows the owner's page (any status) from the members-only
 * preview endpoint, looking exactly the same, but booking, saving, sharing, reporting and links do nothing.
 */
export default function BusinessProfile({ preview = false }: { preview?: boolean }) {
  const { slug, businessId } = useParams();
  const { status } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const qc = useQueryClient();
  // null = closed; {} = the whole business; with content = one notice or gallery photo.
  const [report, setReport] = useState<ReportTarget | null>(null);

  const { data: b, isLoading, error } = useQuery({
    queryKey: preview ? ["business", businessId, "page", "preview"] : ["public", "business", slug],
    queryFn: () => api.get<PublicBusiness>(preview ? `/api/businesses/${businessId}/page/preview` : `/api/public/businesses/${slug}`),
  });
  const favourites = useQuery({
    queryKey: ["favourites"],
    queryFn: () => api.get<string[]>("/api/me/favourites"),
    enabled: status === "authenticated" && !preview,
  });
  const isFav = !!b && !!favourites.data?.includes(b.id);
  const fav = useMutation({
    mutationFn: () => (isFav ? api.del(`/api/me/favourites/${b!.id}`) : api.put(`/api/me/favourites/${b!.id}`, {})),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["favourites"] }); toast(isFav ? "সংরক্ষিত তালিকা থেকে সরানো হয়েছে।" : "সংরক্ষণ করা হয়েছে।"); },
    onError: (e) => toast((e as Error).message, "error"),
  });

  if (isLoading) return <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6"><Skeleton className="h-64 w-full" /><Skeleton className="mt-6 h-96 w-full" /></div>;
  if (error || !b) return <div className="mx-auto max-w-3xl px-4 py-16"><ErrorBox error={error ?? new Error("ব্যবসাটি পাওয়া যায়নি।")} /></div>;

  const today = bdDayOfWeek(todayBd());
  const todayHours = b.hours.filter((h) => h.dayOfWeek === today);
  const totalReviews = Object.values(b.ratingDistribution).reduce((s, n) => s + n, 0);
  const firstService = b.services.find((s) => s.availability === "available");
  const bookUrl = (serviceId?: string) => `/b/${b.slug}/book${serviceId ? `?serviceId=${serviceId}` : ""}`;
  const previewOnly = () => toast("এটি প্রিভিউ। এখানে বুকিং, সংরক্ষণ বা রিপোর্ট করা যায় না।");
  const requireLogin = (fn: () => void) =>
    preview ? previewOnly() : status === "authenticated" ? fn() : navigate(`/login?next=${encodeURIComponent(`/b/${b.slug}`)}`);

  const share = async () => {
    if (preview) return previewOnly();
    const url = window.location.href;
    try {
      if (navigator.share) await navigator.share({ title: b.name, url });
      else { await navigator.clipboard.writeText(url); toast("লিংক কপি করা হয়েছে।"); }
    } catch { /* cancelled */ }
  };

  // Each section renders nothing when it has no content, so empty sections never show as blank cards.
  const sectionView: Record<SectionKey, ReactNode> = {
    notices: <NoticeBoard notices={b.notices} onReport={(n) => requireLogin(() => setReport({ contentType: "notice", contentId: n.id, label: `নোটিশ: “${n.title}”` }))} />,
    highlights: <HighlightsSection items={b.highlights} />,
    services: <ServicesSection b={b} bookUrl={bookUrl} />,
    gallery: <GallerySection items={b.gallery} name={b.name} onReport={(g) => requireLogin(() => setReport({ contentType: "gallery_item", contentId: g.id!, label: g.caption ? `ছবি: “${g.caption}”` : "গ্যালারির ছবি" }))} />,
    reviews: <ReviewsSection b={b} totalReviews={totalReviews} />,
    faqs: <FaqSection items={b.faqs} />,
    team: <TeamSection members={b.team} />,
    about: <AboutSection b={b} today={today} onReport={() => requireLogin(() => setReport({}))} />,
  };

  return (
    <div className="bg-cream" style={themeStyle(b.themeKey)}
      // In preview every link (book, website, search) is inert; in-page controls like the gallery still work.
      onClickCapture={preview ? (e) => { if ((e.target as HTMLElement).closest("a")) { e.preventDefault(); e.stopPropagation(); previewOnly(); } } : undefined}>
      <div className="mx-auto max-w-7xl px-4 pt-6 pb-28 sm:px-6 lg:pb-10">
      <section>
        {/* Cover in a painted frame (theme colour, marigold inner line). */}
        <div className="rounded-[20px] bg-brand-navy p-2">
          <div className="h-44 overflow-hidden rounded-[14px] border-[3px] border-marigold sm:h-64">
            {b.headerStyle === "color" ? (
              <div className="relative size-full bg-primary"><PetalPattern className="opacity-[0.12]" /></div>
            ) : b.coverUrl ? <img src={b.coverUrl} alt={`${b.name} কভার ছবি`} className="size-full object-cover" /> : <CoverArt categoryId={b.categoryId} className="size-full" />}
          </div>
        </div>

        {/* Signboard nameplate hanging over the cover's lower edge. */}
        <div className="relative z-10 -mt-12 px-3 sm:-mt-14 sm:px-8">
          <div className="relative flex flex-wrap items-center gap-x-5 gap-y-4 rounded-[14px] border-4 border-marigold bg-primary px-5 py-5 text-cream shadow-[0_6px_0_var(--color-night)] sm:px-7">
            <Rosette className="absolute -top-4 -left-4 hidden size-9 sm:block" />
            <Rosette color="#e9b24a" className="absolute -top-4 -right-4 hidden size-9 sm:block" />
            <span className="shrink-0 rounded-full bg-cream p-1 shadow-[0_0_0_3px_var(--color-marigold)]">
              {b.logoUrl
                ? <img src={b.logoUrl} alt="" className="size-20 rounded-full object-cover sm:size-24" />
                : <span aria-hidden className={`flex size-20 items-center justify-center rounded-full text-4xl leading-none text-primary sm:size-24 sm:text-5xl ${initialFont(initialOf(b.name))}`}><span className="translate-y-[0.06em]">{initialOf(b.name)}</span></span>}
            </span>
            <div className="min-w-0 flex-[1_1_300px]">
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
                <h1 className="font-display text-3xl leading-tight font-extrabold text-cream sm:text-[40px]">{b.name}</h1>
                {b.isVerified && <span className="inline-flex items-center gap-1 rounded-full bg-leaf px-3 py-0.5 text-sm font-semibold text-white"><BadgeCheck className="size-4" aria-hidden />যাচাইকৃত</span>}
              </div>
              <div className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1 text-[15px] text-cream/85">
                <span className="font-painted text-xl text-marigold">{b.categoryName}</span>
                {b.reviewCount > 0 && <span><Stars value={b.avgRating} className="text-cream" /> ({bn(b.reviewCount)} রিভিউ)</span>}
                {b.zoneName && <span className="flex items-center gap-1"><MapPin className="size-4" aria-hidden />{b.zoneName}</span>}
              </div>
              {b.tagline && <p className="mt-1.5 font-medium text-cream">{b.tagline}</p>}
            </div>
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={() => requireLogin(() => fav.mutate())} disabled={fav.isPending} aria-pressed={isFav}
                className={`inline-flex min-h-11 items-center gap-2 rounded-full px-4 text-sm font-semibold transition-colors ${isFav ? "bg-rose text-white" : "bg-cream text-ink hover:bg-marigold"}`}>
                <Heart className="size-4" fill={isFav ? "currentColor" : "none"} aria-hidden />{isFav ? "সংরক্ষিত" : "সংরক্ষণ করুন"}
              </button>
              <button type="button" onClick={share} className="inline-flex min-h-11 items-center gap-2 rounded-full border-2 border-cream/60 px-4 text-sm font-semibold text-cream hover:border-marigold hover:text-marigold">
                <Share2 className="size-4" aria-hidden />শেয়ার করুন
              </button>
            </div>
          </div>
        </div>
      </section>

      <div className="mt-10 flex flex-col gap-6 lg:flex-row lg:items-start">
        <div className="flex min-w-0 flex-1 flex-col gap-6">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {[
              ["সম্পন্ন বুকিং", b.completedBookingCount > 0 ? `${bn(b.completedBookingCount)}+` : "নতুন"],
              ["সাড়া দেওয়ার সময়", b.avgResponseMinutes ? `~${bn(b.avgResponseMinutes)} মিনিট` : "—"],
              ["প্রতিষ্ঠার বছর", b.establishedYear ? bn(b.establishedYear) : "—"],
              ["আজ", todayHours.length ? todayHours.map((h) => `${clock(h.opensAt)}–${clock(h.closesAt)}`).join(", ") : "বন্ধ"],
            ].map(([k, v]) => (
              <div key={k} className="rounded-[14px] border-[1.5px] border-sand bg-paper px-4 py-3.5">
                <div className="text-sm text-muted">{k}</div>
                <div className="font-display text-lg leading-snug font-semibold text-primary">{v}</div>
              </div>
            ))}
          </div>

          {(b.offersHomeService || b.offersEmergencyService || b.languages.length > 0) && (
            <div className="flex flex-wrap gap-2">
              {b.offersHomeService && <Pill><Home className="size-4" aria-hidden />হোম সার্ভিস</Pill>}
              {b.offersEmergencyService && <Pill><Siren className="size-4 text-paint-red" aria-hidden />জরুরি সেবা</Pill>}
              {b.languages.length > 0 && <Pill><Languages className="size-4" aria-hidden />{b.languages.map((l) => LANGUAGES[l] ?? l).join(", ")}</Pill>}
            </div>
          )}

          {b.sections.map((key) => <div key={key} className="contents">{sectionView[key]}</div>)}
        </div>

        <aside className="hidden w-full max-w-sm shrink-0 lg:sticky lg:top-24 lg:block">
          <div className="signboard gap-4 rounded-[18px] p-6 hover:transform-none">
            <span className={`inline-flex items-center gap-2 self-start rounded-full px-3 py-1 text-sm font-semibold ${todayHours.length ? "bg-leaf text-white" : "bg-sand-deep text-muted"}`}>
              <span className={`size-2 rounded-full ${todayHours.length ? "bg-white" : "bg-zinc-400"}`} />{todayHours.length ? "আজ খোলা" : "আজ বন্ধ"}
            </span>
            {firstService && (
              <div>
                <div className="text-sm text-muted">সেবা শুরু</div>
                <div className="font-display text-3xl font-semibold text-paint-red">{priceLabel(firstService.priceType, firstService.price)}</div>
              </div>
            )}
            <LinkButton to={bookUrl(firstService?.id)} size="lg" className="w-full rounded-full">অ্যাপয়েন্টমেন্ট বুক করুন</LinkButton>
            <p className="text-sm text-muted">বুকিং বিনামূল্যে। সেবাদাতা নিশ্চিত করলে আপনি নোটিফিকেশন পাবেন।</p>
          </div>
        </aside>
      </div>

      <div className="fixed inset-x-0 bottom-0 z-20 border-t-[1.5px] border-sand bg-cream/95 p-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur lg:hidden">
        <LinkButton to={bookUrl(firstService?.id)} size="lg" className="w-full rounded-full">অ্যাপয়েন্টমেন্ট বুক করুন</LinkButton>
      </div>

      <ReportModal key={report?.contentId ?? "business"} target={report} onClose={() => setReport(null)} businessId={b.id} />
      {b.services.length === 0 && <div className="mt-6"><EmptyState title="এখনও কোনো সেবা যোগ করা হয়নি" /></div>}
      <p className="mt-10 text-center"><Link to="/search" className="font-semibold text-primary underline decoration-paint-red decoration-2 underline-offset-4 hover:text-paint-red">← আরও সেবাদাতা খুঁজুন</Link></p>
      </div>
    </div>
  );
}

type ReportTarget = { contentType?: "notice" | "gallery_item"; contentId?: string; label?: string };

function ReportModal({ target, onClose, businessId }: { target: ReportTarget | null; onClose: () => void; businessId: string }) {
  const [reason, setReason] = useState(target?.contentType ? "inappropriate_content" : "misleading_information");
  const [description, setDescription] = useState("");
  const toast = useToast();
  const m = useMutation({
    mutationFn: () => api.post("/api/reports", {
      targetType: "business", targetBusinessId: businessId, reason, description,
      contentType: target?.contentType, contentId: target?.contentId,
    }),
    onSuccess: () => { toast("রিপোর্ট জমা হয়েছে। মডারেটর পর্যালোচনা করবেন।"); onClose(); setDescription(""); },
  });
  return (
    <Modal open={!!target} onClose={onClose} title={target?.contentType ? "রিপোর্ট করুন" : "ব্যবসাটি রিপোর্ট করুন"}
      footer={<><Button variant="ghost" onClick={onClose}>বাতিল</Button><Button variant="danger" loading={m.isPending} onClick={() => m.mutate()}>রিপোর্ট জমা দিন</Button></>}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={m.error} />
        {target?.label && <p className="rounded-md bg-bg px-3 py-2 text-[15px]">{target.label}</p>}
        <label className="label">কারণ
          <select className="input" value={reason} onChange={(e) => setReason(e.target.value)}>
            {Object.entries(REPORT_REASONS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </label>
        <label className="label">বিস্তারিত <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>
          <textarea className="input" rows={4} value={description} onChange={(e) => setDescription(e.target.value)} />
        </label>
      </div>
    </Modal>
  );
}

// ---------- page sections (order and visibility come from the owner's layout) ----------

function SectionCard({ id, title, icon, children }: { id: string; title: string; icon?: ReactNode; children: ReactNode }) {
  return (
    <section className="rounded-[18px] border-[1.5px] border-sand bg-paper p-5 sm:p-7" aria-labelledby={id}>
      <h2 id={id} className="mb-5 flex items-center gap-2.5 font-display text-2xl font-semibold text-primary">
        {icon}{title}
      </h2>
      {children}
    </section>
  );
}

function Pill({ children }: { children: ReactNode }) {
  return <span className="inline-flex items-center gap-1.5 rounded-full border-[1.5px] border-sand bg-paper px-3 py-1 text-sm font-semibold text-ink">{children}</span>;
}

function NoticeBoard({ notices, onReport }: { notices: PublicBusiness["notices"]; onReport: (n: PublicBusiness["notices"][number]) => void }) {
  const [all, setAll] = useState(false);
  if (notices.length === 0) return null;
  const shown = all ? notices : notices.slice(0, 3);
  return (
    <SectionCard id="notices" title="নোটিশ বোর্ড" icon={<Megaphone className="size-5 text-paint-red" aria-hidden />}>
      <ul className="flex flex-col gap-3">
        {shown.map((n) => (
          <li key={n.id} className={`rounded-[12px] border-[1.5px] p-4 ${n.isPinned ? "border-marigold bg-marigold-soft" : "border-sand bg-cream"}`}>
            <div className="flex flex-wrap items-center gap-2">
              {n.isPinned && <Pin className="size-4 text-paint-red" aria-label="পিন করা নোটিশ" />}
              <h3 className="font-display text-lg font-semibold">{n.title}</h3>
              <span className="ml-auto text-sm text-subtle">{dateShort(n.createdAt)}</span>
            </div>
            <p className="mt-1 whitespace-pre-line text-[15px] text-ink/85">{n.body}</p>
            <div className="mt-2 flex items-center gap-3 text-sm text-subtle">
              {n.expiresAt && <span>{dateShort(n.expiresAt)} পর্যন্ত</span>}
              <button onClick={() => onReport(n)} className="ml-auto inline-flex min-h-9 items-center gap-1 hover:text-red-700 hover:underline">
                <Flag className="size-3.5" aria-hidden />রিপোর্ট
              </button>
            </div>
          </li>
        ))}
      </ul>
      {notices.length > 3 && (
        <button onClick={() => setAll(!all)} className="mt-3 min-h-11 font-medium text-primary hover:underline">
          {all ? "কম দেখুন" : `সব নোটিশ দেখুন (${bn(notices.length)})`}
        </button>
      )}
    </SectionCard>
  );
}

function HighlightsSection({ items }: { items: PublicBusiness["highlights"] }) {
  if (items.length === 0) return null;
  return (
    <SectionCard id="highlights" title="কেন আমাদের বেছে নেবেন">
      <ul className="grid gap-4 sm:grid-cols-2">
        {items.map((h, i) => {
          const Icon = HIGHLIGHT_ICONS[h.iconKey]?.icon ?? BadgeCheck;
          return (
            <li key={h.id ?? i} className="flex gap-3">
              <span className="flex size-12 shrink-0 items-center justify-center rounded-full" style={{ background: paintFor(i).bg, color: paintFor(i).fg, boxShadow: `inset 0 0 0 3px var(--color-paper), 0 0 0 2px ${paintFor(i).bg}` }}><Icon className="size-5" aria-hidden /></span>
              <div><div className="font-display text-lg font-semibold">{h.title}</div>{h.description && <p className="text-[15px] text-muted">{h.description}</p>}</div>
            </li>
          );
        })}
      </ul>
    </SectionCard>
  );
}

function ServicesSection({ b, bookUrl }: { b: PublicBusiness; bookUrl: (serviceId?: string) => string }) {
  return (
    <SectionCard id="services" title="সেবাসমূহ">
      {b.services.length === 0 ? <p className="text-muted">এখনও কোনো সেবা যোগ করা হয়নি</p> : (
        <ul className="flex flex-col gap-3">
          {b.services.map((s) => (
            <li key={s.id} className="flex flex-wrap items-center gap-4 rounded-[12px] border-[1.5px] border-sand bg-cream p-4 transition-colors hover:border-ink">
              {s.imageUrl && <img src={s.imageUrl} alt="" className="size-16 shrink-0 rounded-[10px] border-2 border-primary object-cover" />}
              <div className="min-w-0 flex-[1_1_240px]">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-display text-lg font-semibold">{s.name}</span>
                  {s.isFeatured && <span className="rounded-full bg-marigold px-2.5 py-0.5 text-xs font-semibold text-ink">ফিচার্ড</span>}
                  {s.availability !== "available" && <Badge className="text-xs">এখন পাওয়া যাচ্ছে না</Badge>}
                </div>
                {s.shortDescription && <p className="text-[15px] text-muted">{s.shortDescription}</p>}
                <p className="text-sm text-subtle"><Clock className="mr-1 inline size-4" aria-hidden />{duration(s.durationMinutes)}{s.avgRating > 0 && <> · ★ {bn(s.avgRating)}</>}</p>
              </div>
              <div className="min-w-24 font-display text-xl font-semibold text-paint-red">{priceLabel(s.priceType, s.price, s.priceMax)}</div>
              <LinkButton to={bookUrl(s.id)} size="sm" className={`rounded-full px-5 ${s.availability !== "available" ? "pointer-events-none opacity-50" : ""}`} aria-disabled={s.availability !== "available"}>বুক করুন</LinkButton>
            </li>
          ))}
        </ul>
      )}
    </SectionCard>
  );
}

function GallerySection({ items, name, onReport }: { items: GalleryItem[]; name: string; onReport: (g: GalleryItem) => void }) {
  const [open, setOpen] = useState<number | null>(null);
  const [all, setAll] = useState(false);
  if (items.length === 0) return null;
  const shown = all ? items : items.slice(0, 6);
  return (
    <SectionCard id="gallery" title="গ্যালারি">
      <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        {shown.map((g, i) => (
          <li key={g.id ?? g.imageUrl}>
            <button onClick={() => setOpen(i)} className="block w-full overflow-hidden rounded-[12px] border-2 border-sand transition-colors duration-150 hover:border-ink" aria-label={g.caption ? `ছবি: ${g.caption}` : `ছবি ${bn(i + 1)} বড় করে দেখুন`}>
              <img src={g.imageUrl} alt={g.caption ?? `${name}-এর কাজের ছবি`} loading="lazy" className="aspect-square w-full object-cover" />
            </button>
          </li>
        ))}
      </ul>
      {items.length > 6 && (
        <button onClick={() => setAll(!all)} className="mt-3 min-h-11 font-medium text-primary hover:underline">
          {all ? "কম দেখুন" : `সব ছবি দেখুন (${bn(items.length)})`}
        </button>
      )}
      {open !== null && <Lightbox items={items} index={open} onIndex={setOpen} onClose={() => setOpen(null)}
        onReport={(g) => { setOpen(null); onReport(g); }} />}
    </SectionCard>
  );
}

function Lightbox({ items, index, onIndex, onClose, onReport }: {
  items: GalleryItem[]; index: number; onIndex: (i: number) => void; onClose: () => void; onReport: (g: GalleryItem) => void;
}) {
  const go = (by: number) => onIndex((index + by + items.length) % items.length);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
      if (e.key === "ArrowRight") go(1);
      if (e.key === "ArrowLeft") go(-1);
    };
    window.addEventListener("keydown", onKey);
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { window.removeEventListener("keydown", onKey); document.body.style.overflow = overflow; };
  });
  const g = items[index];
  return (
    <div role="dialog" aria-modal="true" aria-label="ছবি" className="fixed inset-0 z-50 flex flex-col bg-zinc-950/90 p-4" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="flex items-center justify-between text-white">
        <span className="text-sm">{bn(index + 1)} / {bn(items.length)}</span>
        <button onClick={onClose} className="flex size-11 items-center justify-center rounded-md hover:bg-white/10" aria-label="বন্ধ করুন"><X className="size-6" aria-hidden /></button>
      </div>
      <div className="flex min-h-0 flex-1 items-center justify-center gap-2" onClick={(e) => e.target === e.currentTarget && onClose()}>
        {items.length > 1 && <button onClick={() => go(-1)} className="flex size-11 shrink-0 items-center justify-center rounded-full bg-white/10 text-white hover:bg-white/20" aria-label="আগের ছবি"><ChevronLeft className="size-6" aria-hidden /></button>}
        <img src={g.imageUrl} alt={g.caption ?? ""} className="max-h-full min-w-0 max-w-full rounded-md object-contain" />
        {items.length > 1 && <button onClick={() => go(1)} className="flex size-11 shrink-0 items-center justify-center rounded-full bg-white/10 text-white hover:bg-white/20" aria-label="পরের ছবি"><ChevronRight className="size-6" aria-hidden /></button>}
      </div>
      <div className="flex items-center justify-center gap-4 pt-3 text-white">
        {g.caption && <p>{g.caption}</p>}
        <button onClick={() => onReport(g)} className="inline-flex min-h-9 items-center gap-1 text-sm text-white/70 hover:text-white hover:underline">
          <Flag className="size-3.5" aria-hidden />ছবিটি রিপোর্ট করুন
        </button>
      </div>
    </div>
  );
}

/** Reviews shown per page; the profile itself already carries the first page (the latest five). */
const REVIEWS_PER_PAGE = 5;

function ReviewsSection({ b, totalReviews }: { b: PublicBusiness; totalReviews: number }) {
  const [page, setPage] = usePage();
  const more = useQuery({
    queryKey: ["public", "reviews", b.id, page],
    queryFn: () => api.get<Paged<PublicReview>>(`/api/public/businesses/${b.id}/reviews`, { page, pageSize: REVIEWS_PER_PAGE }),
    enabled: page > 1,
    placeholderData: keepPreviousData,
  });
  const shown = page === 1 ? b.latestReviews : more.data?.items ?? [];
  return (
    <section className="rounded-[18px] border-[1.5px] border-sand bg-paper p-5 sm:p-7" aria-labelledby="reviews">
      <h2 id="reviews" className="mb-5 flex items-center gap-2.5 font-display text-2xl font-semibold text-primary">গ্রাহক রিভিউ</h2>
      {totalReviews === 0 ? <p className="text-muted">এখনও কোনো রিভিউ নেই। শুধুমাত্র সম্পন্ন বুকিংয়ের গ্রাহকরাই রিভিউ দিতে পারেন।</p> : (
        <>
          <div className="mb-6 flex flex-wrap items-center gap-8">
            <div className="text-center">
              <div className="font-display text-5xl leading-none font-semibold text-primary">{bn(b.avgRating)}</div>
              <div className="text-lg tracking-widest text-marigold" aria-hidden>{"★".repeat(Math.round(b.avgRating))}</div>
              <div className="text-sm text-subtle">{bn(b.reviewCount)} রিভিউ</div>
            </div>
            <div className="flex min-w-60 flex-1 flex-col gap-1.5">
              {["5", "4", "3", "2", "1"].map((star) => {
                const n = b.ratingDistribution[star] ?? 0;
                return (
                  <div key={star} className="flex items-center gap-2.5 text-sm">
                    <span className="w-7">{bn(star)}★</span>
                    <span className="h-2.5 flex-1 overflow-hidden rounded-full bg-sand-deep"><span className="block h-full rounded-full bg-marigold" style={{ width: `${(n / totalReviews) * 100}%` }} /></span>
                    <span className="w-9 text-right text-subtle">{bn(n)}</span>
                  </div>
                );
              })}
            </div>
          </div>
          {page > 1 && !more.data && <Skeleton className="h-40" />}
          <ul className="flex flex-col gap-4">
            {shown.map((r) => (
              <li key={r.id} className="flex flex-col gap-2 border-t border-sand pt-4">
                <div className="flex items-center gap-3">
                  <span aria-hidden className={`flex size-11 shrink-0 items-center justify-center rounded-full text-xl leading-none ${initialFont(initialOf(r.customerName))}`} style={{ background: paintFor(r.customerName.length).bg, color: paintFor(r.customerName.length).fg }}><span className="translate-y-[0.06em]">{initialOf(r.customerName)}</span></span>
                  <div className="flex-1"><div className="font-semibold">{r.customerName}</div><div className="text-sm text-subtle">{r.serviceName} · {dateShort(r.createdAt)}</div></div>
                  <Stars value={r.rating} />
                </div>
                {r.comment && <p className="text-ink/85">{r.comment}</p>}
                {r.photos.length > 0 && <ReviewPhotos photos={r.photos} />}
                <span className="inline-flex items-center gap-1 text-[13px] font-medium text-brand-green"><CheckCircle2 className="size-3.5" aria-hidden />সম্পন্ন বুকিংয়ের রিভিউ</span>
                {r.replyBody && <div className="rounded-[12px] border-l-4 border-primary bg-sand-deep px-4 py-3 text-[15px]"><span className="font-semibold">ব্যবসার উত্তর: </span>{r.replyBody}</div>}
              </li>
            ))}
          </ul>
          <Pagination page={page} total={totalReviews} pageSize={REVIEWS_PER_PAGE} onChange={setPage} scrollTop={false} label="রিভিউর পাতা" />
        </>
      )}
    </section>
  );
}

function FaqSection({ items }: { items: PublicBusiness["faqs"] }) {
  if (items.length === 0) return null;
  return (
    <SectionCard id="faqs" title="সাধারণ প্রশ্নোত্তর">
      <div className="flex flex-col gap-2.5">
        {items.map((f, i) => (
          <details key={f.id ?? i} className="group rounded-[12px] border-[1.5px] border-sand bg-cream open:border-ink">
            <summary className="flex min-h-12 cursor-pointer list-none items-center gap-3 px-4 py-2 font-display text-lg font-semibold text-primary [&::-webkit-details-marker]:hidden">
              <span className="flex-1">{f.question}</span>
              <Rosette color="#c4373f" className="size-5 shrink-0 transition-transform group-open:rotate-45" />
            </summary>
            <p className="whitespace-pre-line px-4 pb-4 text-[15px] text-ink/85">{f.answer}</p>
          </details>
        ))}
      </div>
    </SectionCard>
  );
}

function TeamSection({ members }: { members: PublicBusiness["team"] }) {
  if (members.length === 0) return null;
  return (
    <SectionCard id="team" title="আমাদের টিম">
      <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3">
        {members.map((m, i) => (
          <li key={i} className="flex flex-col items-center gap-2 text-center">
            {m.avatarUrl
              ? <img src={m.avatarUrl} alt="" className="size-20 rounded-full object-cover shadow-[0_0_0_3px_var(--color-paper),0_0_0_5px_var(--color-marigold)]" />
              : <span aria-hidden className={`flex size-20 items-center justify-center rounded-full text-3xl leading-none ${initialFont(initialOf(m.name))}`} style={{ background: paintFor(i).bg, color: paintFor(i).fg, boxShadow: `inset 0 0 0 4px var(--color-paper), 0 0 0 2px ${paintFor(i).bg}` }}><span className="translate-y-[0.06em]">{initialOf(m.name)}</span></span>}
            <div className="flex flex-col items-center gap-1">
              <div className="font-display text-lg leading-tight font-semibold">{m.name}</div>
              {m.roleName && <span className="rounded-full bg-primary px-2.5 py-0.5 text-xs font-semibold text-cream">{m.roleName}</span>}
              {m.title && m.title !== m.roleName && <div className="text-sm text-muted">{m.title}</div>}
            </div>
          </li>
        ))}
      </ul>
    </SectionCard>
  );
}

function AboutSection({ b, today, onReport }: { b: PublicBusiness; today: number; onReport: () => void }) {
  return (
    <section className="rounded-[18px] border-[1.5px] border-sand bg-paper p-5 sm:p-7" aria-labelledby="about">
      <h2 id="about" className="mb-4 flex items-center gap-2.5 font-display text-2xl font-semibold text-primary">পরিচিতি</h2>
      {(b.description || b.shortDescription) && <p className="mb-5 whitespace-pre-line text-ink/85">{b.description || b.shortDescription}</p>}
      <dl className="grid gap-4 text-[15px] sm:grid-cols-2">
        {b.addressLine && <div><dt className="text-muted">ঠিকানা</dt><dd className="font-semibold">{b.addressLine}{b.zoneName && `, ${b.zoneName}`}</dd></div>}
        {b.coverageNote && <div><dt className="text-muted">সেবা এলাকা</dt><dd className="font-semibold">{b.coverageNote}</dd></div>}
        {b.cancellationPolicy && <div><dt className="text-muted">বাতিলের নীতি</dt><dd className="font-semibold">{b.cancellationPolicy}</dd></div>}
      </dl>
      <h3 className="mt-6 mb-2 font-display text-lg font-semibold text-primary">কাজের সময়</h3>
      <ul className="grid gap-1 text-[15px] sm:grid-cols-2">
        {DAY_NAMES.map((name, d) => {
          const hrs = b.hours.filter((h) => h.dayOfWeek === d);
          return (
            <li key={name} className={`flex justify-between gap-4 rounded-[8px] px-3 py-1.5 ${d === today ? "bg-marigold-soft font-semibold" : ""}`}>
              <span>{name}</span><span>{hrs.length ? hrs.map((h) => `${clock(h.opensAt)}–${clock(h.closesAt)}`).join(", ") : "বন্ধ"}</span>
            </li>
          );
        })}
      </ul>
      <button onClick={onReport} className="mt-6 flex min-h-11 items-center gap-2 font-medium text-red-700 hover:underline">
        <Flag className="size-4" aria-hidden />ব্যবসাটি রিপোর্ট করুন
      </button>
    </section>
  );
}
