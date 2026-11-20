import { createContext, useContext, useEffect, useState } from "react";
import { Link, Outlet } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Check, ExternalLink, Eye, EyeOff, ImagePlus, Lock, Megaphone, Pencil, Pin, Plus,
  RotateCw, Trash2, X,
} from "lucide-react";
import { api } from "../../api/client";
import type { Faq, GalleryItem, HeaderStyle, Highlight, Notice, PageSection, PageSettings, ThemeKey } from "../../api/types";
import { Badge, Button, EmptyState, ErrorBox, LinkButton, LoadingList, Modal, PageHeader, Segmented, Skeleton, useToast } from "../../components/ui";
import { SectionTabs } from "../../components/SectionTabs";
import { FilePicker, useImageUpload } from "../../components/ImageUpload";
import { UNLIMITED, useBusinessPlan } from "../../lib/billing";
import { bn, dateShort } from "../../lib/format";
import { HEADER_STYLES, HIGHLIGHT_ICONS, SECTIONS, THEMES } from "../../lib/page";
import { useBiz } from "./Dashboard";

const PageDataContext = createContext<PageSettings | null>(null);
const usePageData = () => useContext(PageDataContext)!;

/**
 * "পেজ সাজান": one URL per part (notices, look, gallery, content, team) under tabs. The page settings load once
 * here and are shared with the tab pages; the preview button stays on every tab.
 */
export default function PageDesignLayout() {
  const biz = useBiz();
  const [previewing, setPreviewing] = useState(false);
  const page = usePage(biz?.id);
  if (!biz) return <Skeleton className="h-96" />;
  return (
    <>
      <PageHeader title="পেজ সাজান" subtitle="আপনার পাবলিক পেজের রং, সেকশন, নোটিশ, ছবি ও তথ্য। পরিবর্তন সাথে সাথে পেজে দেখা যাবে।"
        actions={<>
          <Button onClick={() => setPreviewing(true)}><Eye className="size-4" aria-hidden />প্রিভিউ দেখুন</Button>
          {biz.status === "published" && <LinkButton to={`/b/${biz.slug}`} variant="outline" target="_blank"><ExternalLink className="size-4" aria-hidden />পাবলিক পেজ</LinkButton>}
        </>} />
      {previewing && <PreviewOverlay onClose={() => setPreviewing(false)} />}
      <SectionTabs label="পেজ সাজানোর অংশ" items={[
        { to: "notices", label: "নোটিশ বোর্ড" }, { to: "look", label: "থিম ও সেকশন" }, { to: "gallery", label: "গ্যালারি" },
        { to: "content", label: "প্রশ্নোত্তর ও বিশেষত্ব" }, { to: "team", label: "টিম" },
      ]} />
      <ErrorBox error={page.error} />
      {page.isLoading || !page.data ? <LoadingList /> : (
        <PageDataContext.Provider value={page.data}><Outlet context={biz} /></PageDataContext.Provider>
      )}
    </>
  );
}

export const PageNotices = () => <NoticesTab page={usePageData()} />;
export function PageLook() {
  const page = usePageData();
  return <LookTab key={JSON.stringify([page.themeKey, page.headerStyle, page.sections])} page={page} />;
}
export const PageGallery = () => <GalleryTab page={usePageData()} />;
export function PageContent() {
  const page = usePageData();
  return <ContentTab key={JSON.stringify([page.faqs, page.highlights])} page={page} />;
}
export function PageTeam() {
  const page = usePageData();
  return <TeamTab key={JSON.stringify(page.team)} page={page} />;
}

// ---------- data ----------

function usePage(businessId: string | undefined) {
  return useQuery({
    queryKey: ["business", businessId, "page"],
    queryFn: () => api.get<PageSettings>(`/api/businesses/${businessId}/page`),
    enabled: !!businessId,
  });
}

/** Saves to a page endpoint and refreshes the page settings (and the public profile cache). */
function useSave<T>(path: string, method: "put" | "post" = "put", message = "সংরক্ষিত হয়েছে।") {
  const biz = useBiz()!;
  const qc = useQueryClient();
  const toast = useToast();
  return useMutation({
    mutationFn: (body: T) => api[method](`/api/businesses/${biz.id}/${path}`, body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["business", biz.id, "page"] });
      qc.invalidateQueries({ queryKey: ["public", "business", biz.slug] });
      if (message) toast(message);
    },
  });
}

