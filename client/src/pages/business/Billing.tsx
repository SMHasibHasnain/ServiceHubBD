import { useState } from "react";
import { useOutletContext } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Clock, Copy, CreditCard, Hourglass, Receipt, ShieldCheck } from "lucide-react";
import { api } from "../../api/client";
import type { BillingCycle, BusinessDetail, BusinessPlan, Invoice, Paged, PaymentMethod, Plan, PlansResponse } from "../../api/types";
import { Badge, Button, EmptyState, ErrorBox, LoadingList, Modal, PAGE_SIZE, PageHeader, Pagination, Segmented, Skeleton, usePage, useToast } from "../../components/ui";
import { METHOD_LABEL, PLAN_STATUS, UNLIMITED, daysLeft, featureValue, useBusinessPlan, usePlans } from "../../lib/billing";
import { bn, dateShort, taka } from "../../lib/format";

/** How a limit reads after its number on a plan card: "৫টি সেবা", "৯০ দিন পর্যন্ত বিশ্লেষণ". */
const SHORT: Record<string, string> = {
  "services.max": "সেবা",
  "members.max": "টিম সদস্য (মালিক ছাড়া)",
  "booking_fields.max": "প্রশ্ন প্রতি সেবার বুকিং ফর্মে",
  "analytics.max_days": "পর্যন্ত বিশ্লেষণ",
};

const INVOICE_STATUS = {
  open: { label: "পেমেন্ট বাকি", tone: "amber" },
  pending_verification: { label: "যাচাই চলছে", tone: "blue" },
  paid: { label: "পরিশোধিত", tone: "green" },
  void: { label: "বাতিল", tone: "neutral" },
} as const;

export default function Billing() {
  const biz = useOutletContext<BusinessDetail | undefined>();
  const id = biz?.id;
  const plan = useBusinessPlan(id);
  const plans = usePlans();
  // Newest first, so an invoice still waiting for payment is always on the first page.
  const invoices = useQuery({
    queryKey: ["business", id, "invoices", 1],
    queryFn: () => api.get<Paged<Invoice>>(`/api/businesses/${id}/billing/invoices`, { page: 1, pageSize: PAGE_SIZE }),
    enabled: !!id,
  });
  const [cycle, setCycle] = useState<BillingCycle>("monthly");

  if (!biz || plan.isLoading || plans.isLoading) return <LoadingList rows={3} />;
  if (!biz.myPermissions.includes("billing.manage")) {
    return <EmptyState icon={<CreditCard aria-hidden />} title="বিলিং শুধু মালিক দেখতে পারেন" description={`বর্তমান প্ল্যান: ${plan.data?.planName ?? "—"}`} />;
  }
  const p = plan.data!, catalog = plans.data!;
  const pendingInvoice = invoices.data?.items.find((i) => i.status === "open" || i.status === "pending_verification");

  return (
    <>
      <PageHeader title="সাবস্ক্রিপশন ও বিলিং" subtitle="আপনার প্ল্যান, ব্যবহারের সীমা ও পেমেন্ট" />
      <ErrorBox error={plan.error ?? plans.error ?? invoices.error} />
      <div className="flex flex-col gap-6">
        <CurrentPlan businessId={biz.id} plan={p} />
        {pendingInvoice && <PaymentPanel businessId={biz.id} invoice={pendingInvoice} catalog={catalog} />}
        <section aria-labelledby="plans-h">
          <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
            <div>
              <h2 id="plans-h" className="text-lg font-semibold">প্ল্যান বেছে নিন</h2>
              <p className="text-sm text-muted">যেকোনো সময় বদলানো বা বাতিল করা যায়। টাকা দিলেও সার্চে আগে দেখানো হয় না — যাচাইকৃত ব্যাজ সবার জন্য সমান।</p>
            </div>
            <Segmented label="বিলিং চক্র" value={cycle} onChange={setCycle}
              options={[{ value: "monthly", label: "মাসিক" }, { value: "yearly", label: "বাৎসরিক · ২ মাস ফ্রি" }]} />
          </div>
          <PlanCards catalog={catalog} current={p} cycle={cycle} businessId={biz.id} blocked={pendingInvoice?.status === "pending_verification"} />
        </section>
        {id && <InvoiceHistory businessId={id} />}
      </div>
    </>
  );
}

