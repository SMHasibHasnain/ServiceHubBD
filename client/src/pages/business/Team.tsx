import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { UserPlus } from "lucide-react";
import { api } from "../../api/client";
import type { Invitation, Member, Role } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Avatar, Badge, Button, ErrorBox, LoadingList, Modal, PageHeader, useToast } from "../../components/ui";
import { dateShort } from "../../lib/format";
import { useBiz } from "./Dashboard";

const ROLE_HELP: Record<string, string> = {
  business_manager: "বুকিং, সেবা, গ্রাহক ও টিম পরিচালনা",
  business_staff: "নিজের বরাদ্দ অ্যাপয়েন্টমেন্ট দেখা ও সম্পন্ন করা",
  page_moderator: "রিভিউর উত্তর ও রিপোর্ট",
  business_analyst: "শুধু দেখা — ড্যাশবোর্ড ও বিশ্লেষণ",
};

export default function Team() {
  const biz = useBiz();
  const { user } = useAuth();
  const qc = useQueryClient();
  const toast = useToast();
  const [inviteOpen, setInviteOpen] = useState(false);
  const [removing, setRemoving] = useState<Member | null>(null);
  const canManage = !!biz?.myPermissions.includes("members.manage");

  const roles = useQuery({ queryKey: ["roles", "business"], queryFn: () => api.get<Role[]>("/api/roles", { scope: "business" }) });
  const members = useQuery({ queryKey: ["business", biz?.id, "members"], queryFn: () => api.get<Member[]>(`/api/businesses/${biz!.id}/members`), enabled: !!biz });
  const invitations = useQuery({ queryKey: ["business", biz?.id, "invitations"], queryFn: () => api.get<Invitation[]>(`/api/businesses/${biz!.id}/invitations`), enabled: !!biz });
  const refresh = () => { qc.invalidateQueries({ queryKey: ["business", biz?.id, "members"] }); qc.invalidateQueries({ queryKey: ["business", biz?.id, "invitations"] }); };

  const myRank = roles.data?.find((r) => r.key === biz?.myRole)?.rank ?? 0;
  const assignable = (roles.data ?? []).filter((r) => r.key !== "business_owner" && r.rank < myRank);
  const rankOf = (key: string) => roles.data?.find((r) => r.key === key)?.rank ?? 0;

  const changeRole = useMutation({
    mutationFn: ({ id, roleKey }: { id: string; roleKey: string }) => api.patch(`/api/businesses/${biz!.id}/members/${id}`, { roleKey }),
    onSuccess: () => { refresh(); toast("ভূমিকা পরিবর্তন হয়েছে।"); },
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.del(`/api/businesses/${biz!.id}/members/${id}`),
    onSuccess: () => { refresh(); setRemoving(null); toast("সদস্যকে টিম থেকে সরানো হয়েছে।"); },
  });
  const revoke = useMutation({ mutationFn: (id: string) => api.del(`/api/businesses/${biz!.id}/invitations/${id}`), onSuccess: refresh });

  return (
    <>
      <PageHeader title="টিম ও ভূমিকা" subtitle="একই ব্যক্তি এক পেজে মালিক, অন্য পেজে স্টাফ হতে পারেন — সব একটি লগইনে"
        actions={canManage && <Button onClick={() => setInviteOpen(true)}><UserPlus className="size-4" aria-hidden />সদস্য আমন্ত্রণ জানান</Button>} />
      <ErrorBox error={members.error ?? changeRole.error} />
      {members.isLoading ? <LoadingList /> : (
        <ul className="card divide-y divide-zinc-100">
          {members.data?.map((m) => {
            const editable = canManage && m.userId !== user?.id && rankOf(m.roleKey) < myRank;
            return (
              <li key={m.id} className="flex flex-wrap items-center gap-4 px-5 py-4">
                <Avatar name={m.fullName} tone="violet" className="size-11 rounded-full" />
                <div className="min-w-0 flex-[1_1_200px]">
                  <div className="font-semibold">{m.fullName} {m.userId === user?.id && <span className="text-sm font-normal text-subtle">(আপনি)</span>}</div>
                  <div className="font-num text-sm text-subtle">{m.phone} · যোগদান {dateShort(m.joinedAt)}</div>
                </div>
                {m.status === "suspended" && <Badge tone="red">স্থগিত</Badge>}
                {editable ? (
                  <select className="input w-auto" value={m.roleKey} onChange={(e) => changeRole.mutate({ id: m.id, roleKey: e.target.value })} aria-label={`${m.fullName}-এর ভূমিকা`}>
                    {assignable.map((r) => <option key={r.key} value={r.key}>{r.nameBn}</option>)}
                  </select>
                ) : <Badge tone={m.roleKey === "business_owner" ? "blue" : "neutral"}>{m.roleName}</Badge>}
                {(editable || (m.userId === user?.id && m.roleKey !== "business_owner")) && (
                  <Button size="sm" variant="danger" onClick={() => setRemoving(m)}>{m.userId === user?.id ? "টিম ছাড়ুন" : "সরান"}</Button>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {!!invitations.data?.length && (
        <section className="mt-8">
          <h2 className="mb-3 text-base font-semibold">অপেক্ষমাণ আমন্ত্রণ</h2>
          <ul className="card divide-y divide-zinc-100">
            {invitations.data.map((i) => (
              <li key={i.id} className="flex flex-wrap items-center gap-3 px-5 py-3">
                <span className="flex-1 font-num">{i.inviteePhone}</span><Badge>{i.roleName}</Badge>
                <span className="text-sm text-subtle">মেয়াদ {dateShort(i.expiresAt)}</span>
                {canManage && <Button size="sm" variant="ghost" onClick={() => revoke.mutate(i.id)}>বাতিল</Button>}
              </li>
            ))}
          </ul>
        </section>
      )}

      {inviteOpen && biz && <InviteModal businessId={biz.id} roles={assignable} onClose={() => setInviteOpen(false)} onDone={refresh} />}
      <Modal open={!!removing} onClose={() => setRemoving(null)} title="আপনি কি নিশ্চিত?"
        footer={<><Button variant="ghost" onClick={() => setRemoving(null)}>বাতিল</Button><Button variant="danger" loading={remove.isPending} onClick={() => removing && remove.mutate(removing.id)}>নিশ্চিত করুন</Button></>}>
        <ErrorBox error={remove.error} />
        <p className="text-muted">{removing?.fullName} আর এই ব্যবসার ওয়ার্কস্পেসে প্রবেশ করতে পারবেন না। তাঁর আসন্ন অ্যাপয়েন্টমেন্টগুলো অনির্ধারিত হয়ে যাবে।</p>
      </Modal>
    </>
  );
}

function InviteModal({ businessId, roles, onClose, onDone }: { businessId: string; roles: Role[]; onClose: () => void; onDone: () => void }) {
  const [phone, setPhone] = useState("");
  const [roleKey, setRoleKey] = useState(roles.find((r) => r.key === "business_staff")?.key ?? roles[0]?.key ?? "");
  const [token, setToken] = useState<string | null>(null);
  const m = useMutation({
    mutationFn: () => api.post<{ token: string }>(`/api/businesses/${businessId}/invitations`, { phone, roleKey }),
    onSuccess: (r) => { setToken(r.token); onDone(); },
  });
  return (
    <Modal open onClose={onClose} title="সদস্য আমন্ত্রণ জানান"
      footer={token ? <Button onClick={onClose}>ঠিক আছে</Button> : <><Button variant="ghost" onClick={onClose}>বাতিল</Button><Button loading={m.isPending} disabled={!phone || !roleKey} onClick={() => m.mutate()}>আমন্ত্রণ পাঠান</Button></>}>
      {token ? (
        <div className="flex flex-col gap-3">
          <p>আমন্ত্রণ পাঠানো হয়েছে। {phone} নম্বরে SMS-এ কোড যাবে। তিনি অ্যাকাউন্ট খুলে <strong>প্রোফাইল › টিম আমন্ত্রণ</strong>-এ কোডটি দেবেন।</p>
          <div className="rounded-md bg-bg p-3"><div className="text-sm text-subtle">আমন্ত্রণ কোড</div><code className="font-num break-all">{token}</code></div>
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          <ErrorBox error={m.error} />
          <label className="label">মোবাইল নম্বর<input className="input" type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="০১XXXXXXXXX" /></label>
          <fieldset className="flex flex-col gap-2"><legend className="mb-1 font-medium">ভূমিকা</legend>
            {roles.map((r) => (
              <label key={r.key} className={`flex cursor-pointer items-start gap-3 rounded-md border p-3 ${roleKey === r.key ? "border-zinc-900 bg-zinc-50" : "border-line"}`}>
                <input type="radio" name="role" className="mt-1 size-4 accent-primary" checked={roleKey === r.key} onChange={() => setRoleKey(r.key)} />
                <span><span className="block font-semibold">{r.nameBn}</span><span className="block text-sm text-muted">{ROLE_HELP[r.key]}</span></span>
              </label>
            ))}
          </fieldset>
        </div>
      )}
    </Modal>
  );
}