function useLimit(key: string) {
  const biz = useBiz()!;
  const plan = useBusinessPlan(biz.id);
  const limit = plan.data?.limits[key];
  return { limit, loaded: !!plan.data, planName: plan.data?.planName };
}

function UpgradeLink({ children }: { children: React.ReactNode }) {
  const biz = useBiz()!;
  return (
    <Link to={`/w/${biz.id}/billing`} className="inline-flex items-center gap-1 text-sm font-medium text-amber-800 hover:underline">
      <Lock className="size-3.5" aria-hidden />{children}
    </Link>
  );
}

const layoutWith = (sections: PageSection[], key: string, visible: boolean) =>
  sections.map((s) => (s.key === key ? { ...s, visible } : s));

// ---------- notice board ----------

type NoticeDraft = { id?: string; title: string; body: string; isPinned: boolean; isPublished: boolean; expiresOn: string };

const toDraft = (n?: Notice): NoticeDraft => ({
  id: n?.id, title: n?.title ?? "", body: n?.body ?? "", isPinned: n?.isPinned ?? false, isPublished: n?.isPublished ?? true,
  // Shown as a Dhaka calendar date; the notice stays up until the end of that day.
  expiresOn: n?.expiresAt ? new Date(n.expiresAt).toLocaleDateString("en-CA", { timeZone: "Asia/Dhaka" }) : "",
});

function noticeState(n: Notice): { label: string; tone: "green" | "neutral" | "amber" } {
  if (n.isLive) return { label: "চালু", tone: "green" };
  if (!n.isPublished) return { label: "খসড়া", tone: "neutral" };
  return { label: "মেয়াদোত্তীর্ণ", tone: "amber" };
}

