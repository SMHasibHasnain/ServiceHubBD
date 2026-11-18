import { useEffect, useState } from "react";
import { useNavigate } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Trash2 } from "lucide-react";
import { api } from "../../api/client";
import type { BookingSettings, DocumentType, VerificationSummary } from "../../api/types";
import { flattenCategories, useCategories, useZones, ZonePicker } from "../../components/shared";
import { Badge, Button, ErrorBox, Modal, PageHeader, Skeleton, useToast } from "../../components/ui";
import { DOCUMENT_TYPES, VERIFICATION_STATUS, bn, dateShort } from "../../lib/format";
import { useBiz } from "./Dashboard";
import { HoursEditor, hoursFromWeek, weekFromHours } from "./HoursEditor";

export default function Settings() {
  const biz = useBiz();
  if (!biz) return <Skeleton className="h-96" />;
  return (
    <>
      <PageHeader title="ব্যবসার সেটিংস" subtitle="এখানে পরিবর্তন করলে পাবলিক পেজে দেখা যাবে" />
      <div className="flex flex-col gap-6">
        <ProfileSection key={`p-${biz.id}`} />
        <HoursSection key={`h-${biz.id}`} />
        <BookingSettingsSection key={`b-${biz.id}`} />
        <VerificationSection key={`v-${biz.id}`} />
        {biz.myPermissions.includes("business.delete") && <DangerSection />}
      </div>
    </>
  );
}

function useSaved(message = "তথ্য সংরক্ষিত হয়েছে।") {
  const qc = useQueryClient();
  const biz = useBiz();
  const toast = useToast();
  return () => { qc.invalidateQueries({ queryKey: ["business", biz?.id] }); qc.invalidateQueries({ queryKey: ["workspaces"] }); toast(message); };
}

function ProfileSection() {
  const biz = useBiz()!;
  const categories = useCategories();
  const saved = useSaved();
  const zone = useQuery({ queryKey: ["zone", biz.zoneId], queryFn: () => api.get<{ path: string }>(`/api/zones/${biz.zoneId}`), enabled: !!biz.zoneId });
  const [f, setF] = useState({
    name: biz.name, categoryId: String(biz.categoryId), tagline: biz.tagline ?? "", shortDescription: biz.shortDescription ?? "",
    description: biz.description ?? "", addressLine: biz.addressLine ?? "", zoneId: biz.zoneId, coverageNote: biz.coverageNote ?? "",
    phone: biz.phone ?? "", email: biz.email ?? "", websiteUrl: biz.websiteUrl ?? "", facebookUrl: biz.facebookUrl ?? "",
    logoUrl: biz.logoUrl ?? "", coverUrl: biz.coverUrl ?? "", establishedYear: biz.establishedYear?.toString() ?? "",
    offersHomeService: biz.offersHomeService, offersEmergencyService: biz.offersEmergencyService,
  });
  useZones(null); // warm the division list
  const save = useMutation({
    mutationFn: () => api.patch(`/api/businesses/${biz.id}`, {
      ...f, categoryId: Number(f.categoryId), zoneId: f.zoneId ?? undefined,
      establishedYear: f.establishedYear ? Number(f.establishedYear) : undefined,
      email: f.email || undefined,
    }),
    onSuccess: saved,
  });
  const input = (k: keyof typeof f, label: string, props: Record<string, unknown> = {}) => (
    <label className="label">{label}<input className="input" value={f[k] as string} onChange={(e) => setF({ ...f, [k]: e.target.value })} {...props} /></label>
  );

  return (
    <section className="card flex flex-col gap-4 p-6">
      <h2 className="text-base font-semibold">ব্যবসার তথ্য</h2>
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-2">
        {input("name", "ব্যবসার নাম")}
        <label className="label">ক্যাটাগরি
          <select className="input" value={f.categoryId} onChange={(e) => setF({ ...f, categoryId: e.target.value })}>
            {flattenCategories(categories.data).map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}
          </select>
        </label>
      </div>
      {input("tagline", "ট্যাগলাইন", { placeholder: "এক লাইনে আপনার ব্যবসা" })}
      <label className="label">সংক্ষিপ্ত বিবরণ<textarea className="input" rows={2} maxLength={300} value={f.shortDescription} onChange={(e) => setF({ ...f, shortDescription: e.target.value })} /></label>
      <label className="label">বিস্তারিত বিবরণ<textarea className="input" rows={4} value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} placeholder="অভিজ্ঞতা, বিশেষত্ব, সেবা এলাকা..." /></label>
      <fieldset><legend className="mb-2 font-medium">এলাকা</legend>
        {(!biz.zoneId || zone.data) && <ZonePicker value={f.zoneId} minDepth={2} initialPath={zone.data?.path} onChange={(zoneId) => setF({ ...f, zoneId })} />}
      </fieldset>
      <div className="grid gap-4 sm:grid-cols-2">
        {input("addressLine", "ঠিকানা")}
        {input("coverageNote", "সেবা এলাকা", { placeholder: "যেমন: উত্তরা, তুরাগ, খিলক্ষেত" })}
        {input("phone", "মোবাইল (গোপন)", { type: "tel" })}
        {input("email", "ইমেইল", { type: "email" })}
        {input("websiteUrl", "ওয়েবসাইট", { type: "url" })}
        {input("facebookUrl", "ফেসবুক পেজ", { type: "url" })}
        {input("logoUrl", "লোগোর ছবির লিংক", { type: "url", placeholder: "https://..." })}
        {input("coverUrl", "কভার ছবির লিংক", { type: "url", placeholder: "https://..." })}
        {input("establishedYear", "প্রতিষ্ঠার বছর", { type: "number", min: 1950, max: 2100 })}
      </div>
      <div className="flex flex-col">
        {([["offersHomeService", "হোম সার্ভিস দিই"], ["offersEmergencyService", "জরুরি সেবা দিই"]] as const).map(([k, label]) => (
          <label key={k} className="flex min-h-11 items-center gap-2.5"><input type="checkbox" className="size-5 accent-primary" checked={f[k]} onChange={(e) => setF({ ...f, [k]: e.target.checked })} />{label}</label>
        ))}
      </div>
      <div><Button loading={save.isPending} onClick={() => save.mutate()}>সংরক্ষণ করুন</Button></div>
    </section>
  );
}