function useRefresh(businessId: string) {
  const qc = useQueryClient();
  return () => {
    qc.invalidateQueries({ queryKey: ["business", businessId, "plan"] });
    qc.invalidateQueries({ queryKey: ["business", businessId, "invoices"] });
    qc.invalidateQueries({ queryKey: ["workspaces"] });
  };
}

function Meter({ label, used, limit }: { label: string; used: number; limit: number }) {
  const unlimited = limit === UNLIMITED;
  const pct = unlimited ? 0 : Math.min(100, (used / Math.max(limit, 1)) * 100);
  const full = !unlimited && used >= limit;
  return (
    <div>
      <div className="mb-1 flex justify-between text-sm"><span className="text-muted">{label}</span>
        <span className="font-semibold">{bn(used)} / {unlimited ? "সীমাহীন" : bn(limit)}</span></div>
      <div className="h-2 rounded-full bg-primary-light" aria-hidden>
        {!unlimited && <div className={`h-full rounded-full ${full ? "bg-amber-500" : "bg-primary"}`} style={{ width: `${pct}%` }} />}
      </div>
    </div>
  );
}

function CurrentPlan({ businessId, plan: p }: { businessId: string; plan: BusinessPlan }) {
  const toast = useToast();
  const refresh = useRefresh(businessId);
  const [confirm, setConfirm] = useState(false);
  const cancel = useMutation({ mutationFn: () => api.post(`/api/businesses/${businessId}/billing/cancel`), onSuccess: () => { refresh(); setConfirm(false); toast("অনুরোধ গ্রহণ করা হয়েছে।"); } });
  const resume = useMutation({ mutationFn: () => api.post(`/api/businesses/${businessId}/billing/resume`), onSuccess: () => { refresh(); toast("প্ল্যান চালু থাকবে।"); } });
  const status = p.status ? PLAN_STATUS[p.status] : null;

  let line: string;
  if (p.isTrial) line = `ট্রায়াল শেষ হবে ${dateShort(p.trialEndsAt!)} — আর ${bn(daysLeft(p.trialEndsAt))} দিন বাকি`;
  else if (p.status === "past_due") line = `মেয়াদ শেষ হয়েছে ${dateShort(p.currentPeriodEnd!)}। ${dateShort(p.graceEndsAt!)}-এর মধ্যে নবায়ন না করলে ফ্রি প্ল্যানে চলে যাবে।`;
  else if (p.isPaid && p.cancelAtPeriodEnd) line = `${dateShort(p.currentPeriodEnd!)} পর্যন্ত চালু থাকবে, তারপর ফ্রি প্ল্যান`;
  else if (p.isPaid) line = `${p.cycle === "yearly" ? "বাৎসরিক" : "মাসিক"} · মেয়াদ ${dateShort(p.currentPeriodEnd!)} পর্যন্ত`;
  else line = "বিনামূল্যে — প্রয়োজন হলে যেকোনো সময় আপগ্রেড করুন";

  return (
    <section className="card grid gap-6 p-6 md:grid-cols-[1.2fr_1fr]" aria-label="বর্তমান প্ল্যান">
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm text-subtle">বর্তমান প্ল্যান</span>
          {status && p.planKey !== "free" && <Badge tone={status.tone}>{status.label}</Badge>}
        </div>
        <div className="text-3xl font-semibold text-primary">{p.planName}</div>
        <p className={`text-sm ${p.status === "past_due" ? "font-medium text-amber-800" : "text-muted"}`}>{line}</p>
        <div className="mt-1 flex flex-wrap gap-2">
          {p.isPaid && !p.cancelAtPeriodEnd && <Button variant="ghost" size="sm" onClick={() => setConfirm(true)}>প্ল্যান বাতিল করুন</Button>}
          {p.isTrial && <Button variant="ghost" size="sm" onClick={() => setConfirm(true)}>ট্রায়াল বন্ধ করুন</Button>}
          {p.isPaid && p.cancelAtPeriodEnd && <Button variant="outline" size="sm" loading={resume.isPending} onClick={() => resume.mutate()}>বাতিল প্রত্যাহার করুন</Button>}
        </div>
        <ErrorBox error={resume.error} />
      </div>
      <div className="flex flex-col justify-center gap-4 rounded-lg bg-bg p-4">
        <Meter label="সেবা" used={p.usage.services} limit={p.limits["services.max"] ?? 0} />
        <Meter label="টিম সদস্য ও আমন্ত্রণ" used={p.usage.members} limit={p.limits["members.max"] ?? 0} />
        <p className="text-xs text-subtle">প্ল্যান কমলেও কোনো তথ্য মুছে যায় না — শুধু সীমার বেশি নতুন কিছু যোগ করা যায় না।</p>
      </div>
      <Modal open={confirm} onClose={() => setConfirm(false)} title={p.isTrial ? "ট্রায়াল বন্ধ করবেন?" : "প্ল্যান বাতিল করবেন?"}
        footer={<><Button variant="ghost" onClick={() => setConfirm(false)}>না, থাক</Button><Button variant="danger" loading={cancel.isPending} onClick={() => cancel.mutate()}>হ্যাঁ, বাতিল করুন</Button></>}>
        <ErrorBox error={cancel.error} />
        <p className="text-muted">{p.isTrial ? "এখনই ফ্রি প্ল্যানে চলে যাবেন। ট্রায়াল আর ফিরে পাওয়া যাবে না।" : `${dateShort(p.currentPeriodEnd!)} পর্যন্ত সব সুবিধা থাকবে, তারপর ফ্রি প্ল্যান চালু হবে। টাকা ফেরত দেওয়া হয় না।`}</p>
      </Modal>
    </section>
  );
}

