import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ClipboardCheck, MapPin, Navigation } from "lucide-react";
import { api } from "../../api/client";
import type { InspectionItem, InspectionOutcome, InspectionStatus, Paged } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Badge, Button, EmptyState, ErrorBox, LoadingList, Modal, PageHeader, Segmented, useToast } from "../../components/ui";
import { dateTime, relative } from "../../lib/format";

const TYPE = { verification_visit: "যাচাইকরণ পরিদর্শন", complaint_followup: "অভিযোগের তদন্ত", routine_audit: "নিয়মিত পরিদর্শন" };
const OUTCOMES: { value: InspectionOutcome; label: string }[] = [
  { value: "passed", label: "উত্তীর্ণ" }, { value: "failed", label: "অনুত্তীর্ণ" }, { value: "needs_followup", label: "পুনরায় দেখা প্রয়োজন" }, { value: "business_not_found", label: "ব্যবসা পাওয়া যায়নি" },
];
const CHECKLIST = [["signboard", "সাইনবোর্ড আছে"], ["address_matches", "ঠিকানা মিলেছে"], ["owner_met", "মালিকের সাথে দেখা হয়েছে"], ["license_sighted", "লাইসেন্স দেখা হয়েছে"]] as const;

/** Best-effort GPS fix; inspections still work without location permission. */
function locate(): Promise<{ latitude: number; longitude: number } | null> {
  return new Promise((resolve) => {
    if (!navigator.geolocation) return resolve(null);
    navigator.geolocation.getCurrentPosition(
      (p) => resolve({ latitude: Number(p.coords.latitude.toFixed(6)), longitude: Number(p.coords.longitude.toFixed(6)) }),
      () => resolve(null), { timeout: 8000 });
  });
}

export default function Inspections() {
  const { workspaces } = useAuth();
  const zones = workspaces.filter((w) => w.type === "inspection").map((w) => w.name.replace("পরিদর্শন — ", "")).join(", ");
  const [status, setStatus] = useState<"" | InspectionStatus>("");
  const [open, setOpen] = useState<InspectionItem | null>(null);
  const list = useQuery({
    queryKey: ["inspections", status],
    queryFn: () => api.get<Paged<InspectionItem>>("/api/inspections", { status, pageSize: 100 }),
  });

  return (
    <>
      <PageHeader title="মাঠ পরিদর্শন" subtitle={zones ? `আপনার এলাকা: ${zones}` : "আপনার এলাকার পরিদর্শন কাজ"} />
      <div className="mb-5"><Segmented label="অবস্থা" value={status} onChange={setStatus} options={[{ value: "", label: "চলমান" }, { value: "completed", label: "সম্পন্ন" }]} /></div>
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <ul className="flex flex-col gap-3">
          {list.data.items.map((i) => (
            <li key={i.id}>
              <button onClick={() => setOpen(i)} className="card flex w-full flex-wrap items-center gap-4 p-4 text-left hover:border-zinc-400">
                <span className="flex size-11 items-center justify-center rounded-md bg-orange-100 text-orange-800"><ClipboardCheck className="size-5" aria-hidden /></span>
                <div className="min-w-0 flex-[1_1_220px]">
                  <div className="font-semibold">{i.businessName}</div>
                  <div className="flex items-center gap-1 text-[15px] text-muted"><MapPin className="size-4" aria-hidden />{i.addressLine ?? "—"}, {i.zoneName}</div>
                </div>
                <Badge tone="violet">{TYPE[i.type]}</Badge>
                {!i.inspectorId && <Badge tone="amber">অনির্ধারিত</Badge>}
                <Badge tone={i.status === "completed" ? "green" : i.status === "in_progress" ? "blue" : "neutral"}>{{ scheduled: "নির্ধারিত", in_progress: "চলছে", completed: "সম্পন্ন", cancelled: "বাতিল" }[i.status]}</Badge>
                <span className="text-sm text-subtle">{relative(i.createdAt)}</span>
              </button>
            </li>
          ))}
        </ul>
      ) : <EmptyState icon={<ClipboardCheck className="size-8" aria-hidden />} title="এই মুহূর্তে কোনো পরিদর্শন নেই।" />}
      {open && <InspectionPanel item={open} onClose={() => setOpen(null)} />}
    </>
  );
}

