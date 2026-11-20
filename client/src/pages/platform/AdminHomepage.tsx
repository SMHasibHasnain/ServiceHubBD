import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, ExternalLink, Eye, EyeOff, PanelTop, Plus, RotateCcw, Save, Trash2 } from "lucide-react";
import { api } from "../../api/client";
import { Badge, Button, ErrorBox, Field, LoadingList, Modal, PageHeader, useToast } from "../../components/ui";
import { bn } from "../../lib/format";
import {
  CUSTOM_TYPES, HOME_QUERY_KEY, SECTION_DEFS, SITE_FIELDS, emptyFrom, mergeHome, newSection, safeHref,
  type Data, type FieldDef, type HomeContent, type Section, type SiteContentResponse,
} from "../../lib/homeContent";
import { HomeView } from "../public/Landing";

const SITE = "site";

/**
 * Super-admin homepage editor (/admin/homepage): every text, the order of sections, which are shown, extra custom
 * sections, and the header/footer text. Changes stay local until saved; "প্রিভিউ" shows the unsaved draft.
 */
export default function AdminHomepage() {
  const qc = useQueryClient();
  const toast = useToast();
  const saved = useQuery({ queryKey: HOME_QUERY_KEY, queryFn: () => api.get<SiteContentResponse>("/api/site/home") });
  const [draft, setDraft] = useState<HomeContent | null>(null);
  const [selected, setSelected] = useState<string>("hero");
  const [dirty, setDirty] = useState(false);
  const [view, setView] = useState<"edit" | "preview">("edit");
  const [confirmReset, setConfirmReset] = useState(false);
  const [removing, setRemoving] = useState<Section | null>(null);

  useEffect(() => {
    if (saved.data && !draft) setDraft(mergeHome(saved.data.content));
  }, [saved.data, draft]);

  // Warn before leaving with unsaved edits.
  useEffect(() => {
    if (!dirty) return;
    const warn = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  const save = useMutation({
    mutationFn: () => api.put<SiteContentResponse>("/api/admin/site/home", { content: draft }),
    onSuccess: (res) => { qc.setQueryData(HOME_QUERY_KEY, res); setDirty(false); toast("হোমপেজ সংরক্ষণ হয়েছে। এখন সবাই নতুন লেখা দেখবে।"); },
  });
  const reset = useMutation({
    mutationFn: () => api.del("/api/admin/site/home"),
    onSuccess: () => {
      qc.setQueryData(HOME_QUERY_KEY, { key: "home", content: null, updatedAt: null } satisfies SiteContentResponse);
      setDraft(mergeHome(null)); setSelected("hero"); setDirty(false); setConfirmReset(false);
      toast("হোমপেজ আগের মূল লেখায় ফিরেছে।");
    },
  });

  if (saved.isLoading || (!draft && !saved.error)) return <><PageHeader title="হোমপেজ সম্পাদনা" /><LoadingList rows={4} /></>;
  if (!draft) return <><PageHeader title="হোমপেজ সম্পাদনা" /><ErrorBox error={saved.error} /></>;

  const change = (next: HomeContent) => { setDraft(next); setDirty(true); };
  const setSection = (id: string, patch: Partial<Section>) =>
    change({ ...draft, sections: draft.sections.map((s) => (s.id === id ? { ...s, ...patch } : s)) });
  const move = (i: number, by: -1 | 1) => {
    const next = [...draft.sections];
    [next[i], next[i + by]] = [next[i + by], next[i]];
    change({ ...draft, sections: next });
  };
  const add = (type: (typeof CUSTOM_TYPES)[number]) => {
    const s = newSection(type);
    change({ ...draft, sections: [...draft.sections, s] });
    setSelected(s.id);
  };
  const remove = (s: Section) => {
    change({ ...draft, sections: draft.sections.filter((x) => x.id !== s.id) });
    if (selected === s.id) setSelected("hero");
    setRemoving(null);
  };

  const current = draft.sections.find((s) => s.id === selected);
  const updatedAt = saved.data?.updatedAt;

  return (
    <>
      <PageHeader
        title="হোমপেজ সম্পাদনা"
        subtitle={<>লেখা, সেকশনের ক্রম আর কোনটি দেখা যাবে, সব এখান থেকে। সংরক্ষণ করলেই সবাই দেখবে।{updatedAt ? "" : " এখন মূল (ডিফল্ট) লেখা দেখানো হচ্ছে।"}</>}
        actions={<>
          <a href="/" target="_blank" rel="noopener" className="inline-flex min-h-10 items-center gap-2 rounded-md border border-zinc-300 bg-surface px-3.5 text-sm font-semibold text-ink shadow-soft hover:bg-zinc-50">
            <ExternalLink className="size-4" aria-hidden />হোমপেজ খুলুন
          </a>
          <Button size="sm" variant="outline" onClick={() => setConfirmReset(true)}><RotateCcw className="size-4" aria-hidden />মূল লেখায় ফেরান</Button>
          <Button size="sm" onClick={() => save.mutate()} loading={save.isPending} disabled={!dirty}><Save className="size-4" aria-hidden />{dirty ? "সংরক্ষণ করুন" : "সংরক্ষিত"}</Button>
        </>}
      />
      <ErrorBox error={save.error} />

      <div role="tablist" aria-label="দেখার ধরন" className="mb-5 inline-flex rounded-md border border-line bg-surface p-1">
        {([["edit", "সম্পাদনা"], ["preview", "প্রিভিউ"]] as const).map(([v, label]) => (
          <button key={v} role="tab" aria-selected={view === v} onClick={() => setView(v)}
            className={`min-h-9 rounded px-4 text-sm font-semibold ${view === v ? "bg-primary text-white" : "text-muted hover:text-ink"}`}>{label}</button>
        ))}
      </div>

      {view === "preview" ? (
        <div className="overflow-hidden rounded-lg border border-line shadow-card">
          <p className="border-b border-line bg-amber-50 px-4 py-2 text-sm text-amber-900">{dirty ? "এটি অসংরক্ষিত খসড়া। সংরক্ষণ না করা পর্যন্ত সাইটে দেখাবে না।" : "সাইটে এখন যা দেখানো হচ্ছে।"}</p>
          <HomeView content={draft} />
        </div>
      ) : (
        <div className="grid items-start gap-6 lg:grid-cols-[320px_minmax(0,1fr)]">
          <nav aria-label="সেকশন" className="card flex flex-col gap-1 p-3 lg:sticky lg:top-20">
            <button type="button" onClick={() => setSelected(SITE)} aria-current={selected === SITE}
              className={`flex min-h-11 items-center gap-2 rounded-md px-3 text-left text-sm font-semibold ${selected === SITE ? "bg-primary-light text-primary" : "hover:bg-zinc-100"}`}>
              <PanelTop className="size-4" aria-hidden />হেডার ও ফুটার
            </button>
            <p className="mt-3 mb-1 px-3 text-xs font-semibold text-subtle">সেকশন (উপর থেকে নিচে)</p>
            <ol className="flex flex-col gap-1">
              {draft.sections.map((s, i) => {
                const def = SECTION_DEFS[s.type];
                const title = def.custom && typeof s.data.title === "string" && s.data.title.trim() ? s.data.title : def.custom && typeof s.data.text === "string" ? s.data.text : "";
                return (
                  <li key={s.id} className={`flex items-center gap-1 rounded-md ${selected === s.id ? "bg-primary-light" : "hover:bg-zinc-50"}`}>
                    <button type="button" onClick={() => setSelected(s.id)} aria-current={selected === s.id}
                      className={`min-h-11 min-w-0 flex-1 px-3 py-1.5 text-left text-sm ${selected === s.id ? "font-semibold text-primary" : ""} ${s.visible ? "" : "text-subtle"}`}>
                      <span className="block truncate">{def.label}</span>
                      {title && <span className="block truncate text-xs text-subtle">{title}</span>}
                      {!s.visible && <Badge tone="neutral" className="mt-0.5 text-[11px]">লুকানো</Badge>}
                    </button>
                    <IconBtn label={s.visible ? `${def.label} লুকান` : `${def.label} দেখান`} onClick={() => setSection(s.id, { visible: !s.visible })}>
                      {s.visible ? <Eye className="size-4" aria-hidden /> : <EyeOff className="size-4" aria-hidden />}
                    </IconBtn>
                    <IconBtn label={`${def.label} উপরে নিন`} disabled={i === 0} onClick={() => move(i, -1)}><ArrowUp className="size-4" aria-hidden /></IconBtn>
                    <IconBtn label={`${def.label} নিচে নিন`} disabled={i === draft.sections.length - 1} onClick={() => move(i, 1)}><ArrowDown className="size-4" aria-hidden /></IconBtn>
                    {def.custom && <IconBtn label={`${def.label} মুছুন`} onClick={() => setRemoving(s)}><Trash2 className="size-4 text-red-700" aria-hidden /></IconBtn>}
                  </li>
                );
              })}
            </ol>
            <div className="mt-3 border-t border-line pt-3">
              <p className="mb-2 px-3 text-xs font-semibold text-subtle">নতুন সেকশন যোগ করুন</p>
              <div className="flex flex-col gap-1">
                {CUSTOM_TYPES.map((t) => (
                  <button key={t} type="button" onClick={() => add(t)}
                    className="flex min-h-10 items-start gap-2 rounded-md px-3 py-2 text-left text-sm hover:bg-zinc-100">
                    <Plus className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden />
                    <span><span className="block font-semibold">{SECTION_DEFS[t].label}</span><span className="block text-xs text-subtle">{SECTION_DEFS[t].description}</span></span>
                  </button>
                ))}
              </div>
            </div>
          </nav>

          <section className="card min-w-0 p-5 sm:p-6" aria-live="polite">
            {selected === SITE ? (
              <>
                <h2 className="text-xl font-semibold">হেডার ও ফুটার</h2>
                <p className="mt-1 mb-5 text-sm text-muted">সব পাবলিক পেজের উপরের মেনু আর নিচের ফুটারের লেখা।</p>
                <Fields fields={SITE_FIELDS} value={draft.site} onChange={(site) => change({ ...draft, site })} />
              </>
            ) : current ? (
              <>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <h2 className="text-xl font-semibold">{SECTION_DEFS[current.type].label}</h2>
                    <p className="mt-1 text-sm text-muted">{SECTION_DEFS[current.type].description}</p>
                  </div>
                  <label className="flex min-h-10 items-center gap-2 text-sm font-medium">
                    <input type="checkbox" className="size-5 accent-primary" checked={current.visible} onChange={(e) => setSection(current.id, { visible: e.target.checked })} />
                    হোমপেজে দেখান
                  </label>
                </div>
                <p className="mt-2 mb-5 text-sm text-subtle">কোনো লেখা খালি রাখলে সেটি পেজে দেখাবে না।</p>
                <Fields fields={SECTION_DEFS[current.type].fields} value={current.data} onChange={(data) => setSection(current.id, { data })} />
              </>
            ) : null}
          </section>
        </div>
      )}

      <Modal open={confirmReset} onClose={() => setConfirmReset(false)} title="মূল লেখায় ফেরাবেন?"
        footer={<><Button variant="ghost" onClick={() => setConfirmReset(false)}>বাতিল</Button><Button variant="danger" loading={reset.isPending} onClick={() => reset.mutate()}>হ্যাঁ, ফেরান</Button></>}>
        <ErrorBox error={reset.error} />
        <p className="text-muted">আপনার সব পরিবর্তন, যোগ করা সেকশন আর ক্রম মুছে যাবে। হোমপেজ শুরুর লেখায় ফিরে যাবে। এটি ফেরানো যায় না।</p>
      </Modal>
      <Modal open={!!removing} onClose={() => setRemoving(null)} title="সেকশনটি মুছবেন?"
        footer={<><Button variant="ghost" onClick={() => setRemoving(null)}>বাতিল</Button><Button variant="danger" onClick={() => removing && remove(removing)}>মুছুন</Button></>}>
        <p className="text-muted">“{removing ? SECTION_DEFS[removing.type].label : ""}” সেকশন আর এর সব লেখা মুছে যাবে। সংরক্ষণ না করা পর্যন্ত সাইটে কিছু বদলাবে না।</p>
      </Modal>
    </>
  );
}

function IconBtn({ label, onClick, disabled, children }: { label: string; onClick: () => void; disabled?: boolean; children: React.ReactNode }) {
  return (
    <button type="button" aria-label={label} title={label} onClick={onClick} disabled={disabled}
      className="flex size-9 shrink-0 items-center justify-center rounded-md text-muted hover:bg-white hover:text-ink disabled:opacity-30 disabled:hover:bg-transparent">
      {children}
    </button>
  );
}

// ---------- Schema-driven form ----------

function Fields({ fields, value, onChange }: { fields: FieldDef[]; value: Data; onChange: (v: Data) => void }) {
  return (
    <div className="flex flex-col gap-4">
      {fields.map((f) => <FieldEditor key={f.key} f={f} value={value[f.key]} onChange={(v) => onChange({ ...value, [f.key]: v })} />)}
    </div>
  );
}

function FieldEditor({ f, value, onChange }: { f: FieldDef; value: Data[string] | undefined; onChange: (v: Data[string]) => void }) {
  const s = typeof value === "string" ? value : "";
  switch (f.kind) {
    case "text":
      return <Field label={f.label} hint={f.hint}>{(id) => <input id={id} className="input" value={s} maxLength={2000} onChange={(e) => onChange(e.target.value)} />}</Field>;
    case "textarea":
      return <Field label={f.label} hint={f.hint}>{(id) => <textarea id={id} className="input" rows={3} value={s} maxLength={2000} onChange={(e) => onChange(e.target.value)} />}</Field>;
    case "link": {
      const bad = s.trim() !== "" && !safeHref(s);
      return (
        <Field label={f.label} hint={f.hint} error={bad ? "এই লিংক কাজ করবে না। / বা https:// দিয়ে শুরু করুন।" : undefined}>
          {(id) => <input id={id} className="input font-num" dir="ltr" value={s} placeholder="/search?q=..." onChange={(e) => onChange(e.target.value)} />}
        </Field>
      );
    }
    case "select":
      return (
        <Field label={f.label}>{(id) => (
          <select id={id} className="input" value={s} onChange={(e) => onChange(e.target.value)}>
            {f.options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        )}</Field>
      );
    case "group":
      return (
        <fieldset className="rounded-lg border border-line p-4">
          <legend className="px-1.5 font-semibold">{f.label}</legend>
          <Fields fields={f.fields} value={value && typeof value === "object" && !Array.isArray(value) ? value : {}} onChange={onChange} />
        </fieldset>
      );
    case "list":
      return <ListEditor f={f} items={Array.isArray(value) ? value : []} onChange={onChange} />;
  }
}

function ListEditor({ f, items, onChange }: { f: Extract<FieldDef, { kind: "list" }>; items: Data[]; onChange: (v: Data[]) => void }) {
  const [open, setOpen] = useState<number | null>(null);
  const max = f.max ?? 30;
  const move = (i: number, by: -1 | 1) => {
    const next = [...items];
    [next[i], next[i + by]] = [next[i + by], next[i]];
    onChange(next);
    setOpen(i + by);
  };
  return (
    <fieldset className="rounded-lg border border-line p-4">
      <legend className="px-1.5 font-semibold">{f.label} <span className="font-normal text-subtle">({bn(items.length)}/{bn(max)})</span></legend>
      <ol className="flex flex-col gap-2">
        {items.map((item, i) => {
          const title = typeof item[f.titleKey] === "string" && (item[f.titleKey] as string).trim() ? item[f.titleKey] as string : "(খালি)";
          const isOpen = open === i;
          return (
            <li key={i} className="rounded-md border border-line bg-bg">
              <div className="flex items-center gap-1 pr-1">
                <button type="button" onClick={() => setOpen(isOpen ? null : i)} aria-expanded={isOpen}
                  className="min-h-11 min-w-0 flex-1 truncate px-3 text-left text-sm">
                  <span className="text-subtle">{f.itemLabel} {bn(i + 1)}:</span> <span className="font-medium">{title}</span>
                </button>
                <IconBtn label={`${f.itemLabel} ${bn(i + 1)} উপরে নিন`} disabled={i === 0} onClick={() => move(i, -1)}><ArrowUp className="size-4" aria-hidden /></IconBtn>
                <IconBtn label={`${f.itemLabel} ${bn(i + 1)} নিচে নিন`} disabled={i === items.length - 1} onClick={() => move(i, 1)}><ArrowDown className="size-4" aria-hidden /></IconBtn>
                <IconBtn label={`${f.itemLabel} ${bn(i + 1)} মুছুন`} onClick={() => { onChange(items.filter((_, j) => j !== i)); setOpen(null); }}><Trash2 className="size-4 text-red-700" aria-hidden /></IconBtn>
              </div>
              {isOpen && (
                <div className="border-t border-line bg-surface p-4">
                  <Fields fields={f.fields} value={item} onChange={(v) => onChange(items.map((x, j) => (j === i ? v : x)))} />
                </div>
              )}
            </li>
          );
        })}
      </ol>
      <Button type="button" size="sm" variant="outline" className="mt-3" disabled={items.length >= max}
        onClick={() => { onChange([...items, emptyFrom(f.fields)]); setOpen(items.length); }}>
        <Plus className="size-4" aria-hidden />{f.itemLabel} যোগ করুন
      </Button>
    </fieldset>
  );
}