function PlanCards({ catalog, current, cycle, businessId, blocked }: { catalog: PlansResponse; current: BusinessPlan; cycle: BillingCycle; businessId: string; blocked: boolean }) {
  const toast = useToast();
  const refresh = useRefresh(businessId);
  const checkout = useMutation({
    mutationFn: (key: string) => api.post<Invoice>(`/api/businesses/${businessId}/billing/checkout`, { planKey: key, cycle }),
    onSuccess: (inv) => {
      refresh();
      if (inv.status === "paid") toast("প্ল্যান বদলানো হয়েছে।");
      else setTimeout(() => document.getElementById("payment")?.scrollIntoView({ behavior: "smooth", block: "start" }), 200);
    },
  });
  const price = (pl: Plan) => (cycle === "yearly" ? pl.priceYearly : pl.priceMonthly);
  return (
    <>
      <ErrorBox error={checkout.error} />
      <div className="mt-3 grid gap-4 lg:grid-cols-3">
        {catalog.plans.map((pl) => {
          const isCurrent = pl.key === current.planKey && (pl.key === "free" || current.isPaid || current.isTrial);
          const popular = pl.key === "pro";
          const action = pl.key === "free" ? null
            : current.isPaid && pl.key === current.planKey ? "নবায়ন করুন"
            : "এই প্ল্যান নিন";
          return (
            <article key={pl.key} className={`card relative flex flex-col gap-4 p-5 ${popular ? "border-primary/50 shadow-lift" : ""}`}>
              {popular && <span className="absolute -top-3 left-5 rounded-full bg-primary px-3 py-0.5 text-xs font-semibold text-white">সবচেয়ে জনপ্রিয়</span>}
              <div>
                <div className="flex items-center justify-between gap-2">
                  <h3 className="text-lg font-semibold">{pl.nameBn}</h3>
                  {isCurrent && <Badge tone="green"><CheckCircle2 className="size-3.5" aria-hidden />বর্তমান</Badge>}
                </div>
                {pl.taglineBn && <p className="text-sm text-muted">{pl.taglineBn}</p>}
              </div>
              <div>
                <span className="text-3xl font-semibold tabular-nums">{price(pl) ? taka(price(pl)) : "৳০"}</span>
                {price(pl) > 0 && <span className="text-sm text-subtle"> / {cycle === "yearly" ? "বছর" : "মাস"}</span>}
                {cycle === "yearly" && pl.priceMonthly > 0 && <div className="text-xs text-brand-green">মাসে মাত্র {taka(Math.round(pl.priceYearly / 12))}</div>}
              </div>
              <ul className="flex flex-1 flex-col gap-2 text-sm">
                {catalog.features.map((f) => {
                  const v = pl.entitlements[f.key];
                  const off = f.kind === "switch" && !v;
                  return (
                    <li key={f.key} className={`flex items-start gap-2 ${off ? "text-subtle line-through decoration-zinc-300" : ""}`}>
                      <CheckCircle2 className={`mt-0.5 size-4 shrink-0 ${off ? "text-zinc-300" : "text-brand-green"}`} aria-hidden />
                      <span>{f.kind === "limit" ? <><strong>{featureValue(f, v)}</strong> {SHORT[f.key] ?? f.labelBn}</> : f.labelBn}</span>
                    </li>
                  );
                })}
              </ul>
              {action && (
                <Button variant={popular ? "primary" : "outline"} disabled={blocked} loading={checkout.isPending && checkout.variables === pl.key}
                  onClick={() => checkout.mutate(pl.key)}>{action}</Button>
              )}
            </article>
          );
        })}
      </div>
      {blocked && <p className="mt-2 text-sm text-muted">একটি পেমেন্ট যাচাইয়ের অপেক্ষায় আছে — যাচাই শেষ হলে আবার প্ল্যান বদলাতে পারবেন।</p>}
    </>
  );
}

