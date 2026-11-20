import { useState } from "react";
import { Link, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Check, ChevronDown, ChevronUp, ExternalLink, Eye, EyeOff, Image as ImageIcon, RotateCcw } from "lucide-react";
import { api } from "../../api/client";
import type { HeaderStyle, PageDefaults, PageSection, Paged, StaffAction, StaffActionDetail, StaffBusiness, ThemeKey } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Badge, Button, EmptyState, ErrorBox, LinkButton, LoadingList, Modal, PAGE_SIZE, PageHeader, Pagination, Segmented, Skeleton, usePage, useToast } from "../../components/ui";
import { BUSINESS_STATUS, VERIFICATION_STATUS, bn, dateTime, relative } from "../../lib/format";
import { HEADER_STYLES, SECTIONS, THEMES } from "../../lib/page";

/** Permissions from the person's platform workspaces (moderation, administration, …). */
function usePlatformPermissions() {
  const { workspaces } = useAuth();
  const set = new Set(workspaces.filter((w) => w.type !== "customer" && w.type !== "business").flatMap((w) => w.permissions));
  return (p: string) => set.has(p);
}

// ---------- All businesses ----------

type StatusFilter = "all" | "published" | "draft" | "suspended" | "deleted";

/** Every business in the person's scope. Staff open one to edit it through its own workspace pages. */
export function AllBusinesses() {
  const can = usePlatformPermissions();
  const [q, setQ] = useState("");
  const [filter, setFilter] = useState<StatusFilter>("all");
  const [page, setPage] = usePage({ q, filter });
  const deleted = filter === "deleted";
  const list = useQuery({
    queryKey: ["staff", "businesses", q, filter, page],
    queryFn: () => api.get<Paged<StaffBusiness>>("/api/staff/businesses", {
      q, page, pageSize: PAGE_SIZE, deleted, status: filter === "all" || deleted ? undefined : filter,
    }),
    placeholderData: keepPreviousData,
  });
  const canOpen = can("businesses.edit") || can("businesses.team") || can("businesses.view");
  const options: { value: StatusFilter; label: string }[] = [
    { value: "all", label: "সব" }, { value: "published", label: "প্রকাশিত" }, { value: "draft", label: "খসড়া" },
    { value: "suspended", label: "স্থগিত" },
    ...(can("businesses.undo") ? [{ value: "deleted" as const, label: "মুছে ফেলা" }] : []),
  ];

  return (
    <>
      <PageHeader title="সব ব্যবসা" subtitle={can("businesses.edit")
        ? "যেকোনো ব্যবসা খুলে তার সেটিংস, পেজ ও সেবা বদলাতে পারবেন। প্রতিটি পরিবর্তন কর্মীদের লগে থাকে।"
        : "সব ব্যবসার তালিকা ও বিশ্লেষণ, শুধু দেখার জন্য"} />
      <div className="mb-5 flex flex-wrap items-center gap-3">
        <input className="input max-w-sm" value={q} onChange={(e) => setQ(e.target.value)} placeholder="ব্যবসা বা মালিকের নাম" aria-label="ব্যবসা খুঁজুন" />
        <Segmented label="অবস্থা" value={filter} onChange={setFilter} options={options} />
      </div>
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : !list.data?.items.length ? (
        <EmptyState title={q ? "এই নামে কোনো ব্যবসা পাওয়া যায়নি" : "কোনো ব্যবসা নেই"} />
      ) : (
        <div className="card overflow-x-auto">
          <table className="w-full min-w-[860px] text-left text-[15px]">
            <thead className="border-b border-line text-sm text-subtle">
              <tr>
                <th className="px-5 py-3 font-medium">ব্যবসা</th>
                <th className="px-3 py-3 font-medium">মালিক</th>
                <th className="px-3 py-3 font-medium">অবস্থা</th>
                <th className="px-3 py-3 font-medium">{deleted ? "মুছে ফেলা হয়েছে" : "শেষ কর্মী-পরিবর্তন"}</th>
                <th className="px-5 py-3"><span className="sr-only">কাজ</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-100">
              {list.data.items.map((b) => (
                <tr key={b.id}>
                  <td className="px-5 py-3">
                    <div className="font-medium">{b.name}</div>
                    <div className="text-sm text-subtle">{b.categoryName}{b.zoneName && ` · ${b.zoneName}`}</div>
                  </td>
                  <td className="px-3 py-3">{b.ownerName}</td>
                  <td className="px-3 py-3">
                    <div className="flex flex-wrap gap-1">
                      <Badge tone={BUSINESS_STATUS[b.status].tone}>{BUSINESS_STATUS[b.status].label}</Badge>
                      <Badge tone={VERIFICATION_STATUS[b.verificationStatus].tone}>{VERIFICATION_STATUS[b.verificationStatus].label}</Badge>
                    </div>
                  </td>
                  <td className="px-3 py-3 text-sm text-muted">
                    {deleted && b.deletedAt ? dateTime(b.deletedAt) : b.lastStaffEditAt ? relative(b.lastStaffEditAt) : "—"}
                  </td>
                  <td className="px-5 py-3">
                    <div className="flex justify-end gap-2">
                      {deleted ? (
                        can("audit.view") && <LinkButton size="sm" variant="outline" to={`/admin/staff-log?businessId=${b.id}`}>লগ দেখুন</LinkButton>
                      ) : (
                        <>
                          {b.status === "published" && (
                            <a href={`/b/${b.slug}`} target="_blank" rel="noopener noreferrer" className="inline-flex size-10 items-center justify-center rounded-md text-subtle transition-colors duration-150 hover:bg-zinc-100 hover:text-ink" aria-label={`${b.name}-এর পাবলিক পেজ`}>
                              <ExternalLink className="size-4" aria-hidden />
                            </a>
                          )}
                          {canOpen && <LinkButton size="sm" variant="outline" to={`/w/${b.id}`}>{can("businesses.edit") ? "খুলুন ও সম্পাদনা" : "খুলুন"}</LinkButton>}
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {list.data && <Pagination page={page} total={list.data.total} onChange={setPage} />}
    </>
  );
}

// ---------- Staff edit log ----------

type RoleFilter = "system_moderator" | "admin" | "super_admin" | "all";

const KIND: Record<string, { label: string; tone: "green" | "red" | "blue" }> = {
  added: { label: "নতুন", tone: "green" },
  removed: { label: "মুছে ফেলা", tone: "red" },
  updated: { label: "বদলানো", tone: "blue" },
};

/** Every edit platform staff made to businesses. Moderators' edits first, since admins review those. */
export function StaffLog() {
  const can = usePlatformPermissions();
  const [params, setParams] = useSearchParams();
  const businessId = params.get("businessId") ?? undefined;
  const [role, setRole] = useState<RoleFilter>(businessId ? "all" : "system_moderator");
  const [page, setPage] = usePage({ role, businessId });
  const [open, setOpen] = useState<number | null>(null);
  const [undoing, setUndoing] = useState<StaffAction | null>(null);
  const list = useQuery({
    queryKey: ["staff", "actions", role, businessId, page],
    queryFn: () => api.get<Paged<StaffAction>>("/api/staff/actions", { role: role === "all" ? undefined : role, businessId, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="কর্মীদের পরিবর্তন" subtitle="মডারেটর ও এ্যাডমিনরা কোন ব্যবসায় কী বদলেছেন। প্রয়োজনে এখান থেকে ফিরিয়ে নিন।" />
      <div className="mb-5 flex flex-wrap items-center gap-3">
        <Segmented label="কে বদলেছেন" value={role} onChange={setRole} options={[
          { value: "system_moderator", label: "মডারেটর" }, { value: "admin", label: "এ্যাডমিন" },
          { value: "super_admin", label: "সুপার এ্যাডমিন" }, { value: "all", label: "সবাই" },
        ]} />
        {businessId && (
          <Button size="sm" variant="ghost" onClick={() => { params.delete("businessId"); setParams(params); }}>
            একটি ব্যবসার লগ দেখাচ্ছে · সব দেখুন
          </Button>
        )}
      </div>
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : !list.data?.items.length ? (
        <EmptyState title="কোনো পরিবর্তন নেই" description="কর্মীরা কোনো ব্যবসা বদলালে এখানে দেখাবে।" />
      ) : (
        <ol className="flex flex-col gap-3">
          {list.data.items.map((a) => (
            <li key={a.id} className="card">
              <div className="flex flex-wrap items-start gap-x-4 gap-y-2 p-4">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                    <span className="font-medium">{a.summary}</span>
                    {a.undoneAt && <Badge tone="neutral">ফিরিয়ে নেওয়া হয়েছে</Badge>}
                    {a.undoOf && <Badge tone="violet">পূর্বাবস্থায় ফেরানো</Badge>}
                  </div>
                  <div className="mt-0.5 text-sm text-muted">
                    <Link to={`/w/${a.businessId}`} className="font-medium text-ink hover:text-primary">{a.businessName}</Link>
                    {" · "}{a.actorName} ({a.roleName}) · <span className="tabular-nums">{dateTime(a.createdAt)}</span>
                  </div>
                  {a.undoneAt && (
                    <div className="mt-1 text-sm text-subtle">
                      {a.undoneByName} ফিরিয়ে নিয়েছেন, {relative(a.undoneAt)}{a.undoReason && ` · কারণ: ${a.undoReason}`}
                    </div>
                  )}
                </div>
                <div className="flex gap-2">
                  <Button size="sm" variant="ghost" onClick={() => setOpen(open === a.id ? null : a.id)} aria-expanded={open === a.id}>
                    {open === a.id ? <ChevronUp className="size-4" aria-hidden /> : <ChevronDown className="size-4" aria-hidden />}বিস্তারিত
                  </Button>
                  {can("businesses.undo") && !a.undoneAt && (
                    <Button size="sm" variant="outline" onClick={() => setUndoing(a)}><RotateCcw className="size-4" aria-hidden />ফিরিয়ে নিন</Button>
                  )}
                </div>
              </div>
              {open === a.id && <ActionDetail id={a.id} />}
            </li>
          ))}
        </ol>
      )}
      {list.data && <Pagination page={page} total={list.data.total} onChange={setPage} />}
      {undoing && <UndoDialog action={undoing} onClose={() => setUndoing(null)} />}
    </>
  );
}

function ActionDetail({ id }: { id: number }) {
  const q = useQuery({ queryKey: ["staff", "action", id], queryFn: () => api.get<StaffActionDetail>(`/api/staff/actions/${id}`) });
  if (q.error) return <div className="border-t border-line p-4"><ErrorBox error={q.error} /></div>;
  if (!q.data) return <div className="border-t border-line p-4"><Skeleton className="h-24" /></div>;
  return (
    <div className="flex flex-col gap-4 border-t border-line p-4">
      {q.data.rows.map((r, i) => (
        <div key={i}>
          <div className="mb-2 flex flex-wrap items-center gap-2 text-sm">
            <Badge tone={KIND[r.kind].tone}>{KIND[r.kind].label}</Badge>
            <span className="font-medium">{r.entityLabel}</span>
            {r.title && <span className="text-muted">· {r.title}</span>}
          </div>
          {r.fields.length > 0 && (
            <div className="overflow-x-auto rounded-md border border-line">
              <table className="w-full min-w-[520px] text-left text-sm">
                <thead className="bg-zinc-50 text-subtle"><tr><th className="px-3 py-2 font-medium">ঘর</th><th className="px-3 py-2 font-medium">আগে</th><th className="px-3 py-2 font-medium">পরে</th></tr></thead>
                <tbody className="divide-y divide-zinc-100">
                  {r.fields.map((f) => (
                    <tr key={f.field}>
                      <td className="px-3 py-2 text-muted">{f.field}</td>
                      <td className="px-3 py-2 break-all">{f.before ?? <span className="text-subtle">—</span>}</td>
                      <td className="px-3 py-2 break-all">{f.after ?? <span className="text-subtle">—</span>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

function UndoDialog({ action, onClose }: { action: StaffAction; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [reason, setReason] = useState("");
  const undo = useMutation({
    mutationFn: () => api.post(`/api/staff/actions/${action.id}/undo`, { reason }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["staff"] });
      qc.invalidateQueries({ queryKey: ["business", action.businessId] });
      toast("পরিবর্তন ফিরিয়ে নেওয়া হয়েছে।");
      onClose();
    },
  });
  return (
    <Modal open onClose={onClose} title="পরিবর্তন ফিরিয়ে নেবেন?"
      footer={<><Button variant="ghost" onClick={onClose}>বাতিল</Button><Button loading={undo.isPending} onClick={() => undo.mutate()}>ফিরিয়ে নিন</Button></>}>
      <div className="flex flex-col gap-3">
        <p className="text-muted">
          “{action.businessName}”-এ {action.actorName}-এর করা পরিবর্তন ({action.summary}) আগের অবস্থায় ফিরবে।
          পরে কেউ একই অংশ আবার বদলে থাকলে ফিরিয়ে নেওয়া যাবে না।
        </p>
        <ErrorBox error={undo.error} />
        <label className="label">কারণ <span className="font-normal text-subtle">(ঐচ্ছিক)</span>
          <textarea className="input" rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </label>
      </div>
    </Modal>
  );
}

// ---------- Page defaults ----------

/** The platform's starting look for business pages: theme, header style and section order/visibility. */
export function PageDefaultsPage() {
  const q = useQuery({ queryKey: ["staff", "page-defaults"], queryFn: () => api.get<PageDefaults>("/api/staff/page-defaults") });
  return (
    <>
      <PageHeader title="পেজের ডিফল্ট" subtitle="নতুন ব্যবসা আর যেসব ব্যবসা এখনও নিজের থিম বা সেকশন বেছে নেয়নি, তাদের পেজ এভাবে দেখাবে। যারা নিজেরা বেছে নিয়েছে, তাদের পছন্দ বদলাবে না।" />
      <ErrorBox error={q.error} />
      {q.data ? <DefaultsForm value={q.data} /> : !q.error && <Skeleton className="h-96" />}
    </>
  );
}

function DefaultsForm({ value }: { value: PageDefaults }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [theme, setTheme] = useState<ThemeKey>(value.themeKey);
  const [header, setHeader] = useState<HeaderStyle>(value.headerStyle);
  const [sections, setSections] = useState<PageSection[]>(value.sections);
  const save = useMutation({
    mutationFn: () => api.put<PageDefaults>("/api/staff/page-defaults", { themeKey: theme, headerStyle: header, sections }),
    onSuccess: (d) => { qc.setQueryData(["staff", "page-defaults"], d); toast("ডিফল্ট সংরক্ষিত হয়েছে।"); },
  });
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
              className={`flex min-h-11 items-center gap-2 rounded-md border px-3 text-sm font-medium transition-colors duration-150 ${theme === k ? "border-ink ring-2 ring-ink/10" : "border-line hover:border-zinc-400"}`}>
              <span className="flex size-6 items-center justify-center rounded-full text-white" style={{ background: THEMES[k].accent }}>{theme === k && <Check className="size-3.5" aria-hidden />}</span>
              {THEMES[k].label}
            </button>
          ))}
        </div>
        <h3 className="mt-2 font-semibold">হেডারের ধরন</h3>
        <div role="radiogroup" aria-label="হেডারের ধরন" className="grid gap-3 sm:grid-cols-2">
          {(Object.keys(HEADER_STYLES) as HeaderStyle[]).map((k) => (
            <button key={k} role="radio" aria-checked={header === k} onClick={() => setHeader(k)}
              className={`flex flex-col gap-2 rounded-md border p-3 text-left transition-colors duration-150 ${header === k ? "border-ink ring-2 ring-ink/10" : "border-line hover:border-zinc-400"}`}>
              <span className="flex h-14 w-full items-center justify-center rounded text-zinc-400" style={{ background: k === "color" ? THEMES[theme].accent : "var(--color-zinc-200)" }} aria-hidden>
                {k !== "color" && <ImageIcon className="size-5" />}
              </span>
              <span className="font-medium">{HEADER_STYLES[k].label}</span>
              <span className="text-sm text-subtle">{HEADER_STYLES[k].hint}</span>
            </button>
          ))}
        </div>
      </section>

      <section className="card flex flex-col gap-4 p-6">
        <h2 className="text-base font-semibold">সেকশনের ক্রম</h2>
        <p className="-mt-2 text-sm text-muted">ব্যবসার পেজে কোন সেকশন আগে আসবে আর কোনটা শুরুতে লুকানো থাকবে।</p>
        <ol className="flex flex-col divide-y divide-zinc-100 rounded-md border border-line">
          {sections.map((s, i) => {
            const meta = SECTIONS[s.key];
            return (
              <li key={s.key} className={`flex min-h-14 items-center gap-2 px-3 ${s.visible ? "" : "bg-zinc-50 text-subtle"}`}>
                <span className="w-6 text-center text-sm text-subtle tabular-nums">{bn(i + 1)}</span>
                <span className="mr-auto font-medium">{meta.label}</span>
                {meta.hideable ? (
                  <Button size="sm" variant="ghost" aria-pressed={!s.visible}
                    onClick={() => setSections(sections.map((x) => x.key === s.key ? { ...x, visible: !x.visible } : x))}>
                    {s.visible ? <><Eye className="size-4" aria-hidden />দেখাচ্ছে</> : <><EyeOff className="size-4" aria-hidden />লুকানো</>}
                  </Button>
                ) : <span className="px-2 text-xs text-subtle">সবসময় দেখায়</span>}
                <Button size="sm" variant="ghost" disabled={i === 0} onClick={() => move(i, -1)} aria-label={`${meta.label} উপরে নিন`}><ArrowUp className="size-4" aria-hidden /></Button>
                <Button size="sm" variant="ghost" disabled={i === sections.length - 1} onClick={() => move(i, 1)} aria-label={`${meta.label} নিচে নিন`}><ArrowDown className="size-4" aria-hidden /></Button>
              </li>
            );
          })}
        </ol>
      </section>

      <ErrorBox error={save.error} />
      <div><Button loading={save.isPending} onClick={() => save.mutate()}>সংরক্ষণ করুন</Button></div>
    </div>
  );
}