function NoticesTab({ page }: { page: PageSettings }) {
  const biz = useBiz()!;
  const [draft, setDraft] = useState<NoticeDraft | null>(null);
  const [deleting, setDeleting] = useState<Notice | null>(null);
  const qc = useQueryClient();
  const toast = useToast();
  const { limit, loaded } = useLimit("notices.max");
  const live = page.notices.filter((n) => n.isLive).length;
  const atLimit = loaded && limit !== UNLIMITED && limit !== undefined && live >= limit;
  const visible = page.sections.find((s) => s.key === "notices")?.visible ?? true;
  const layout = useSave<{ sections: PageSection[] }>("page/layout", "put", "");
  const refresh = () => { qc.invalidateQueries({ queryKey: ["business", biz.id, "page"] }); qc.invalidateQueries({ queryKey: ["public", "business", biz.slug] }); };
  const remove = useMutation({
    mutationFn: (id: string) => api.del(`/api/businesses/${biz.id}/page/notices/${id}`),
    onSuccess: () => { refresh(); setDeleting(null); toast("নোটিশটি মুছে ফেলা হয়েছে।"); },
  });

  return (
    <div className="flex flex-col gap-5">
      <section className="card flex flex-wrap items-center gap-4 p-5">
        <span className="flex size-11 items-center justify-center rounded-md bg-primary-light text-primary"><Megaphone className="size-5" aria-hidden /></span>
        <div className="min-w-0 flex-[1_1_260px]">
          <h2 className="font-semibold">পাবলিক পেজে নোটিশ বোর্ড</h2>
          <p className="text-sm text-muted">{visible ? "এখন গ্রাহকরা চালু নোটিশগুলো দেখতে পাচ্ছেন।" : "নোটিশ বোর্ড এখন লুকানো আছে। নোটিশগুলো মুছে যায়নি।"}</p>
        </div>
        <Button variant={visible ? "outline" : "primary"} loading={layout.isPending}
          onClick={() => layout.mutate({ sections: layoutWith(page.sections, "notices", !visible) }, { onSuccess: () => toast(visible ? "নোটিশ বোর্ড লুকানো হয়েছে।" : "নোটিশ বোর্ড দেখানো হচ্ছে।") })}>
          {visible ? <><EyeOff className="size-4" aria-hidden />লুকিয়ে রাখুন</> : <><Eye className="size-4" aria-hidden />দেখান</>}
        </Button>
      </section>
      <ErrorBox error={layout.error ?? remove.error} />

      <div className="flex flex-wrap items-center gap-3">
        <Button onClick={() => setDraft(toDraft())}><Plus className="size-4" aria-hidden />নতুন নোটিশ</Button>
        {loaded && (
          <span className="text-sm text-muted">চালু নোটিশ: {bn(live)}{limit === UNLIMITED ? "" : ` / ${bn(limit ?? 0)}`}</span>
        )}
        {atLimit && <UpgradeLink>আরও নোটিশ চালু রাখতে প্ল্যান আপগ্রেড করুন</UpgradeLink>}
      </div>

      {page.notices.length === 0 ? (
        <EmptyState icon={<Megaphone aria-hidden />} title="এখনও কোনো নোটিশ নেই"
          description="অফার, ছুটির খবর, নতুন সেবা বা গুরুত্বপূর্ণ ঘোষণা নোটিশ বোর্ডে দিন। গ্রাহকরা আপনার পেজে দেখবেন।" />
      ) : (
        <ul className="flex flex-col gap-3">
          {page.notices.map((n) => {
            const st = noticeState(n);
            return (
              <li key={n.id} className={`card flex flex-col gap-2 p-5 ${n.isPinned ? "border-primary/40" : ""}`}>
                <div className="flex flex-wrap items-center gap-2">
                  <h3 className="mr-auto font-semibold">{n.title}</h3>
                  {n.isPinned && <Badge tone="blue"><Pin className="size-3" aria-hidden />পিন করা</Badge>}
                  <Badge tone={st.tone}>{st.label}</Badge>
                </div>
                <p className="line-clamp-3 whitespace-pre-line text-[15px] text-muted">{n.body}</p>
                <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-subtle">
                  <span>{dateShort(n.createdAt)}</span>
                  {n.expiresAt && <span>মেয়াদ: {dateShort(n.expiresAt)} পর্যন্ত</span>}
                  <span className="ml-auto flex gap-1">
                    <Button size="sm" variant="ghost" onClick={() => setDraft(toDraft(n))}><Pencil className="size-4" aria-hidden />সম্পাদনা</Button>
                    <Button size="sm" variant="ghost" className="text-red-700" onClick={() => setDeleting(n)} aria-label="নোটিশ মুছুন"><Trash2 className="size-4" aria-hidden /></Button>
                  </span>
                </div>
              </li>
            );
          })}
        </ul>
      )}

      {draft && <NoticeEditor draft={draft} setDraft={setDraft} onSaved={() => { refresh(); setDraft(null); }} />}
      <Modal open={!!deleting} onClose={() => setDeleting(null)} title="নোটিশটি মুছে ফেলবেন?"
        footer={<><Button variant="ghost" onClick={() => setDeleting(null)}>বাতিল</Button><Button variant="danger" loading={remove.isPending} onClick={() => deleting && remove.mutate(deleting.id)}>মুছে ফেলুন</Button></>}>
        <p className="text-muted">“{deleting?.title}” নোটিশটি স্থায়ীভাবে মুছে যাবে। শুধু লুকাতে চাইলে সম্পাদনা করে “এখনই প্রকাশ করুন” বন্ধ করুন।</p>
      </Modal>
    </div>
  );
}

