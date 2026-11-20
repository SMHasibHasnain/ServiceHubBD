import { useState } from "react";
import { useNavigate } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BadgeCheck } from "lucide-react";
import { api } from "../../api/client";
import type { Invitation, Member, User } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { ZonePicker } from "../../components/shared";
import { Badge, Button, ErrorBox, PageHeader, Pagination, useClientPage, useToast } from "../../components/ui";

export default function Profile() {
  const { user, setUser } = useAuth();
  const toast = useToast();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [form, setForm] = useState({ fullName: user?.fullName ?? "", addressLine: "", zoneId: null as number | null });
  const [otp, setOtp] = useState<string | null>(null);
  const [code, setCode] = useState("");

  const save = useMutation({
    mutationFn: () => api.patch<User>("/api/me", { fullName: form.fullName, addressLine: form.addressLine || undefined, zoneId: form.zoneId ?? undefined }),
    onSuccess: (u) => { setUser(u); toast("প্রোফাইল সফলভাবে হালনাগাদ হয়েছে।"); },
  });
  const sendOtp = useMutation({
    mutationFn: () => api.post("/api/auth/otp", { phone: user!.phone, purpose: "phone_verification" }),
    onSuccess: () => { setOtp(""); toast("আপনার মোবাইলে একটি কোড পাঠানো হয়েছে।"); },
  });
  const verify = useMutation({
    mutationFn: () => api.post<User>("/api/auth/verify-phone", { code: otp }),
    onSuccess: (u) => { setUser(u); setOtp(null); toast("মোবাইল নম্বর যাচাই হয়েছে।"); },
  });

  const invitations = useQuery({ queryKey: ["me", "invitations"], queryFn: () => api.get<Invitation[]>("/api/me/invitations") });
  const invitePage = useClientPage(invitations.data, null, 5);
  const accept = useMutation({
    mutationFn: () => api.post<Member>("/api/invitations/accept", { token: code.trim() }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["workspaces"] });
      qc.invalidateQueries({ queryKey: ["me", "invitations"] });
      toast("আপনি টিমে যোগ দিয়েছেন। ওয়ার্কস্পেস মেনু থেকে ব্যবসাটি খুলুন।");
      setCode("");
      navigate("/me/bookings");
    },
  });

  return (
    <>
      <PageHeader title="প্রোফাইল" subtitle="আপনার ব্যক্তিগত তথ্য — সব ওয়ার্কস্পেসে একই অ্যাকাউন্ট" />
      <div className="grid gap-6 xl:grid-cols-2">
        <section className="card flex flex-col gap-4 p-6">
          <h2 className="text-base font-semibold">ব্যক্তিগত তথ্য</h2>
          <ErrorBox error={save.error} />
          <label className="label">পূর্ণ নাম<input className="input" value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} /></label>
          <div className="flex flex-col gap-1.5">
            <span className="font-medium">মোবাইল নম্বর</span>
            <div className="flex flex-wrap items-center gap-3">
              <span className="font-num">{user?.phone}</span>
              {user?.phoneVerified ? <Badge tone="teal"><BadgeCheck className="size-4" aria-hidden />যাচাইকৃত</Badge>
                : <Button size="sm" variant="outline" loading={sendOtp.isPending} onClick={() => sendOtp.mutate()}>যাচাই করুন</Button>}
            </div>
            {otp !== null && (
              <div className="mt-2 flex flex-wrap gap-2">
                <input className="input max-w-48 font-num tracking-widest" inputMode="numeric" maxLength={6} value={otp} onChange={(e) => setOtp(e.target.value)} placeholder="৬ সংখ্যার কোড" aria-label="যাচাই কোড" />
                <Button loading={verify.isPending} disabled={otp.length !== 6} onClick={() => verify.mutate()}>নিশ্চিত করুন</Button>
              </div>
            )}
            <ErrorBox error={sendOtp.error ?? verify.error} />
          </div>
          <label className="label">ঠিকানা <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>
            <input className="input" value={form.addressLine} onChange={(e) => setForm({ ...form, addressLine: e.target.value })} placeholder="যেমন: বাসা ১২, রোড ৫" />
          </label>
          <fieldset><legend className="mb-1.5 font-medium">পছন্দের এলাকা</legend><ZonePicker value={form.zoneId} minDepth={0} onChange={(zoneId) => setForm({ ...form, zoneId })} /></fieldset>
          <div><Button loading={save.isPending} onClick={() => save.mutate()}>সংরক্ষণ করুন</Button></div>
        </section>

        <section className="card flex flex-col gap-4 p-6">
          <h2 className="text-base font-semibold">টিম আমন্ত্রণ</h2>
          <p className="text-muted">কোনো ব্যবসা আপনাকে টিম সদস্য হিসেবে যোগ করলে SMS-এ একটি কোড পাবেন। এখানে দিন।</p>
          {invitePage.items.map((i) => (
            <div key={i.id} className="rounded-md border border-line p-4">
              <div className="font-semibold">{i.businessName}</div>
              <div className="text-[15px] text-muted">ভূমিকা: {i.roleName}</div>
            </div>
          ))}
          <Pagination page={invitePage.page} total={invitePage.total} pageSize={invitePage.pageSize} onChange={invitePage.setPage} scrollTop={false} label="আমন্ত্রণের পাতা" className="mt-0" />
          <ErrorBox error={accept.error} />
          <div className="flex flex-wrap gap-2">
            <input className="input flex-1" value={code} onChange={(e) => setCode(e.target.value)} placeholder="আমন্ত্রণ কোড" aria-label="আমন্ত্রণ কোড" />
            <Button disabled={!code.trim()} loading={accept.isPending} onClick={() => accept.mutate()}>যোগ দিন</Button>
          </div>
        </section>
      </div>
    </>
  );
}