function HoursSection() {
  const biz = useBiz()!;
  const saved = useSaved("কাজের সময় সংরক্ষিত হয়েছে।");
  const [week, setWeek] = useState(() => weekFromHours(biz.hours));
  const save = useMutation({ mutationFn: () => api.put(`/api/businesses/${biz.id}/hours`, hoursFromWeek(week)), onSuccess: saved });
  return (
    <section className="card flex flex-col gap-4 p-6">
      <h2 className="text-base font-semibold">কাজের সময়</h2>
      <ErrorBox error={save.error} />
      <HoursEditor week={week} onChange={setWeek} />
      <div><Button loading={save.isPending} onClick={() => save.mutate()}>সংরক্ষণ করুন</Button></div>
    </section>
  );
}

function BookingSettingsSection() {
  const biz = useBiz()!;
  const saved = useSaved("বুকিং সেটিংস সংরক্ষিত হয়েছে।");
  const [s, setS] = useState<BookingSettings>(biz.bookingSettings);
  const save = useMutation({ mutationFn: () => api.put(`/api/businesses/${biz.id}/booking-settings`, s), onSuccess: saved });
  const num = (k: keyof BookingSettings, label: string, hint: string) => (
    <label className="label">{label}<input className="input" type="number" min={0} value={s[k] as number} onChange={(e) => setS({ ...s, [k]: Number(e.target.value) })} /><span className="text-sm font-normal text-subtle">{hint}</span></label>
  );
  return (
    <section className="card flex flex-col gap-4 p-6">
      <h2 className="text-base font-semibold">বুকিং সেটিংস</h2>
      <ErrorBox error={save.error} />
      <label className="flex min-h-11 items-center gap-2.5"><input type="checkbox" className="size-5 accent-primary" checked={s.autoConfirm} onChange={(e) => setS({ ...s, autoConfirm: e.target.checked })} />নতুন বুকিং স্বয়ংক্রিয়ভাবে নিশ্চিত করুন</label>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {num("slotIntervalMinutes", "সময়ের ব্যবধান (মিনিট)", "প্রতি কত মিনিটে একটি স্লট")}
        {num("minNoticeMinutes", "ন্যূনতম আগাম সময় (মিনিট)", "এর আগে বুকিং নেওয়া হবে না")}
        {num("maxAdvanceDays", "সর্বোচ্চ কত দিন আগে", "ভবিষ্যতের বুকিং সীমা")}
        {num("bufferMinutes", "দুই বুকিংয়ের মাঝে বিরতি (মিনিট)", "যাতায়াত বা প্রস্তুতি")}
        {num("maxConcurrentBookings", "একই সময়ে সর্বোচ্চ বুকিং", "টিমে কতজন একসাথে কাজ করেন")}
      </div>
      <label className="label">বাতিলের নীতি<textarea className="input" rows={2} value={s.cancellationPolicy ?? ""} onChange={(e) => setS({ ...s, cancellationPolicy: e.target.value })} placeholder="যেমন: অ্যাপয়েন্টমেন্টের ২ ঘণ্টা আগ পর্যন্ত বিনামূল্যে বাতিল" /></label>
      <div><Button loading={save.isPending} onClick={() => save.mutate()}>সংরক্ষণ করুন</Button></div>
    </section>
  );
}