function NoticeEditor({ draft, setDraft, onSaved }: { draft: NoticeDraft; setDraft: (d: NoticeDraft | null) => void; onSaved: () => void }) {
  const biz = useBiz()!;
  const toast = useToast();
  const set = (patch: Partial<NoticeDraft>) => setDraft({ ...draft, ...patch });
  const save = useMutation({
    mutationFn: () => {
      const body = {
        title: draft.title, body: draft.body, isPinned: draft.isPinned, isPublished: draft.isPublished,
        expiresAt: draft.expiresOn ? new Date(`${draft.expiresOn}T23:59:59+06:00`).toISOString() : null,
      };
      return draft.id ? api.put(`/api/businesses/${biz.id}/page/notices/${draft.id}`, body) : api.post(`/api/businesses/${biz.id}/page/notices`, body);
    },
    onSuccess: () => { toast(draft.id ? "নোটিশ হালনাগাদ হয়েছে।" : "নোটিশ যোগ হয়েছে।"); onSaved(); },
  });
  const tomorrow = new Date(Date.now() + 86_400_000).toLocaleDateString("en-CA", { timeZone: "Asia/Dhaka" });
  return (
    <Modal open onClose={() => setDraft(null)} title={draft.id ? "নোটিশ সম্পাদনা" : "নতুন নোটিশ"}
      footer={<><Button variant="ghost" onClick={() => setDraft(null)}>বাতিল</Button><Button loading={save.isPending} disabled={draft.title.trim().length < 2 || draft.body.trim().length < 2} onClick={() => save.mutate()}>সংরক্ষণ করুন</Button></>}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={save.error} />
        <label className="label">শিরোনাম
          <input className="input" maxLength={120} value={draft.title} onChange={(e) => set({ title: e.target.value })} placeholder="যেমন: ঈদ উপলক্ষে ২০% ছাড়" />
        </label>
        <label className="label">বিস্তারিত
          <textarea className="input" rows={5} maxLength={1000} value={draft.body} onChange={(e) => set({ body: e.target.value })} placeholder="অফারের শর্ত, সময়, বা গ্রাহকদের যা জানা দরকার" />
          <span className="self-end text-xs font-normal text-subtle">{bn(draft.body.length)} / {bn(1000)}</span>
        </label>
        <p className="-mt-2 text-sm text-subtle">নোটিশে ফোন নম্বর দেওয়া যাবে না। গ্রাহকরা প্ল্যাটফর্মের মাধ্যমেই বুকিং করবেন।</p>
        <label className="label">কত দিন পর্যন্ত দেখাবে <span className="text-sm font-normal text-subtle">(ঐচ্ছিক — খালি রাখলে যতদিন না সরান)</span>
          <input className="input" type="date" min={tomorrow} value={draft.expiresOn} onChange={(e) => set({ expiresOn: e.target.value })} />
        </label>
        <label className="flex min-h-11 items-center gap-2.5"><input type="checkbox" className="size-5 accent-primary" checked={draft.isPinned} onChange={(e) => set({ isPinned: e.target.checked })} />উপরে পিন করে রাখুন (একটিই পিন করা যায়)</label>
        <label className="flex min-h-11 items-center gap-2.5"><input type="checkbox" className="size-5 accent-primary" checked={draft.isPublished} onChange={(e) => set({ isPublished: e.target.checked })} />এখনই প্রকাশ করুন</label>
      </div>
    </Modal>
  );
}

// ---------- theme & sections ----------

