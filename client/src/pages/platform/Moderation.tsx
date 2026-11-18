import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ExternalLink, ShieldCheck } from "lucide-react";
import { api } from "../../api/client";
import type { Paged, QueueItem, ReportItem, VerificationDetail, VerificationStatus } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Avatar, Badge, Button, EmptyState, ErrorBox, LoadingList, Modal, PageHeader, Segmented, useToast } from "../../components/ui";
import { DOCUMENT_TYPES, MODERATION_ACTIONS, REPORT_REASONS, VERIFICATION_STATUS, bn, dateTime, relative } from "../../lib/format";

const PRIORITY = { low: { label: "নিম্ন", tone: "neutral" }, normal: { label: "সাধারণ", tone: "blue" }, high: { label: "উচ্চ", tone: "amber" }, urgent: { label: "জরুরি", tone: "red" } } as const;

export function ModerationQueue() {
  const [status, setStatus] = useState<"" | VerificationStatus>("");
  const list = useQuery({
    queryKey: ["moderation", "queue", status],
    queryFn: () => api.get<Paged<QueueItem>>("/api/moderation/verifications", { status, pageSize: 100 }),
  });
  return (
    <>
      <PageHeader title="ব্যবসা যাচাইকরণ" subtitle="আপনার এলাকা ও ক্যাটাগরির আবেদনগুলো — প্রতিটি সিদ্ধান্তে কারণ লিখতে হবে" />
      <div className="mb-5"><Segmented label="অবস্থা" value={status} onChange={setStatus}
        options={[{ value: "", label: "অপেক্ষমাণ" }, { value: "info_requested", label: "তথ্য চাওয়া" }, { value: "approved", label: "অনুমোদিত" }, { value: "rejected", label: "প্রত্যাখ্যাত" }]} /></div>
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <ul className="flex flex-col gap-3">
          {list.data.items.map((v) => (
            <li key={v.id}>
              <Link to={`/moderation/v/${v.id}`} className="card flex flex-wrap items-center gap-4 p-4 transition-colors hover:border-zinc-400">
                <Avatar name={v.businessName} className="size-11" />
                <div className="min-w-0 flex-[1_1_220px]">
                  <div className="font-semibold">{v.businessName} {v.attemptNo > 1 && <span className="text-sm font-normal text-subtle">(পুনরায় জমা #{bn(v.attemptNo)})</span>}</div>
                  <div className="text-[15px] text-muted">{v.ownerName} · {v.categoryName}{v.zoneName && ` · ${v.zoneName}`}</div>
                </div>
                <Badge tone={PRIORITY[v.priority].tone}>{PRIORITY[v.priority].label}</Badge>
                {v.requiresInspection && <Badge tone="amber">পরিদর্শন</Badge>}
                <Badge tone={VERIFICATION_STATUS[v.status].tone}>{VERIFICATION_STATUS[v.status].label}</Badge>
                <span className="text-sm text-subtle">{relative(v.submittedAt)}</span>
              </Link>
            </li>
          ))}
        </ul>
      ) : <EmptyState icon={<ShieldCheck className="size-8" aria-hidden />} title="কোনো অপেক্ষমাণ যাচাইকরণ নেই।" />}
    </>
  );
}

type Decision = "approve" | "reject" | "request-info" | "request-inspection";
const DECISION_TITLE: Record<Decision, string> = { approve: "অনুমোদন করুন", reject: "প্রত্যাখ্যান করুন", "request-info": "আরও তথ্য চান", "request-inspection": "মাঠ পরিদর্শনের অনুরোধ" };

