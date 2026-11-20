import { useState } from "react";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Check } from "lucide-react";
import { api } from "../../api/client";
import type { BusinessDetail } from "../../api/types";
import { Logo } from "../../components/Layouts";
import { flattenCategories, useCategories, ZonePicker } from "../../components/shared";
import { Button, ErrorBox, Field } from "../../components/ui";
import { bn } from "../../lib/format";
import { HoursEditor, defaultWeek, hoursFromWeek } from "./HoursEditor";

const STEPS = ["মৌলিক তথ্য", "অবস্থান", "কাজের সময়"];

/**
 * Business onboarding. The draft is created after step 1, so later steps auto-save and the
 * owner can leave and continue from the workspace at any time.
 */
export default function CreateBusiness() {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const categories = useCategories();
  const [step, setStep] = useState(0);
  const [businessId, setBusinessId] = useState<string | null>(null);
  const [basic, setBasic] = useState({ name: "", categoryId: "", shortDescription: "", phone: "" });
  const [location, setLocation] = useState({ zoneId: null as number | null, addressLine: "", offersHomeService: false, offersEmergencyService: false });
  const [week, setWeek] = useState(defaultWeek);

  const createDraft = useMutation({
    mutationFn: () => api.post<BusinessDetail>("/api/businesses", {
      name: basic.name, categoryId: Number(basic.categoryId), shortDescription: basic.shortDescription || null, phone: basic.phone || null,
    }),
    onSuccess: (b) => { setBusinessId(b.id); qc.invalidateQueries({ queryKey: ["workspaces"] }); setStep(1); },
  });
  const saveBasic = useMutation({
    mutationFn: () => api.patch(`/api/businesses/${businessId}`, {
      name: basic.name, categoryId: Number(basic.categoryId), shortDescription: basic.shortDescription, phone: basic.phone, onboardingStep: 2,
    }),
    onSuccess: () => setStep(1),
  });
  const saveLocation = useMutation({
    mutationFn: () => api.patch(`/api/businesses/${businessId}`, { ...location, onboardingStep: 3 }),
    onSuccess: () => setStep(2),
  });
  const saveHours = useMutation({
    mutationFn: async () => {
      await api.put(`/api/businesses/${businessId}/hours`, hoursFromWeek(week));
      await api.patch(`/api/businesses/${businessId}`, { onboardingStep: 4 });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["business", businessId] });
      navigate(`/w/${businessId}/services?welcome=1`);
    },
  });

  const error = createDraft.error ?? saveBasic.error ?? saveLocation.error ?? saveHours.error;

  return (
    <div className="min-h-dvh bg-bg">
      <header className="border-b border-line bg-surface">
        <div className="mx-auto flex max-w-3xl items-center justify-between gap-4 px-4 py-3"><Logo to="/me/bookings" />
          {businessId && <span className="text-sm text-subtle">খসড়া স্বয়ংক্রিয়ভাবে সংরক্ষিত</span>}
        </div>
      </header>
      <main className="mx-auto max-w-3xl px-4 py-8">
        <h1 className="text-2xl font-semibold">আপনার ব্যবসা পেজ তৈরি করুন</h1>
        <p className="mt-1 mb-6 text-muted">ব্যবসা আপনার অ্যাকাউন্টের একটি ওয়ার্কস্পেস — আলাদা অ্যাকাউন্ট লাগবে না।</p>
        <ol className="mb-6 flex gap-2" aria-label="ধাপ">
          {STEPS.map((s, i) => (
            <li key={s} aria-current={i === step ? "step" : undefined} className="flex flex-1 flex-col gap-1.5">
              <span className={`h-1.5 rounded-full ${i <= step ? "bg-primary" : "bg-line"}`} />
              <span className={`flex items-center gap-1 text-sm ${i === step ? "font-semibold text-primary-hover" : "text-subtle"}`}>
                {i < step && <Check className="size-4" aria-hidden />}{bn(i + 1)}. {s}
              </span>
            </li>
          ))}
        </ol>

        <section className="card flex flex-col gap-5 p-6 sm:p-8">
          <ErrorBox error={error} />
          {step === 0 && (
            <>
              <Field label="ব্যবসার নাম">{(id) => <input id={id} className="input" value={basic.name} onChange={(e) => setBasic({ ...basic, name: e.target.value })} placeholder="যেমন: রহমান ইলেকট্রিক সার্ভিস" />}</Field>
              <Field label="ক্যাটাগরি">{(id) => (
                <select id={id} className="input" value={basic.categoryId} onChange={(e) => setBasic({ ...basic, categoryId: e.target.value })}>
                  <option value="">নির্বাচন করুন</option>
                  {flattenCategories(categories.data).map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}
                </select>
              )}</Field>
              <Field label="সংক্ষিপ্ত বিবরণ" hint="গ্রাহকরা সার্চ ফলাফলে এটি দেখবেন">{(id) => <textarea id={id} className="input" rows={3} maxLength={300} value={basic.shortDescription} onChange={(e) => setBasic({ ...basic, shortDescription: e.target.value })} placeholder="যেমন: সব ব্র্যান্ডের এসি সার্ভিসিং, গ্যাস চার্জ ও ইনস্টলেশন" />}</Field>
              <Field label="ব্যবসার মোবাইল নম্বর" optional hint="গ্রাহকদের দেখানো হয় না — যাচাইকরণের জন্য">{(id) => <input id={id} className="input" type="tel" value={basic.phone} onChange={(e) => setBasic({ ...basic, phone: e.target.value })} />}</Field>
              <div className="flex justify-end">
                <Button disabled={basic.name.trim().length < 2 || !basic.categoryId} loading={createDraft.isPending || saveBasic.isPending}
                  onClick={() => (businessId ? saveBasic.mutate() : createDraft.mutate())}>পরবর্তী ধাপ →</Button>
              </div>
            </>
          )}
          {step === 1 && (
            <>
              <fieldset><legend className="mb-2 font-medium">এলাকা</legend>
                <ZonePicker value={location.zoneId} minDepth={2} onChange={(zoneId) => setLocation({ ...location, zoneId })} />
                <p className="mt-2 text-sm text-subtle">যত নির্দিষ্ট এলাকা দেবেন, গ্রাহক তত সহজে খুঁজে পাবেন। যাচাইয়ের জন্য এলাকার পরিদর্শক নির্ধারিত হয়।</p>
              </fieldset>
              <Field label="ঠিকানা">{(id) => <input id={id} className="input" value={location.addressLine} onChange={(e) => setLocation({ ...location, addressLine: e.target.value })} placeholder="যেমন: বাসা ১২, রোড ৫, সেক্টর ৭" />}</Field>
              <div className="flex flex-col gap-1">
                {([["offersHomeService", "বাসায় গিয়ে সেবা দিই (হোম সার্ভিস)"], ["offersEmergencyService", "জরুরি সেবা দিই"]] as const).map(([k, label]) => (
                  <label key={k} className="flex min-h-11 items-center gap-2.5">
                    <input type="checkbox" className="size-5 accent-primary" checked={location[k]} onChange={(e) => setLocation({ ...location, [k]: e.target.checked })} />{label}
                  </label>
                ))}
              </div>
              <div className="flex justify-between">
                <Button variant="outline" onClick={() => setStep(0)}>← আগের ধাপ</Button>
                <Button disabled={!location.zoneId || location.addressLine.trim().length < 3} loading={saveLocation.isPending} onClick={() => saveLocation.mutate()}>পরবর্তী ধাপ →</Button>
              </div>
            </>
          )}
          {step === 2 && (
            <>
              <p className="text-muted">গ্রাহকরা শুধু এই সময়ের মধ্যেই বুকিং দিতে পারবেন।</p>
              <HoursEditor week={week} onChange={setWeek} />
              <div className="flex justify-between">
                <Button variant="outline" onClick={() => setStep(1)}>← আগের ধাপ</Button>
                <Button disabled={!week.some((d) => d.open)} loading={saveHours.isPending} onClick={() => saveHours.mutate()}>সেবা যোগ করুন →</Button>
              </div>
            </>
          )}
        </section>
      </main>
    </div>
  );
}