function LookTab({ page }: { page: PageSettings }) {
  const [theme, setTheme] = useState<ThemeKey>(page.themeKey);
  const [header, setHeader] = useState<HeaderStyle>(page.headerStyle);
  const [sections, setSections] = useState<PageSection[]>(page.sections);
  const appearance = useSave<{ themeKey: ThemeKey; headerStyle: HeaderStyle }>("page/appearance", "put", "থিম সংরক্ষিত হয়েছে।");
  const layout = useSave<{ sections: PageSection[] }>("page/layout", "put", "সেকশন সংরক্ষিত হয়েছে।");
  const { limit, loaded } = useLimit("page.layout");
  const canReorder = !loaded || !!limit;
  const move = (i: number, by: number) => {
    const next = [...sections];
    [next[i], next[i + by]] = [next[i + by], next[i]];
    setSections(next);
  };

  return (
    <div className="flex flex-col gap-5">
      <section className="card flex flex-col gap-4 p-6">
        <h2 className="text-base font-semibold">থিমের রং</h2>
        <div role="radiogroup" aria-label="থিমের রং" className="flex flex-wrap gap-3">
          {(Object.keys(THEMES) as ThemeKey[]).map((k) => (
            <button key={k} role="radio" aria-checked={theme === k} onClick={() => setTheme(k)}
              className={`flex min-h-11 items-center gap-2 rounded-md border px-3 text-sm font-medium ${theme === k ? "border-ink ring-2 ring-ink/10" : "border-line hover:border-zinc-400"}`}>
              <span className="flex size-6 items-center justify-center rounded-full text-white" style={{ background: THEMES[k].accent }}>{theme === k && <Check className="size-3.5" aria-hidden />}</span>
              {THEMES[k].label}
            </button>
          ))}
        </div>
        <h3 className="mt-2 font-semibold">হেডারের ধরন</h3>
        <div role="radiogroup" aria-label="হেডারের ধরন" className="grid gap-3 sm:grid-cols-2">
          {(Object.keys(HEADER_STYLES) as HeaderStyle[]).map((k) => (
            <button key={k} role="radio" aria-checked={header === k} onClick={() => setHeader(k)}
              className={`flex flex-col gap-2 rounded-md border p-3 text-left ${header === k ? "border-ink ring-2 ring-ink/10" : "border-line hover:border-zinc-400"}`}>
              <span className="h-14 w-full rounded" style={k === "color"
                ? { background: `linear-gradient(135deg, ${THEMES[theme].hover}, ${THEMES[theme].accent})` }
                : { background: "linear-gradient(135deg,#cbd5e1,#e2e8f0)" }} aria-hidden />
              <span className="font-medium">{HEADER_STYLES[k].label}</span>
              <span className="text-sm text-subtle">{HEADER_STYLES[k].hint}</span>
            </button>
          ))}
        </div>
        <ErrorBox error={appearance.error} />
        <div><Button loading={appearance.isPending} onClick={() => appearance.mutate({ themeKey: theme, headerStyle: header })}>সংরক্ষণ করুন</Button></div>
        <p className="text-sm text-subtle">যাচাইকৃত ব্যাজ, রেটিং, রিভিউ ও বুকিং বাটন সব পেজে একই রকম থাকে, যাতে গ্রাহক সহজে বিশ্বাস করতে পারেন।</p>
      </section>

      <section className="card flex flex-col gap-4 p-6">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="mr-auto text-base font-semibold">সেকশন</h2>
          {loaded && !limit && <UpgradeLink>ক্রম বদলাতে প্রো প্ল্যান লাগবে</UpgradeLink>}
        </div>
        <p className="-mt-2 text-sm text-muted">কোন সেকশন দেখাবে আর কোনটা আগে আসবে ঠিক করুন। খালি সেকশন পেজে এমনিতেই দেখায় না।</p>
        <ol className="flex flex-col divide-y divide-zinc-100 rounded-md border border-line">
          {sections.map((s, i) => {
            const meta = SECTIONS[s.key];
            return (
              <li key={s.key} className={`flex min-h-14 items-center gap-2 px-3 ${s.visible ? "" : "bg-zinc-50 text-subtle"}`}>
                <span className="w-6 text-center text-sm text-subtle">{bn(i + 1)}</span>
                <span className="mr-auto font-medium">{meta.label}</span>
                {meta.hideable ? (
                  <Button size="sm" variant="ghost" onClick={() => setSections(layoutWith(sections, s.key, !s.visible))} aria-pressed={!s.visible}>
                    {s.visible ? <><Eye className="size-4" aria-hidden />দেখাচ্ছে</> : <><EyeOff className="size-4" aria-hidden />লুকানো</>}
                  </Button>
                ) : <span className="px-2 text-xs text-subtle">সবসময় দেখায়</span>}
                <Button size="sm" variant="ghost" disabled={!canReorder || i === 0} onClick={() => move(i, -1)} aria-label={`${meta.label} উপরে নিন`}><ArrowUp className="size-4" aria-hidden /></Button>
                <Button size="sm" variant="ghost" disabled={!canReorder || i === sections.length - 1} onClick={() => move(i, 1)} aria-label={`${meta.label} নিচে নিন`}><ArrowDown className="size-4" aria-hidden /></Button>
              </li>
            );
          })}
        </ol>
        <ErrorBox error={layout.error} />
        <div><Button loading={layout.isPending} onClick={() => layout.mutate({ sections })}>সংরক্ষণ করুন</Button></div>
      </section>
    </div>
  );
}

// ---------- gallery ----------