function VerificationSection() {
  const biz = useBiz()!;
  const saved = useSaved("আপনার যাচাইকরণের আবেদন জমা হয়েছে।");
  const history = useQuery({ queryKey: ["business", biz.id, "verification"], queryFn: () => api.get<VerificationSummary[]>(`/api/businesses/${biz.id}/verification`) });
  const [docs, setDocs] = useState<{ documentType: DocumentType; fileUrl: string }[]>([{ documentType: "trade_license", fileUrl: "" }]);
  const canSubmit = biz.myPermissions.includes("business.verification.submit") && ["draft", "rejected", "info_requested"].includes(biz.verificationStatus);
  const submit = useMutation({
    mutationFn: () => api.post(`/api/businesses/${biz.id}/verification`, { documents: docs.filter((d) => d.fileUrl.trim()) }),
    onSuccess: () => { saved(); history.refetch(); },
  });
  useEffect(() => { if (window.location.hash === "#verification") document.getElementById("verification")?.scrollIntoView({ behavior: "smooth" }); }, []);
  const latest = history.data?.[0];

  return (
    <section id="verification" className="card flex scroll-mt-24 flex-col gap-4 p-6">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-base font-semibold">যাচাইকরণ</h2>
        <Badge tone={VERIFICATION_STATUS[biz.verificationStatus].tone}>{VERIFICATION_STATUS[biz.verificationStatus].label}</Badge>
      </div>
      {latest?.decisionReason && (latest.status === "rejected" || latest.status === "info_requested") && (
        <div className="rounded-md border border-line bg-zinc-50 p-4"><div className="font-semibold">মডারেটরের মন্তব্য</div><p>{latest.decisionReason}</p></div>
      )}
      {canSubmit ? (
        <>
          <p className="text-muted">ট্রেড লাইসেন্স, জাতীয় পরিচয়পত্র বা অন্য প্রমাণপত্রের লিংক দিন। ফাইল আপলোড শীঘ্রই আসছে।</p>
          <ErrorBox error={submit.error} />
          {docs.map((d, i) => (
            <div key={i} className="flex flex-wrap gap-2">
              <select className="input w-auto" value={d.documentType} onChange={(e) => setDocs(docs.map((x, j) => (j === i ? { ...x, documentType: e.target.value as DocumentType } : x)))} aria-label="ডকুমেন্টের ধরন">
                {Object.entries(DOCUMENT_TYPES).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
              </select>
              <input className="input min-w-56 flex-1" type="url" value={d.fileUrl} onChange={(e) => setDocs(docs.map((x, j) => (j === i ? { ...x, fileUrl: e.target.value } : x)))} placeholder="https://drive.google.com/..." aria-label="ডকুমেন্টের লিংক" />
              {docs.length > 1 && <Button variant="ghost" onClick={() => setDocs(docs.filter((_, j) => j !== i))} aria-label="সরান"><Trash2 className="size-4" aria-hidden /></Button>}
            </div>
          ))}
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={() => setDocs([...docs, { documentType: "national_id", fileUrl: "" }])}><Plus className="size-4" aria-hidden />আরেকটি ডকুমেন্ট</Button>
            <Button loading={submit.isPending} disabled={!docs.some((d) => d.fileUrl.trim())} onClick={() => submit.mutate()}>যাচাইকরণের জন্য জমা দিন</Button>
          </div>
        </>
      ) : biz.verificationStatus !== "approved" && <p className="text-muted">আপনার আবেদন পর্যালোচনায় আছে। সিদ্ধান্ত হলে নোটিফিকেশন পাবেন।</p>}
      {!!history.data?.length && (
        <ul className="divide-y divide-zinc-100 rounded-md border border-line text-[15px]">
          {history.data.map((v) => (
            <li key={v.id} className="flex flex-wrap items-center gap-3 px-4 py-2.5">
              <span className="font-semibold">আবেদন #{bn(v.attemptNo)}</span><span className="text-subtle">{dateShort(v.submittedAt)}</span>
              <Badge tone={VERIFICATION_STATUS[v.status].tone} className="ml-auto text-xs">{VERIFICATION_STATUS[v.status].label}</Badge>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function DangerSection() {
  const biz = useBiz()!;
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const del = useMutation({
    mutationFn: () => api.del(`/api/businesses/${biz.id}`),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["workspaces"] }); navigate("/me/bookings"); },
  });
  return (
    <section className="card flex flex-col gap-3 border-red-200 p-6">
      <h2 className="text-base font-semibold text-red-800">ব্যবসা মুছে ফেলুন</h2>
      <p className="text-muted">পেজটি সবার কাছ থেকে সরিয়ে ফেলা হবে। আগের বুকিং ও রিভিউ ইতিহাসে থেকে যাবে।</p>
      <div><Button variant="danger" onClick={() => setOpen(true)}>ব্যবসা মুছে ফেলুন</Button></div>
      <Modal open={open} onClose={() => setOpen(false)} title="আপনি কি নিশ্চিত?"
        footer={<><Button variant="ghost" onClick={() => setOpen(false)}>বাতিল</Button><Button variant="danger" loading={del.isPending} onClick={() => del.mutate()}>মুছে ফেলুন</Button></>}>
        <ErrorBox error={del.error} />
        <p>“{biz.name}” মুছে ফেললে তা পুনরুদ্ধার করা যাবে না।</p>
      </Modal>
    </section>
  );
}
