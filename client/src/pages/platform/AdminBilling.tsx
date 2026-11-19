import { useState } from "react";
import { Link } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Gift, XCircle } from "lucide-react";
import { api } from "../../api/client";
import type { AdminPayment, AdminSubscription, BillingCycle, BillingSummary, Paged, PaymentMethod } from "../../api/types";
import { Badge, Button, EmptyState, ErrorBox, LoadingList, Modal, PageHeader, Segmented, Stat, useToast } from "../../components/ui";
import { METHOD_LABEL, PLAN_STATUS, usePlans } from "../../lib/billing";
import { bn, dateShort, relative, taka } from "../../lib/format";

type Tab = "payments" | "subscriptions" | "plans";

export default function AdminBilling() {
  const [tab, setTab] = useState<Tab>("payments");
  const summary = useQuery({ queryKey: ["admin", "billing", "summary"], queryFn: () => api.get<BillingSummary>("/api/admin/billing/summary") });
  const s = summary.data;
  return (
    <>
      <PageHeader title="সাবস্ক্রিপশন ও পেমেন্ট" subtitle="পেমেন্ট যাচাই, ব্যবসার প্ল্যান ও মূল্য — প্রতিটি সিদ্ধান্ত অডিট লগে সংরক্ষিত" />
      <ErrorBox error={summary.error} />
      <section aria-label="সারসংক্ষেপ" className="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-5">
        <Stat label="মাসিক আয় (MRR)" value={s ? taka(s.mrr) : "—"} note="সক্রিয় পেইড প্ল্যান থেকে" />
        <Stat label="পেইড ব্যবসা" value={s ? bn(s.activePaid) : "—"} tone="green" note={s ? Object.entries(s.paidByPlan).map(([k, n]) => `${k === "pro" ? "প্রো" : "বিজনেস"} ${bn(n)}`).join(" · ") || "—" : ""} />
        <Stat label="ট্রায়াল চলছে" value={s ? bn(s.trialing) : "—"} tone="blue" />
        <Stat label="নবায়ন বাকি" value={s ? bn(s.pastDue) : "—"} tone="amber" />
        <Stat label="যাচাইয়ের অপেক্ষায়" value={s ? bn(s.pendingPayments) : "—"} tone="red" note={s ? `গত ৩০ দিনে আদায় ${taka(s.revenue30d)}` : ""} />
      </section>
      <div className="mb-5">
        <Segmented label="বিভাগ" value={tab} onChange={setTab}
          options={[{ value: "payments", label: `পেমেন্ট যাচাই${s?.pendingPayments ? ` (${bn(s.pendingPayments)})` : ""}` }, { value: "subscriptions", label: "সব ব্যবসা" }, { value: "plans", label: "প্ল্যানের মূল্য" }]} />
      </div>
      {tab === "payments" && <PaymentsQueue />}
      {tab === "subscriptions" && <SubscriptionsTable />}
      {tab === "plans" && <PlanPrices />}
    </>
  );
}

function useInvalidate() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: ["admin", "billing"] });
}