function GalleryTab({ page }: { page: PageSettings }) {
  const biz = useBiz()!;
  const [items, setItems] = useState<GalleryItem[]>(page.gallery);
  const save = useSave<GalleryItem[]>("page/gallery", "put", "");
  const { upload, busy } = useImageUpload(biz.id);
  const toast = useToast();
  const { limit, loaded } = useLimit("gallery.max");
  const room = !loaded || limit === UNLIMITED ? Infinity : Math.max(0, (limit ?? 0) - items.length);

  /** Every change is saved at once, so uploaded photos never sit unsaved. */
  const commit = (next: GalleryItem[], message = "গ্যালারি সংরক্ষিত হয়েছে।") => {
    const prev = items;
    setItems(next);
    save.mutate(next, { onSuccess: () => toast(message), onError: () => setItems(prev) });
  };
  const add = async (files: File[]) => {
    if (files.length > room) toast(`এই প্ল্যানে আর ${bn(room)}টি ছবি যোগ করা যাবে।`, "error");
    const added: GalleryItem[] = [];
    for (const f of files.slice(0, room)) {
      const url = await upload(f);
      if (url) added.push({ id: null, imageUrl: url, caption: null });
    }
    if (added.length) commit([...items, ...added], `${bn(added.length)}টি ছবি যোগ হয়েছে।`);
  };
  const move = (i: number, by: number) => {
    const next = [...items];
    [next[i], next[i + by]] = [next[i + by], next[i]];
    commit(next);
  };

  return (
    <section className="card flex flex-col gap-4 p-6">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="mr-auto text-base font-semibold">গ্যালারি</h2>
        {loaded && <span className="text-sm text-muted">{bn(items.length)}{limit === UNLIMITED ? "" : ` / ${bn(limit ?? 0)}`}টি ছবি</span>}
        {loaded && room === 0 && <UpgradeLink>আরও ছবির জন্য আপগ্রেড করুন</UpgradeLink>}
        <FilePicker multiple disabled={busy || save.isPending || room === 0} onFiles={add}>
          <ImagePlus className="size-4" aria-hidden />{busy ? "আপলোড হচ্ছে..." : "ছবি যোগ করুন"}
        </FilePicker>
      </div>
      <p className="-mt-2 text-sm text-muted">আপনার কাজের ছবি, দোকান বা টিমের ছবি দিন। JPG, PNG বা WebP, প্রতিটি সর্বোচ্চ ৫ এমবি।</p>
      <ErrorBox error={save.error} />
      {items.length === 0 ? (
        <EmptyState icon={<ImagePlus aria-hidden />} title="গ্যালারিতে এখনও ছবি নেই" description="ভালো ছবি গ্রাহকের আস্থা বাড়ায়।" />
      ) : (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {items.map((g, i) => (
            <li key={g.imageUrl} className="flex flex-col gap-2 rounded-md border border-line p-2">
              <img src={g.imageUrl} alt={g.caption ?? ""} className="aspect-square w-full rounded object-cover" />
              <input className="input min-h-9 text-sm" maxLength={150} placeholder="ক্যাপশন (ঐচ্ছিক)" defaultValue={g.caption ?? ""} aria-label="ক্যাপশন"
                onBlur={(e) => { const caption = e.target.value.trim() || null; if (caption !== g.caption) commit(items.map((x, j) => (j === i ? { ...x, caption } : x)), "ক্যাপশন সংরক্ষিত হয়েছে।"); }} />
              <div className="flex">
                <Button size="sm" variant="ghost" disabled={i === 0 || save.isPending} onClick={() => move(i, -1)} aria-label="আগে নিন"><ArrowLeft className="size-4" aria-hidden /></Button>
                <Button size="sm" variant="ghost" disabled={i === items.length - 1 || save.isPending} onClick={() => move(i, 1)} aria-label="পরে নিন"><ArrowRight className="size-4" aria-hidden /></Button>
                <Button size="sm" variant="ghost" className="ml-auto text-red-700" disabled={save.isPending} onClick={() => commit(items.filter((_, j) => j !== i), "ছবিটি সরানো হয়েছে।")} aria-label="ছবি সরান"><Trash2 className="size-4" aria-hidden /></Button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

// ---------- FAQs & highlights ----------

function ContentTab({ page }: { page: PageSettings }) {
  const [faqs, setFaqs] = useState<Faq[]>(page.faqs);
  const [highlights, setHighlights] = useState<Highlight[]>(page.highlights);
  const saveFaqs = useSave<Faq[]>("page/faqs", "put", "প্রশ্নোত্তর সংরক্ষিত হয়েছে।");
  const saveHighlights = useSave<Highlight[]>("page/highlights", "put", "বিশেষত্ব সংরক্ষিত হয়েছে।");
  const setFaq = (i: number, patch: Partial<Faq>) => setFaqs(faqs.map((f, j) => (j === i ? { ...f, ...patch } : f)));
  const setHl = (i: number, patch: Partial<Highlight>) => setHighlights(highlights.map((h, j) => (j === i ? { ...h, ...patch } : h)));

  return (
    <div className="flex flex-col gap-5">
      <section className="card flex flex-col gap-4 p-6">
        <h2 className="text-base font-semibold">কেন আমাদের বেছে নেবেন</h2>
        <p className="-mt-2 text-sm text-muted">আপনার সেবার বিশেষ দিকগুলো, সর্বোচ্চ ৬টি। যেমন: “৩০ দিনের ওয়ারেন্টি”, “প্রশিক্ষিত টেকনিশিয়ান”।</p>
        <ErrorBox error={saveHighlights.error} />
        {highlights.map((h, i) => (
          <div key={i} className="flex flex-col gap-2 rounded-md bg-bg p-3">
            <div className="flex flex-wrap gap-1" role="radiogroup" aria-label="আইকন">
              {Object.entries(HIGHLIGHT_ICONS).map(([k, { icon: Icon, label }]) => (
                <button key={k} role="radio" aria-checked={h.iconKey === k} aria-label={label} title={label} onClick={() => setHl(i, { iconKey: k })}
                  className={`flex size-10 items-center justify-center rounded-md border ${h.iconKey === k ? "border-primary bg-primary-light text-primary" : "border-transparent text-muted hover:bg-zinc-100"}`}>
                  <Icon className="size-5" aria-hidden />
                </button>
              ))}
            </div>
            <div className="flex flex-wrap gap-2">
              <input className="input min-w-48 flex-1" maxLength={60} value={h.title} onChange={(e) => setHl(i, { title: e.target.value })} placeholder="শিরোনাম" aria-label="শিরোনাম" />
              <Button variant="ghost" onClick={() => setHighlights(highlights.filter((_, j) => j !== i))} aria-label="বিশেষত্ব মুছুন"><Trash2 className="size-4" aria-hidden /></Button>
            </div>
            <input className="input" maxLength={160} value={h.description ?? ""} onChange={(e) => setHl(i, { description: e.target.value || null })} placeholder="এক লাইনের ব্যাখ্যা (ঐচ্ছিক)" aria-label="ব্যাখ্যা" />
          </div>
        ))}
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" disabled={highlights.length >= 6} onClick={() => setHighlights([...highlights, { id: null, iconKey: "shield", title: "", description: null }])}><Plus className="size-4" aria-hidden />বিশেষত্ব যোগ করুন</Button>
          <Button loading={saveHighlights.isPending} disabled={highlights.some((h) => h.title.trim().length < 2)} onClick={() => saveHighlights.mutate(highlights)}>সংরক্ষণ করুন</Button>
        </div>
      </section>

      <section className="card flex flex-col gap-4 p-6">
        <h2 className="text-base font-semibold">সাধারণ প্রশ্নোত্তর</h2>
        <p className="-mt-2 text-sm text-muted">গ্রাহকরা প্রায়ই যা জিজ্ঞেস করেন: ওয়ারেন্টি, পেমেন্ট, কোন এলাকায় যান ইত্যাদি।</p>
        <ErrorBox error={saveFaqs.error} />
        {faqs.map((f, i) => (
          <div key={i} className="flex flex-col gap-2 rounded-md bg-bg p-3">
            <div className="flex gap-2">
              <input className="input flex-1" maxLength={200} value={f.question} onChange={(e) => setFaq(i, { question: e.target.value })} placeholder="প্রশ্ন" aria-label="প্রশ্ন" />
              <Button variant="ghost" onClick={() => setFaqs(faqs.filter((_, j) => j !== i))} aria-label="প্রশ্ন মুছুন"><Trash2 className="size-4" aria-hidden /></Button>
            </div>
            <textarea className="input" rows={2} maxLength={1000} value={f.answer} onChange={(e) => setFaq(i, { answer: e.target.value })} placeholder="উত্তর" aria-label="উত্তর" />
          </div>
        ))}
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" disabled={faqs.length >= 20} onClick={() => setFaqs([...faqs, { id: null, question: "", answer: "" }])}><Plus className="size-4" aria-hidden />প্রশ্ন যোগ করুন</Button>
          <Button loading={saveFaqs.isPending} disabled={faqs.some((f) => f.question.trim().length < 2 || !f.answer.trim())} onClick={() => saveFaqs.mutate(faqs)}>সংরক্ষণ করুন</Button>
        </div>
      </section>
    </div>
  );
}

// ---------- team ----------

function TeamTab({ page }: { page: PageSettings }) {
  const [shown, setShown] = useState(() => new Set(page.team.filter((m) => m.showOnProfile).map((m) => m.memberId)));
  const save = useSave<{ memberIds: string[] }>("page/team", "put", "টিম সেকশন সংরক্ষিত হয়েছে।");
  const toggle = (id: string) => { const next = new Set(shown); if (next.has(id)) next.delete(id); else next.add(id); setShown(next); };
  return (
    <section className="card flex flex-col gap-4 p-6">
      <h2 className="text-base font-semibold">পাবলিক পেজে টিম</h2>
      <p className="-mt-2 text-sm text-muted">যাঁদের বাছাই করবেন, পাবলিক পেজে শুধু তাঁদের নাম, পদবি ও ছবি দেখাবে। ফোন নম্বর কখনও দেখাবে না।</p>
      <ErrorBox error={save.error} />
      <ul className="flex flex-col divide-y divide-zinc-100 rounded-md border border-line">
        {page.team.map((m) => (
          <li key={m.memberId}>
            <label className="flex min-h-14 items-center gap-3 px-3">
              <input type="checkbox" className="size-5 accent-primary" checked={shown.has(m.memberId)} onChange={() => toggle(m.memberId)} />
              <span className="mr-auto"><span className="font-medium">{m.name}</span>{m.title && <span className="text-muted"> · {m.title}</span>}</span>
              <Badge>{m.roleName}</Badge>
            </label>
          </li>
        ))}
      </ul>
      <div><Button loading={save.isPending} onClick={() => save.mutate({ memberIds: [...shown] })}>সংরক্ষণ করুন</Button></div>
    </section>
  );
}

// ---------- preview ----------

/**
 * Full-screen preview of the page as customers see it. The page loads in an iframe so the mobile view uses the
 * real mobile layout (media queries follow the frame width). Shows saved changes only.
 */
function PreviewOverlay({ onClose }: { onClose: () => void }) {
  const biz = useBiz()!;
  const [device, setDevice] = useState<"desktop" | "mobile">(() => (window.innerWidth < 768 ? "mobile" : "desktop"));
  const [reload, setReload] = useState(0);
  const src = `/preview/${biz.id}`;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { window.removeEventListener("keydown", onKey); document.body.style.overflow = overflow; };
  }, [onClose]);

  return (
    <div role="dialog" aria-modal="true" aria-label="পেজের প্রিভিউ" className="fixed inset-0 z-50 flex flex-col bg-zinc-800">
      <div className="flex flex-wrap items-center gap-2 border-b border-line bg-surface px-3 py-2 sm:px-4">
        <div className="mr-auto min-w-0">
          <div className="font-semibold">প্রিভিউ: গ্রাহকরা যেভাবে দেখবেন</div>
          <div className="text-xs text-subtle">
            {biz.status === "published" ? "শুধু সংরক্ষিত পরিবর্তন দেখা যায়।" : "পেজটি এখনও প্রকাশিত হয়নি। যাচাইয়ের পর গ্রাহকরা এভাবে দেখবেন।"}
          </div>
        </div>
        <Segmented label="ডিভাইস" value={device} onChange={setDevice} options={[{ value: "desktop", label: "ডেস্কটপ" }, { value: "mobile", label: "মোবাইল" }]} />
        <Button size="sm" variant="ghost" onClick={() => setReload((n) => n + 1)} aria-label="প্রিভিউ রিফ্রেশ করুন" title="রিফ্রেশ"><RotateCw className="size-4" aria-hidden /></Button>
        <a href={src} target="_blank" rel="noopener" className="inline-flex size-10 items-center justify-center rounded-md text-zinc-600 hover:bg-zinc-100 hover:text-ink" aria-label="নতুন ট্যাবে খুলুন" title="নতুন ট্যাবে খুলুন"><ExternalLink className="size-4" aria-hidden /></a>
        <Button size="sm" variant="outline" onClick={onClose}><X className="size-4" aria-hidden />বন্ধ করুন</Button>
      </div>
      <div className="flex min-h-0 flex-1 items-start justify-center overflow-auto p-0 sm:p-4">
        {device === "desktop" ? (
          <iframe key={reload} src={src} title="ডেস্কটপ প্রিভিউ" className="size-full min-h-full bg-bg sm:rounded-md" />
        ) : (
          <div className="flex h-full max-h-[860px] w-[406px] max-w-full shrink-0 flex-col rounded-[32px] border-8 border-zinc-950 bg-zinc-950 shadow-pop">
            <iframe key={reload} src={src} title="মোবাইল প্রিভিউ" className="min-h-0 w-full flex-1 rounded-[24px] bg-bg" />
          </div>
        )}
      </div>
    </div>
  );
}