function PaymentPanel({ businessId, invoice, catalog }: { businessId: string; invoice: Invoice; catalog: PlansResponse }) {
  const toast = useToast();
  const refresh = useRefresh(businessId);
  const accounts = catalog.paymentAccounts;
  const [method, setMethod] = useState<PaymentMethod>(accounts[0]?.method ?? "bkash");
  const [trx, setTrx] = useState("");
  const [sender, setSender] = useState("");
  const account = accounts.find((a) => a.method === method);
  const submit = useMutation({
    mutationFn: () => api.post(`/api/businesses/${businessId}/billing/invoices/${invoice.id}/payments`, { method, transactionId: trx, senderNumber: sender || undefined }),
    onSuccess: () => { refresh(); setTrx(""); toast("জমা হয়েছে। যাচাই শেষে প্ল্যান চালু হবে।"); },
  });
  const voidIt = useMutation({ mutationFn: () => api.post(`/api/businesses/${businessId}/billing/invoices/${invoice.id}/void`), onSuccess: refresh });
  const copy = async (text: string) => { try { await navigator.clipboard.writeText(text); toast("কপি করা হয়েছে।"); } catch { /* ignore */ } };
  const lastRejected = invoice.payments.find((p) => p.status === "rejected");

  if (invoice.status === "pending_verification") {
    const claim = invoice.payments.find((p) => p.status === "pending");
    return (
      <section id="payment" className="card flex scroll-mt-24 items-start gap-4 border-primary/15 bg-primary-light/40 p-5">
        <Hourglass className="mt-0.5 size-5 shrink-0 text-primary" aria-hidden />
        <div>
          <h2 className="font-semibold">পেমেন্ট যাচাই চলছে</h2>
          <p className="text-sm text-muted">
            {invoice.planName} ({invoice.cycle === "yearly" ? "বাৎসরিক" : "মাসিক"}) · {taka(invoice.total)} · ইনভয়েস {invoice.number}
            {claim && <> · {METHOD_LABEL[claim.method]} TrxID <span className="font-num font-semibold">{claim.transactionId}</span></>}
          </p>
          <p className="mt-1 text-sm text-muted">সাধারণত ২৪ ঘণ্টার মধ্যে যাচাই করা হয়। যাচাই হলে নোটিফিকেশন পাবেন।</p>
        </div>
      </section>
    );
  }

  return (
    <section id="payment" className="card scroll-mt-24 overflow-hidden" aria-labelledby="pay-h">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line bg-bg px-5 py-4">
        <div>
          <h2 id="pay-h" className="font-semibold">পেমেন্ট সম্পন্ন করুন</h2>
          <p className="text-sm text-muted">{invoice.planName} · {invoice.cycle === "yearly" ? "বাৎসরিক" : "মাসিক"} · ইনভয়েস <span className="font-num">{invoice.number}</span></p>
        </div>
        <div className="text-right">
          <div className="text-2xl font-semibold tabular-nums">{taka(invoice.total)}</div>
          {invoice.credit > 0 && <div className="text-xs text-brand-green">আগের প্ল্যানের {taka(invoice.credit)} বাদ দেওয়া হয়েছে</div>}
        </div>
      </div>
      <div className="grid gap-6 p-5 md:grid-cols-2">
        <div className="flex flex-col gap-4">
          {lastRejected && <ErrorBox error={new Error(`আগের জমা যাচাই হয়নি: ${lastRejected.reviewNote ?? ""}`)} />}
          {accounts.length === 0 ? <p className="text-sm text-muted">পেমেন্ট অ্যাকাউন্ট এখনও সেট করা হয়নি। সাপোর্টে যোগাযোগ করুন।</p> : (
            <>
              <Segmented label="পেমেন্ট পদ্ধতি" value={method} onChange={setMethod} options={accounts.map((a) => ({ value: a.method, label: METHOD_LABEL[a.method] }))} />
              {account && (
                <div className="rounded-lg border border-line p-4">
                  <div className="text-sm text-subtle">{account.label}</div>
                  <div className="mt-1 flex items-center gap-2">
                    <span className="font-num text-xl font-semibold">{account.number}</span>
                    <button onClick={() => copy(account.number)} className="flex size-8 items-center justify-center rounded-md text-subtle hover:bg-zinc-100" aria-label="নম্বর কপি করুন"><Copy className="size-4" aria-hidden /></button>
                  </div>
                  {account.accountName && <div className="text-sm text-muted">{account.accountName}</div>}
                </div>
              )}
              <ol className="flex list-decimal flex-col gap-1.5 pl-5 text-sm text-muted">
                <li>{account?.instructions ?? "উপরের নম্বরে টাকা পাঠান।"}</li>
                <li>ঠিক <strong className="text-ink">{taka(invoice.total)}</strong> পাঠান, রেফারেন্সে লিখুন <button onClick={() => copy(invoice.number)} className="font-num font-semibold text-primary hover:underline">{invoice.number}</button></li>
                <li>{method === "bank_transfer" ? "ব্যাংকের রেফারেন্স নম্বর" : "এসএমএসে পাওয়া লেনদেন নম্বর (TrxID)"} নিচে লিখে জমা দিন।</li>
              </ol>
            </>
          )}
        </div>
        <form className="flex flex-col gap-4" onSubmit={(e) => { e.preventDefault(); submit.mutate(); }}>
          <ErrorBox error={submit.error} />
          <label className="label">{method === "bank_transfer" ? "ব্যাংক রেফারেন্স নম্বর" : "লেনদেন নম্বর (TrxID)"}
            <input className="input font-num uppercase" value={trx} onChange={(e) => setTrx(e.target.value)} required minLength={6} placeholder="যেমন: 9JK7ABC2XY" autoComplete="off" />
          </label>
          <label className="label">{method === "bank_transfer" ? "যে অ্যাকাউন্ট থেকে পাঠিয়েছেন" : "যে নম্বর থেকে পাঠিয়েছেন"} <span className="text-xs font-normal text-subtle">(ঐচ্ছিক, যাচাই দ্রুত হয়)</span>
            <input className="input font-num" value={sender} onChange={(e) => setSender(e.target.value)} inputMode={method === "bank_transfer" ? "text" : "tel"} />
          </label>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" loading={submit.isPending} disabled={trx.trim().length < 6 || accounts.length === 0}>জমা দিন</Button>
            <Button type="button" variant="ghost" loading={voidIt.isPending} onClick={() => voidIt.mutate()}>ইনভয়েস বাতিল</Button>
          </div>
          <p className="flex items-start gap-1.5 text-xs text-subtle"><ShieldCheck className="mt-0.5 size-3.5 shrink-0" aria-hidden />একই লেনদেন নম্বর দুবার ব্যবহার করা যায় না। ভুল তথ্য দিলে প্ল্যান চালু হবে না।</p>
        </form>
      </div>
    </section>
  );
}