function PaymentsQueue() {
  const [status, setStatus] = useState<"pending" | "verified" | "rejected">("pending");
  const [acting, setActing] = useState<{ p: AdminPayment; action: "verify" | "reject" } | null>(null);
  const [note, setNote] = useState("");
  const toast = useToast();
  const invalidate = useInvalidate();
  const list = useQuery({
    queryKey: ["admin", "billing", "payments", status],
    queryFn: () => api.get<Paged<AdminPayment>>("/api/admin/billing/payments", { status, pageSize: 50 }),
    placeholderData: keepPreviousData,
  });
  const act = useMutation({
    mutationFn: () => api.post(`/api/admin/billing/payments/${acting!.p.id}/${acting!.action}`, acting!.action === "verify" ? { note: note || undefined } : { reason: note }),
    onSuccess: () => { invalidate(); toast(acting!.action === "verify" ? "পেমেন্ট যাচাই হয়েছে, প্ল্যান চালু হয়েছে।" : "পেমেন্ট প্রত্যাখ্যান করা হয়েছে।"); setActing(null); setNote(""); },
  });
  return (
    <>
      <div className="mb-4">
        <Segmented label="অবস্থা" value={status} onChange={setStatus}
          options={[{ value: "pending", label: "অপেক্ষমাণ" }, { value: "verified", label: "যাচাইকৃত" }, { value: "rejected", label: "প্রত্যাখ্যাত" }]} />
      </div>
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : !list.data?.items.length ? (
        <EmptyState title={status === "pending" ? "যাচাইয়ের অপেক্ষায় কোনো পেমেন্ট নেই" : "কিছু পাওয়া যায়নি"} />
      ) : (
        <div className="card overflow-x-auto">
          <table className="w-full min-w-[860px] text-left text-sm">
            <thead className="border-b border-line text-xs text-subtle"><tr>
              <th className="px-5 py-3 font-medium">ব্যবসা</th><th className="px-3 py-3 font-medium">ইনভয়েস</th><th className="px-3 py-3 font-medium">পদ্ধতি ও TrxID</th>
              <th className="px-3 py-3 text-right font-medium">পরিমাণ</th><th className="px-3 py-3 font-medium">জমা</th><th className="px-5 py-3"><span className="sr-only">কাজ</span></th>
            </tr></thead>
            <tbody className="divide-y divide-line">
              {list.data.items.map((p) => (
                <tr key={p.id}>
                  <td className="px-5 py-3"><div className="font-semibold">{p.businessName}</div><div className="text-xs text-subtle">{p.planName} · {p.cycle === "yearly" ? "বাৎসরিক" : "মাসিক"}</div></td>
                  <td className="px-3 py-3 font-num">{p.invoiceNumber}</td>
                  <td className="px-3 py-3">
                    <div className="font-medium">{METHOD_LABEL[p.method]}</div>
                    {p.transactionId && <div className="font-num font-semibold tracking-wide">{p.transactionId}</div>}
                    {p.senderNumber && <div className="font-num text-xs text-subtle">প্রেরক: {p.senderNumber}</div>}
                  </td>
                  <td className="px-3 py-3 text-right font-semibold">{taka(p.amount)}</td>
                  <td className="px-3 py-3"><div>{p.submittedByName}</div><div className="text-xs text-subtle">{relative(p.createdAt)}</div>
                    {p.reviewNote && <div className="mt-0.5 text-xs text-muted">“{p.reviewNote}”</div>}</td>
                  <td className="px-5 py-3">
                    {p.status === "pending" ? (
                      <div className="flex justify-end gap-2">
                        <Button size="sm" onClick={() => setActing({ p, action: "verify" })}><CheckCircle2 className="size-4" aria-hidden />যাচাই</Button>
                        <Button size="sm" variant="danger" onClick={() => setActing({ p, action: "reject" })}><XCircle className="size-4" aria-hidden />প্রত্যাখ্যান</Button>
                      </div>
                    ) : <Badge tone={p.status === "verified" ? "green" : "red"}>{p.status === "verified" ? "যাচাইকৃত" : "প্রত্যাখ্যাত"}</Badge>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <Modal open={!!acting} onClose={() => setActing(null)} title={acting?.action === "verify" ? "পেমেন্ট যাচাই করবেন?" : "পেমেন্ট প্রত্যাখ্যান করবেন?"}
        footer={<><Button variant="ghost" onClick={() => setActing(null)}>বাতিল</Button>
          <Button variant={acting?.action === "verify" ? "primary" : "danger"} loading={act.isPending} disabled={acting?.action === "reject" && note.trim().length < 5} onClick={() => act.mutate()}>নিশ্চিত করুন</Button></>}>
        {acting && (
          <div className="flex flex-col gap-3">
            <ErrorBox error={act.error} />
            <p className="text-muted">
              {acting.action === "verify"
                ? <>{METHOD_LABEL[acting.p.method]} স্টেটমেন্টে <strong className="font-num text-ink">{acting.p.transactionId}</strong> লেনদেনে <strong className="text-ink">{taka(acting.p.amount)}</strong> এসেছে নিশ্চিত হলে যাচাই করুন। সঙ্গে সঙ্গে “{acting.p.businessName}”-এর {acting.p.planName} প্ল্যান চালু হবে।</>
                : "ব্যবসার মালিক কারণসহ নোটিফিকেশন পাবেন এবং আবার জমা দিতে পারবেন।"}
            </p>
            <label className="label">{acting.action === "verify" ? <>নোট <span className="text-xs font-normal text-subtle">(ঐচ্ছিক)</span></> : "কারণ"}
              <textarea className="input" rows={2} value={note} onChange={(e) => setNote(e.target.value)} placeholder={acting.action === "reject" ? "যেমন: এই নম্বরে কোনো লেনদেন পাওয়া যায়নি" : ""} />
            </label>
          </div>
        )}
      </Modal>
    </>
  );
}

function SubscriptionsTable() {
  const [q, setQ] = useState("");
  const [plan, setPlan] = useState("");
  const [granting, setGranting] = useState<AdminSubscription | null>(null);
  const list = useQuery({
    queryKey: ["admin", "billing", "subscriptions", q, plan],
    queryFn: () => api.get<Paged<AdminSubscription>>("/api/admin/billing/subscriptions", { q: q || undefined, plan: plan || undefined, pageSize: 50 }),
    placeholderData: keepPreviousData,
  });
  return (
    <>
      <div className="mb-4 flex flex-wrap gap-3">
        <input className="input max-w-sm" value={q} onChange={(e) => setQ(e.target.value)} placeholder="ব্যবসার নাম খুঁজুন" aria-label="ব্যবসা খুঁজুন" />
        <select className="input w-auto" value={plan} onChange={(e) => setPlan(e.target.value)} aria-label="প্ল্যান">
          <option value="">সব প্ল্যান</option><option value="free">ফ্রি</option><option value="pro">প্রো</option><option value="business">বিজনেস</option>
        </select>
      </div>
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : (
        <div className="card overflow-x-auto">
          <table className="w-full min-w-[760px] text-left text-sm">
            <thead className="border-b border-line text-xs text-subtle"><tr>
              <th className="px-5 py-3 font-medium">ব্যবসা</th><th className="px-3 py-3 font-medium">প্ল্যান</th><th className="px-3 py-3 font-medium">মেয়াদ</th><th className="px-5 py-3"><span className="sr-only">কাজ</span></th>
            </tr></thead>
            <tbody className="divide-y divide-line">
              {list.data?.items.map((s) => (
                <tr key={s.businessId}>
                  <td className="px-5 py-3"><div className="font-semibold">{s.businessStatus === "published" ? <Link to={`/b/${s.slug}`} className="hover:text-primary">{s.businessName}</Link> : s.businessName}</div>
                    <div className="text-xs text-subtle">মালিক: {s.ownerName}</div></td>
                  <td className="px-3 py-3"><div className="font-medium">{s.planName}</div>
                    {s.status && s.planKey !== "free" && <Badge tone={PLAN_STATUS[s.status].tone}>{PLAN_STATUS[s.status].label}</Badge>}
                    {s.cancelAtPeriodEnd && <Badge className="ml-1">মেয়াদ শেষে বাতিল</Badge>}</td>
                  <td className="px-3 py-3 text-muted">{s.planKey === "free" ? "—" : s.status === "trialing" ? `ট্রায়াল ${s.trialEndsAt ? dateShort(s.trialEndsAt) : ""} পর্যন্ত` : s.currentPeriodEnd ? `${dateShort(s.currentPeriodEnd)} পর্যন্ত` : "—"}</td>
                  <td className="px-5 py-3 text-right"><Button size="sm" variant="outline" onClick={() => setGranting(s)}><Gift className="size-4" aria-hidden />প্ল্যান দিন</Button></td>
                </tr>
              ))}
            </tbody>
          </table>
          {!list.data?.items.length && <p className="p-6 text-center text-sm text-subtle">কোনো ব্যবসা পাওয়া যায়নি।</p>}
        </div>
      )}
      {granting && <GrantModal sub={granting} onClose={() => setGranting(null)} />}
    </>
  );
}

function GrantModal({ sub, onClose }: { sub: AdminSubscription; onClose: () => void }) {
  const plans = usePlans();
  const toast = useToast();
  const invalidate = useInvalidate();
  const [planKey, setPlanKey] = useState("pro");
  const [cycle, setCycle] = useState<BillingCycle>("monthly");
  const [method, setMethod] = useState<PaymentMethod>("cash");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const chosen = plans.data?.plans.find((p) => p.key === planKey);
  const price = chosen ? (cycle === "yearly" ? chosen.priceYearly : chosen.priceMonthly) : 0;
  const grant = useMutation({
    mutationFn: () => api.post(`/api/admin/billing/businesses/${sub.businessId}/grant`, {
      planKey, cycle, method, reason, amount: method === "complimentary" ? undefined : amount ? Number(amount) : undefined,
    }),
    onSuccess: () => { invalidate(); toast("প্ল্যান চালু করা হয়েছে।"); onClose(); },
  });
  return (
    <Modal open onClose={onClose} title={`“${sub.businessName}”-কে প্ল্যান দিন`}
      footer={<><Button variant="ghost" onClick={onClose}>বাতিল</Button><Button loading={grant.isPending} disabled={reason.trim().length < 5} onClick={() => grant.mutate()}>প্ল্যান চালু করুন</Button></>}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={grant.error} />
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="label">প্ল্যান
            <select className="input" value={planKey} onChange={(e) => setPlanKey(e.target.value)}>
              {plans.data?.plans.filter((p) => p.key !== "free").map((p) => <option key={p.key} value={p.key}>{p.nameBn}</option>)}
            </select>
          </label>
          <label className="label">মেয়াদ
            <select className="input" value={cycle} onChange={(e) => setCycle(e.target.value as BillingCycle)}>
              <option value="monthly">১ মাস</option><option value="yearly">১ বছর</option>
            </select>
          </label>
          <label className="label">পেমেন্ট
            <select className="input" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
              {(["cash", "bkash", "nagad", "bank_transfer", "complimentary"] as PaymentMethod[]).map((m) => <option key={m} value={m}>{METHOD_LABEL[m]}</option>)}
            </select>
          </label>
          {method !== "complimentary" && (
            <label className="label">গৃহীত টাকা
              <input className="input" type="number" min={0} value={amount} onChange={(e) => setAmount(e.target.value)} placeholder={`পুরো মূল্য: ${price}`} />
            </label>
          )}
        </div>
        <label className="label">কারণ / রসিদের বিবরণ
          <textarea className="input" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="যেমন: গাজীপুর অফিসে নগদ গ্রহণ, রসিদ নং ১২৩" />
        </label>
        <p className="text-xs text-subtle">সঙ্গে সঙ্গে চালু হবে এবং অডিট লগে আপনার নামসহ সংরক্ষিত থাকবে।</p>
      </div>
    </Modal>
  );
}

function PlanPrices() {
  const plans = usePlans();
  const qc = useQueryClient();
  const toast = useToast();
  const [edit, setEdit] = useState<Record<string, { m: string; y: string }>>({});
  const save = useMutation({
    mutationFn: (key: string) => api.put(`/api/admin/billing/plans/${key}`, { priceMonthly: Number(edit[key].m), priceYearly: Number(edit[key].y) }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["plans"] }); toast("মূল্য হালনাগাদ হয়েছে।"); },
  });
  if (plans.isLoading) return <LoadingList rows={2} />;
  return (
    <div className="flex flex-col gap-4">
      <ErrorBox error={plans.error ?? save.error} />
      <p className="text-sm text-muted">নতুন মূল্য শুধু নতুন ইনভয়েসে প্রযোজ্য — চলমান প্ল্যান বা জমা দেওয়া ইনভয়েস বদলায় না। সুবিধার তালিকা কোডে নির্ধারিত।</p>
      <div className="grid gap-4 md:grid-cols-2">
        {plans.data?.plans.filter((p) => p.key !== "free").map((p) => {
          const v = edit[p.key] ?? { m: String(p.priceMonthly), y: String(p.priceYearly) };
          return (
            <div key={p.key} className="card flex flex-col gap-3 p-5">
              <div className="font-semibold">{p.nameBn}</div>
              <div className="grid grid-cols-2 gap-3">
                <label className="label">মাসিক (৳)<input className="input" type="number" min={0} value={v.m} onChange={(e) => setEdit({ ...edit, [p.key]: { ...v, m: e.target.value } })} /></label>
                <label className="label">বাৎসরিক (৳)<input className="input" type="number" min={0} value={v.y} onChange={(e) => setEdit({ ...edit, [p.key]: { ...v, y: e.target.value } })} /></label>
              </div>
              <div><Button size="sm" disabled={!edit[p.key]} loading={save.isPending && save.variables === p.key} onClick={() => save.mutate(p.key)}>সংরক্ষণ</Button></div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