export function VerificationReview() {
  const { id } = useParams();
  const { user } = useAuth();
  const qc = useQueryClient();
  const toast = useToast();
  const navigate = useNavigate();
  const [decision, setDecision] = useState<Decision | null>(null);
  const [reason, setReason] = useState("");

  const detail = useQuery({ queryKey: ["moderation", "verification", id], queryFn: () => api.get<VerificationDetail>(`/api/moderation/verifications/${id}`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ["moderation"] }); };
  const start = useMutation({ mutationFn: () => api.post(`/api/moderation/verifications/${id}/start`), onSuccess: () => { refresh(); toast("পর্যালোচনা শুরু হয়েছে।"); } });
  const decide = useMutation({
    mutationFn: (d: Decision) => api.post(`/api/moderation/verifications/${id}/${d}`, { reason }),
    onSuccess: (_, d) => {
      refresh(); setDecision(null); setReason("");
      toast(d === "approve" ? "ব্যবসাটি অনুমোদিত ও প্রকাশিত হয়েছে।" : d === "request-inspection" ? "পরিদর্শকের কাছে পাঠানো হয়েছে।" : "সিদ্ধান্ত জমা হয়েছে।");
      if (d !== "request-inspection") navigate("/moderation");
    },
  });

  const d = detail.data;
  if (detail.error) return <ErrorBox error={detail.error} />;
  if (!d) return <LoadingList />;
  const r = d.request;
  const mine = r.assignedModeratorId === user?.id;
  const snap = d.snapshot as Record<string, unknown> & { hours?: { dayOfWeek: number }[]; services?: { name: string; price: number | null; durationMinutes: number }[] };
  const pendingInspection = d.inspections.some((i) => i.status === "scheduled" || i.status === "in_progress");

  return (
    <>
      <Link to="/moderation" className="mb-3 inline-flex min-h-11 items-center font-medium text-muted hover:text-ink">← যাচাইকরণ তালিকা</Link>
      <PageHeader title={r.businessName} subtitle={`${r.ownerName} · ${r.categoryName}${r.zoneName ? ` · ${r.zoneName}` : ""} · জমা ${dateTime(r.submittedAt)}`}
        actions={<Badge tone={VERIFICATION_STATUS[r.status].tone}>{VERIFICATION_STATUS[r.status].label}</Badge>} />
      <ErrorBox error={start.error} />

      <div className="grid gap-6 xl:grid-cols-[1.4fr_1fr]">
        <div className="flex flex-col gap-6">
          <section className="card p-6">
            <h2 className="mb-1 text-base font-semibold">জমা দেওয়া তথ্য</h2>
            <p className="mb-4 text-sm text-subtle">জমার মুহূর্তের অপরিবর্তনীয় কপি — মডারেটর ব্যবসার তথ্য সম্পাদনা করেন না।</p>
            <dl className="grid gap-3 sm:grid-cols-2">
              {[["নাম", snap.name], ["ক্যাটাগরি", snap.category], ["ঠিকানা", snap.addressLine], ["এলাকা", snap.zone], ["মোবাইল", snap.phone], ["ইমেইল", snap.email],
                ["হোম সার্ভিস", snap.offersHomeService ? "হ্যাঁ" : "না"], ["কাজের দিন", `${bn(snap.hours?.length ?? 0)} দিন`]]
                .filter(([, v]) => v).map(([k, v]) => <div key={k as string} className="rounded-md bg-bg p-3"><dt className="text-sm text-subtle">{k as string}</dt><dd className="font-semibold">{String(v)}</dd></div>)}
            </dl>
            {Boolean(snap.description || snap.shortDescription) && <p className="mt-4 whitespace-pre-line text-zinc-700">{String(snap.description || snap.shortDescription)}</p>}
            {snap.services && <><h3 className="mt-5 mb-2 font-semibold">সেবা ({bn(snap.services.length)})</h3>
              <ul className="list-inside list-disc text-[15px] text-zinc-700">{snap.services.map((s) => <li key={s.name}>{s.name}{s.price != null && ` — ৳${bn(s.price)}`}</li>)}</ul></>}
          </section>

          <section className="card p-6">
            <h2 className="mb-3 text-base font-semibold">ডকুমেন্ট</h2>
            <ul className="flex flex-col gap-2">
              {d.documents.map((doc) => (
                <li key={doc.id} className="flex flex-wrap items-center gap-3 rounded-md border border-line px-4 py-3">
                  <span className="font-semibold">{DOCUMENT_TYPES[doc.documentType]}</span>
                  <a href={doc.fileUrl} target="_blank" rel="noreferrer noopener" className="inline-flex min-h-9 items-center gap-1 text-primary">দেখুন <ExternalLink className="size-4" aria-hidden /></a>
                  <Badge className="ml-auto text-xs">{doc.status === "accepted" ? "গৃহীত" : doc.status === "rejected" ? "প্রত্যাখ্যাত" : "অপেক্ষমাণ"}</Badge>
                </li>
              ))}
            </ul>
          </section>

          {d.inspections.length > 0 && (
            <section className="card p-6">
              <h2 className="mb-3 text-base font-semibold">মাঠ পরিদর্শন</h2>
              {d.inspections.map((i) => (
                <div key={i.id} className="rounded-md bg-bg p-4">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge tone={i.status === "completed" ? "green" : "amber"}>{i.status === "completed" ? "সম্পন্ন" : i.status === "in_progress" ? "চলছে" : "নির্ধারিত"}</Badge>
                    {i.outcome && <Badge tone={i.outcome === "passed" ? "teal" : "red"}>{{ passed: "উত্তীর্ণ", failed: "অনুত্তীর্ণ", needs_followup: "পুনরায় দেখা প্রয়োজন", business_not_found: "ব্যবসা পাওয়া যায়নি" }[i.outcome]}</Badge>}
                    <span className="text-sm text-subtle">{i.inspectorName ?? "পরিদর্শক নির্ধারণের অপেক্ষায়"}</span>
                  </div>
                  {i.findings && <p className="mt-2">{i.findings}</p>}
                </div>
              ))}
            </section>
          )}
        </div>

        <div className="flex flex-col gap-6">
          <section className="card flex flex-col gap-3 p-6">
            <h2 className="text-base font-semibold">সিদ্ধান্ত</h2>
            {r.status === "submitted" || (r.status === "under_review" && !r.assignedModeratorId) ? (
              <Button loading={start.isPending} onClick={() => start.mutate()}>পর্যালোচনা শুরু করুন</Button>
            ) : r.status === "under_review" && mine ? (
              <>
                <Button disabled={pendingInspection} title={pendingInspection ? "পরিদর্শনের ফলাফলের অপেক্ষায়" : undefined} onClick={() => setDecision("approve")}>অনুমোদন করুন</Button>
                <Button variant="outline" onClick={() => setDecision("request-info")}>আরও তথ্য চান</Button>
                {!pendingInspection && <Button variant="outline" onClick={() => setDecision("request-inspection")}>মাঠ পরিদর্শনের অনুরোধ</Button>}
                <Button variant="danger" onClick={() => setDecision("reject")}>প্রত্যাখ্যান করুন</Button>
                {pendingInspection && <p className="text-sm text-subtle">পরিদর্শন সম্পন্ন হলে অনুমোদন করা যাবে।</p>}
              </>
            ) : r.status === "under_review" ? <p className="text-muted">অন্য একজন মডারেটর পর্যালোচনা করছেন।</p>
              : <p className="text-muted">এই আবেদনের সিদ্ধান্ত হয়ে গেছে।{d.decisionReason && ` কারণ: ${d.decisionReason}`}</p>}
          </section>

          <section className="card p-6">
            <h2 className="mb-3 text-base font-semibold">সিদ্ধান্তের ইতিহাস</h2>
            {d.history.length === 0 ? <p className="text-muted">এখনও কোনো সিদ্ধান্ত নেই।</p> : (
              <ol className="flex flex-col gap-3 border-l-2 border-line pl-4">
                {d.history.map((h) => (
                  <li key={h.id} className="text-[15px]">
                    <div><span className="font-semibold">{MODERATION_ACTIONS[h.action] ?? h.action}</span> <span className="text-subtle">· {h.moderatorName} · {relative(h.createdAt)}</span></div>
                    <div className="text-muted">{h.reason}</div>
                  </li>
                ))}
              </ol>
            )}
          </section>
        </div>
      </div>

      <Modal open={!!decision} onClose={() => setDecision(null)} title={decision ? DECISION_TITLE[decision] : ""}
        footer={<><Button variant="ghost" onClick={() => setDecision(null)}>বাতিল</Button>
          <Button variant={decision === "reject" ? "danger" : "primary"} disabled={reason.trim().length < 5} loading={decide.isPending} onClick={() => decision && decide.mutate(decision)}>নিশ্চিত করুন</Button></>}>
        <div className="flex flex-col gap-3">
          <ErrorBox error={decide.error} />
          <label className="label">কারণ <span className="text-sm font-normal text-subtle">(আবশ্যক — অডিট লগে সংরক্ষিত হবে{decision !== "approve" && decision !== "request-inspection" ? "; ব্যবসায়ী দেখবেন" : ""})</span>
            <textarea className="input" rows={4} value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />
          </label>
        </div>
      </Modal>
    </>
  );
}