function InspectionPanel({ item, onClose }: { item: InspectionItem; onClose: () => void }) {
  const { user } = useAuth();
  const qc = useQueryClient();
  const toast = useToast();
  const [i, setI] = useState(item);
  const [outcome, setOutcome] = useState<InspectionOutcome>("passed");
  const [findings, setFindings] = useState("");
  const [checks, setChecks] = useState<Record<string, boolean>>({});
  const done = (next: InspectionItem, msg: string) => { setI(next); qc.invalidateQueries({ queryKey: ["inspections"] }); toast(msg); };

  const claim = useMutation({ mutationFn: () => api.post<InspectionItem>(`/api/inspections/${i.id}/claim`), onSuccess: (n) => done(n, "পরিদর্শনটি আপনার তালিকায় যুক্ত হয়েছে।") });
  const start = useMutation({ mutationFn: async () => api.post<InspectionItem>(`/api/inspections/${i.id}/start`, (await locate()) ?? {}), onSuccess: (n) => done(n, "পরিদর্শন শুরু হয়েছে।") });
  const submit = useMutation({
    mutationFn: async () => api.post<InspectionItem>(`/api/inspections/${i.id}/submit`, { outcome, findings, checklist: checks, ...((await locate()) ?? {}) }),
    onSuccess: (n) => { done(n, "পরিদর্শনের প্রতিবেদন জমা হয়েছে।"); onClose(); },
  });
  const mine = i.inspectorId === user?.id;

  return (
    <Modal open onClose={onClose} title={i.businessName} wide
      footer={i.status === "completed" ? <Button onClick={onClose}>বন্ধ করুন</Button>
        : !i.inspectorId ? <Button loading={claim.isPending} onClick={() => claim.mutate()}>আমি নেব</Button>
        : mine && i.status === "scheduled" ? <Button loading={start.isPending} onClick={() => start.mutate()}><Navigation className="size-4" aria-hidden />পরিদর্শন শুরু করুন</Button>
        : mine ? <Button disabled={findings.trim().length < 10} loading={submit.isPending} onClick={() => submit.mutate()}>প্রতিবেদন জমা দিন</Button> : null}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={claim.error ?? start.error ?? submit.error} />
        <dl className="grid gap-3 sm:grid-cols-2">
          <div className="rounded-md bg-bg p-3"><dt className="text-sm text-subtle">ধরন</dt><dd className="font-semibold">{TYPE[i.type]}</dd></div>
          <div className="rounded-md bg-bg p-3"><dt className="text-sm text-subtle">ঠিকানা</dt><dd className="font-semibold">{i.addressLine}, {i.zoneName}</dd></div>
          {i.businessPhone && <div className="rounded-md bg-bg p-3"><dt className="text-sm text-subtle">ব্যবসার মোবাইল</dt><dd><a className="font-num font-semibold text-primary hover:text-primary-hover" href={`tel:${i.businessPhone}`}>{i.businessPhone}</a></dd></div>}
          {i.scheduledFor && <div className="rounded-md bg-bg p-3"><dt className="text-sm text-subtle">নির্ধারিত সময়</dt><dd className="font-semibold">{dateTime(i.scheduledFor)}</dd></div>}
        </dl>
        {i.status === "completed" ? (
          <div className="rounded-md bg-green-50 p-4"><div className="font-semibold">ফলাফল: {OUTCOMES.find((o) => o.value === i.outcome)?.label}</div><p>{i.findings}</p></div>
        ) : mine && i.status === "in_progress" ? (
          <>
            <fieldset className="flex flex-col"><legend className="mb-1 font-medium">চেকলিস্ট</legend>
              {CHECKLIST.map(([k, label]) => (
                <label key={k} className="flex min-h-11 items-center gap-2.5"><input type="checkbox" className="size-5 accent-primary" checked={!!checks[k]} onChange={(e) => setChecks({ ...checks, [k]: e.target.checked })} />{label}</label>
              ))}
            </fieldset>
            <label className="label">ফলাফল<select className="input" value={outcome} onChange={(e) => setOutcome(e.target.value as InspectionOutcome)}>{OUTCOMES.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}</select></label>
            <label className="label">পর্যবেক্ষণ <span className="text-sm font-normal text-subtle">(কমপক্ষে ১০ অক্ষর)</span>
              <textarea className="input" rows={4} value={findings} onChange={(e) => setFindings(e.target.value)} placeholder="যেমন: সাইনবোর্ড আছে, ঠিকানা মিলেছে, মালিকের সাথে কথা হয়েছে।" />
            </label>
            <p className="text-sm text-subtle">জমা দেওয়ার সময় আপনার অবস্থান (GPS) প্রমাণ হিসেবে সংরক্ষিত হবে, যদি অনুমতি দেন। সিদ্ধান্ত নেবেন মডারেটর।</p>
          </>
        ) : null}
      </div>
    </Modal>
  );
}
