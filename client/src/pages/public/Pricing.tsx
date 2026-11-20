import { useState } from "react";
import { CheckCircle2, ShieldCheck, Smartphone } from "lucide-react";
import type { BillingCycle } from "../../api/types";
import { ErrorBox, LinkButton, Segmented, Skeleton } from "../../components/ui";
import { featureValue, usePlans } from "../../lib/billing";
import { bn, taka } from "../../lib/format";

const FAQ = [
  ["ট্রায়ালের জন্য কি কার্ড বা টাকা লাগবে?", "না। ব্যবসা পেজ যাচাই হয়ে প্রকাশিত হলেই প্রো ট্রায়াল নিজে থেকে চালু হয়। শেষ হলে ফ্রি প্ল্যানে চলে যায় — কোনো টাকা কাটা হয় না।"],
  ["কীভাবে টাকা দেব?", "বিকাশ, নগদ অথবা ব্যাংক ট্রান্সফারে টাকা পাঠিয়ে লেনদেন নম্বর (TrxID) জমা দিন। আমাদের টিম যাচাই করলেই প্ল্যান চালু হয় — সাধারণত ২৪ ঘণ্টার মধ্যে।"],
  ["প্ল্যান কমালে কি তথ্য মুছে যাবে?", "না। আপনার সেবা, গ্রাহক, বুকিং — সব থেকে যায়। শুধু ফ্রি প্ল্যানের সীমার বেশি নতুন কিছু যোগ করা যায় না।"],
  ["টাকা দিলে কি সার্চে আগে দেখাবে?", "না। সার্চের ক্রম রেটিং ও রিভিউ দিয়ে ঠিক হয়, আর যাচাইকৃত ব্যাজ সবার জন্য একই নিয়মে। গ্রাহকের আস্থা বিক্রির জিনিস নয়।"],
];

export default function Pricing() {
  const plans = usePlans();
  const [cycle, setCycle] = useState<BillingCycle>("monthly");
  const c = plans.data;
  return (
    <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
      <div className="mx-auto mb-10 flex max-w-2xl flex-col items-center gap-3 text-center">
        <span className="font-painted text-2xl text-paint-red">ব্যবসার জন্য</span>
        <h1 className="text-[32px] leading-tight font-semibold text-primary sm:text-[40px]">আপনার ব্যবসার মাপে প্ল্যান</h1>
        <p className="text-muted">ফ্রিতে শুরু করুন। প্রকাশিত হলেই {c ? bn(c.trialDays) : "১৪"} দিনের প্রো ট্রায়াল — কোনো কার্ড লাগবে না।</p>
        <Segmented label="বিলিং চক্র" value={cycle} onChange={setCycle} options={[{ value: "monthly", label: "মাসিক" }, { value: "yearly", label: "বাৎসরিক · ২ মাস ফ্রি" }]} />
      </div>
      <ErrorBox error={plans.error} />
      {!c ? <div className="grid gap-4 lg:grid-cols-3">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-96" />)}</div> : (
        <>
          <div className="grid gap-5 lg:grid-cols-3">
            {c.plans.map((pl) => {
              const price = cycle === "yearly" ? pl.priceYearly : pl.priceMonthly;
              const popular = pl.key === "pro";
              return (
                <article key={pl.key} className={`card relative flex flex-col gap-5 p-6 ${popular ? "border-primary/50 shadow-lift" : ""}`}>
                  {popular && <span className="absolute -top-3 left-6 rounded-md bg-primary px-2.5 py-0.5 text-xs font-medium text-white">প্রস্তাবিত</span>}
                  <div>
                    <h2 className="text-xl font-semibold">{pl.nameBn}</h2>
                    {pl.taglineBn && <p className="text-sm text-muted">{pl.taglineBn}</p>}
                  </div>
                  <div>
                    <span className="text-4xl font-semibold text-ink tabular-nums">{price ? taka(price) : "৳০"}</span>
                    {price > 0 && <span className="text-sm text-subtle"> / {cycle === "yearly" ? "বছর" : "মাস"}</span>}
                    {cycle === "yearly" && pl.priceMonthly > 0 && <div className="text-sm text-brand-green">মাসে মাত্র {taka(Math.round(pl.priceYearly / 12))}</div>}
                  </div>
                  <ul className="flex flex-1 flex-col gap-2.5 text-sm">
                    {c.features.map((f) => {
                      const v = pl.entitlements[f.key];
                      const off = f.kind === "switch" && !v;
                      return (
                        <li key={f.key} className={`flex items-start gap-2 ${off ? "text-subtle" : ""}`}>
                          <CheckCircle2 className={`mt-0.5 size-4 shrink-0 ${off ? "text-zinc-300" : "text-brand-green"}`} aria-hidden />
                          <span className={off ? "line-through decoration-zinc-300" : ""}>{f.labelBn}{f.kind === "limit" && <>: <strong>{featureValue(f, v)}</strong></>}</span>
                        </li>
                      );
                    })}
                  </ul>
                  <LinkButton to="/business/new" variant={popular ? "primary" : "outline"} size="lg">{pl.key === "free" ? "ফ্রিতে শুরু করুন" : "শুরু করুন"}</LinkButton>
                </article>
              );
            })}
          </div>

          <div className="mt-8 grid gap-4 sm:grid-cols-2">
            <div className="card flex gap-3 p-5">
              <Smartphone className="mt-0.5 size-5 shrink-0 text-primary" aria-hidden />
              <div><div className="font-semibold">বিকাশ, নগদ বা ব্যাংকে পেমেন্ট</div><p className="text-sm text-muted">টাকা পাঠিয়ে লেনদেন নম্বর জমা দিন — যাচাই হলেই প্ল্যান চালু।</p></div>
            </div>
            <div className="card flex gap-3 p-5">
              <ShieldCheck className="mt-0.5 size-5 shrink-0 text-brand-green" aria-hidden />
              <div><div className="font-semibold">টাকায় র‍্যাংকিং কেনা যায় না</div><p className="text-sm text-muted">যাচাইকৃত ব্যাজ ও সার্চের ক্রম সব প্ল্যানে একই নিয়মে।</p></div>
            </div>
          </div>

          <section className="mx-auto mt-14 max-w-3xl" aria-labelledby="faq-h">
            <h2 id="faq-h" className="mb-4 text-xl font-semibold text-primary">সাধারণ প্রশ্ন</h2>
            <div className="flex flex-col gap-2">
              {FAQ.map(([q, a]) => (
                <details key={q} className="card group p-4 open:shadow-card">
                  <summary className="cursor-pointer list-none font-semibold marker:hidden">{q}</summary>
                  <p className="mt-2 text-sm text-muted">{a}</p>
                </details>
              ))}
            </div>
          </section>
        </>
      )}
    </div>
  );
}