const RESOLUTIONS = [
  { value: "dismiss", label: "খারিজ করুন" }, { value: "resolve", label: "নিষ্পত্তি" }, { value: "hide_review", label: "রিভিউ লুকান" },
  { value: "remove_review", label: "রিভিউ অপসারণ" }, { value: "request_inspection", label: "মাঠ পরিদর্শন" }, { value: "suspend_business", label: "ব্যবসা স্থগিত" }, { value: "escalate", label: "উচ্চতর পর্যায়ে" },
];

export function ModerationReports() {
  const qc = useQueryClient();
  const toast = useToast();
  const [active, setActive] = useState<ReportItem | null>(null);
  const [resolution, setResolution] = useState("dismiss");
  const [reason, setReason] = useState("");
  const list = useQuery({ queryKey: ["moderation", "reports"], queryFn: () => api.get<Paged<ReportItem>>("/api/moderation/reports", { pageSize: 100 }) });
  const resolve = useMutation({
    mutationFn: () => api.post(`/api/moderation/reports/${active!.id}/resolve`, { resolution, reason }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["moderation", "reports"] }); setActive(null); setReason(""); toast("রিপোর্টের সিদ্ধান্ত সংরক্ষিত হয়েছে।"); },
  });
  const options = RESOLUTIONS.filter((r) => !active || (active.targetType === "review" ? r.value !== "suspend_business" && r.value !== "request_inspection" : !r.value.includes("review")));

  return (
    <>
      <PageHeader title="রিপোর্ট" subtitle="গ্রাহক ও ব্যবসার করা অভিযোগ" />
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <ul className="flex flex-col gap-3">
          {list.data.items.map((r) => (
            <li key={r.id} className="card flex flex-wrap items-center gap-4 p-4">
              <div className="min-w-0 flex-[1_1_260px]">
                <div className="font-semibold">{r.targetType === "review" ? `রিভিউ: “${r.reviewComment ?? "—"}”` : r.targetBusinessName ?? "ব্যবহারকারী"}</div>
                <div className="text-[15px] text-muted">{REPORT_REASONS[r.reason]}{r.description && ` — ${r.description}`}</div>
              </div>
              {r.sameTargetReports > 1 && <Badge tone="red">{bn(r.sameTargetReports)}টি রিপোর্ট</Badge>}
              <Badge tone={PRIORITY[r.severity].tone}>{PRIORITY[r.severity].label}</Badge>
              <span className="text-sm text-subtle">{relative(r.createdAt)}</span>
              <Button size="sm" onClick={() => { setActive(r); setResolution("dismiss"); }}>সিদ্ধান্ত নিন</Button>
            </li>
          ))}
        </ul>
      ) : <EmptyState title="কোনো খোলা রিপোর্ট নেই।" />}
      <Modal open={!!active} onClose={() => setActive(null)} title="রিপোর্টের সিদ্ধান্ত"
        footer={<><Button variant="ghost" onClick={() => setActive(null)}>বাতিল</Button><Button disabled={reason.trim().length < 5} loading={resolve.isPending} onClick={() => resolve.mutate()}>নিশ্চিত করুন</Button></>}>
        <div className="flex flex-col gap-4">
          <ErrorBox error={resolve.error} />
          <label className="label">পদক্ষেপ<select className="input" value={resolution} onChange={(e) => setResolution(e.target.value)}>{options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}</select></label>
          <label className="label">কারণ<textarea className="input" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
        </div>
      </Modal>
    </>
  );
}

export function ModerationHistory() {
  const list = useQuery({
    queryKey: ["moderation", "history"],
    queryFn: () => api.get<Paged<{ id: string; action: string; reason: string; moderatorName: string; createdAt: string }>>("/api/moderation/actions", { pageSize: 100 }),
  });
  return (
    <>
      <PageHeader title="সিদ্ধান্তের ইতিহাস" subtitle="প্রতিটি মডারেশন সিদ্ধান্ত কারণসহ সংরক্ষিত" />
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <ul className="card divide-y divide-zinc-100">
          {list.data.items.map((a) => (
            <li key={a.id} className="flex flex-wrap gap-x-4 gap-y-1 px-5 py-3">
              <span className="font-semibold">{MODERATION_ACTIONS[a.action] ?? a.action}</span>
              <span className="flex-1 text-muted">{a.reason}</span>
              <span className="text-sm text-subtle">{a.moderatorName} · {dateTime(a.createdAt)}</span>
            </li>
          ))}
        </ul>
      ) : <EmptyState title="এখনও কোনো সিদ্ধান্ত নেই।" />}
    </>
  );
}