function InvoiceHistory({ businessId }: { businessId: string }) {
  const [page, setPage] = usePage();
  const q = useQuery({
    queryKey: ["business", businessId, "invoices", page],
    queryFn: () => api.get<Paged<Invoice>>(`/api/businesses/${businessId}/billing/invoices`, { page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const invoices = q.data?.items ?? [];
  const loading = q.isLoading;
  return (
    <section className="card overflow-hidden" aria-labelledby="inv-h">
      <h2 id="inv-h" className="flex items-center gap-2 border-b border-line px-5 py-4 font-semibold"><Receipt className="size-4 text-subtle" aria-hidden />ইনভয়েস ও পেমেন্ট</h2>
      {loading ? <div className="p-5"><Skeleton className="h-20" /></div> : invoices.length === 0 ? (
        <p className="px-5 py-8 text-center text-sm text-subtle">এখনও কোনো ইনভয়েস নেই</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[640px] text-left text-sm">
            <thead className="bg-zinc-50 text-xs text-subtle"><tr>
              <th className="px-5 py-2.5 font-medium">ইনভয়েস</th><th className="px-3 py-2.5 font-medium">প্ল্যান</th><th className="px-3 py-2.5 font-medium">মেয়াদ</th>
              <th className="px-3 py-2.5 text-right font-medium">পরিমাণ</th><th className="px-5 py-2.5 font-medium">অবস্থা</th>
            </tr></thead>
            <tbody className="divide-y divide-line">
              {invoices.map((i) => {
                const st = INVOICE_STATUS[i.status];
                const pay = i.payments[0];
                return (
                  <tr key={i.id}>
                    <td className="px-5 py-3"><div className="font-num font-medium">{i.number}</div><div className="text-xs text-subtle">{dateShort(i.createdAt)}</div></td>
                    <td className="px-3 py-3">{i.planName} <span className="text-subtle">· {i.cycle === "yearly" ? "বাৎসরিক" : "মাসিক"}</span></td>
                    <td className="px-3 py-3 text-muted">{i.periodStart && i.periodEnd ? `${dateShort(i.periodStart)} – ${dateShort(i.periodEnd)}` : "—"}</td>
                    <td className="px-3 py-3 text-right font-semibold">{taka(i.total)}</td>
                    <td className="px-5 py-3">
                      <Badge tone={st.tone}>{i.status === "pending_verification" ? <Clock className="size-3" aria-hidden /> : null}{st.label}</Badge>
                      {pay && <div className="mt-0.5 text-xs text-subtle">{METHOD_LABEL[pay.method]}{pay.transactionId ? ` · ${pay.transactionId}` : ""}</div>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {q.data && <Pagination page={page} total={q.data.total} onChange={setPage} scrollTop={false} label="ইনভয়েসের পাতা" className="border-t border-line px-5 pb-4" />}
    </section>
  );
}
