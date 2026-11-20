import { useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Flag, Star } from "lucide-react";
import { api } from "../../api/client";
import type { ManagedReview, Paged } from "../../api/types";
import { Avatar, Button, EmptyState, ErrorBox, LoadingList, Modal, PAGE_SIZE, PageHeader, Pagination, Segmented, Stars, usePage, useToast } from "../../components/ui";
import { REPORT_REASONS, bn, dateShort } from "../../lib/format";
import { useBiz } from "./Dashboard";
import { ReviewPhotos } from "../../components/shared";

export default function Reviews() {
  const biz = useBiz();
  const qc = useQueryClient();
  const toast = useToast();
  const [filter, setFilter] = useState<"all" | "unreplied">("all");
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [reporting, setReporting] = useState<ManagedReview | null>(null);
  const canReply = !!biz?.myPermissions.includes("reviews.reply");

  const [page, setPage] = usePage(filter);
  const list = useQuery({
    queryKey: ["business", biz?.id, "reviews", filter, page],
    queryFn: () => api.get<Paged<ManagedReview>>(`/api/businesses/${biz!.id}/reviews`, { unrepliedOnly: filter === "unreplied", page, pageSize: PAGE_SIZE }),
    enabled: !!biz,
    placeholderData: keepPreviousData,
  });
  const reply = useMutation({
    mutationFn: ({ id, body }: { id: string; body: string }) => api.put(`/api/businesses/${biz!.id}/reviews/${id}/reply`, { body }),
    onSuccess: (_, { id }) => { setDrafts((d) => ({ ...d, [id]: "" })); qc.invalidateQueries({ queryKey: ["business", biz?.id, "reviews"] }); toast("উত্তর প্রকাশিত হয়েছে।"); },
  });

  return (
    <>
      <PageHeader title="রিভিউ" subtitle={biz && biz.reviewCount > 0 ? <>গড় <Stars value={biz.avgRating} /> · {bn(biz.reviewCount)} রিভিউ</> : "সম্পন্ন বুকিংয়ের গ্রাহকরাই রিভিউ দিতে পারেন"} />
      <div className="mb-5"><Segmented label="রিভিউ ফিল্টার" value={filter} onChange={setFilter} options={[{ value: "all", label: "সব" }, { value: "unreplied", label: "উত্তর দেওয়া হয়নি" }]} /></div>
      <ErrorBox error={list.error ?? reply.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <ul className="flex flex-col gap-4">
          {list.data.items.map((r) => (
            <li key={r.id} className="card flex flex-col gap-3 p-5">
              <div className="flex items-center gap-3">
                <Avatar name={r.customerName} tone="violet" className="size-10 rounded-full" />
                <div className="flex-1"><div className="font-semibold">{r.customerName}</div><div className="text-sm text-subtle">{r.serviceName} · {dateShort(r.createdAt)}</div></div>
                <Stars value={r.rating} />
              </div>
              {r.comment && <p className="text-zinc-700">{r.comment}</p>}
              {r.photos.length > 0 && <ReviewPhotos photos={r.photos} />}
              {r.reply ? (
                <div className="rounded-md bg-bg px-4 py-3 text-[15px]"><span className="font-semibold">আপনার উত্তর: </span>{r.reply}</div>
              ) : canReply && (
                <div className="flex flex-wrap gap-2">
                  <input className="input min-w-56 flex-1" value={drafts[r.id] ?? ""} onChange={(e) => setDrafts({ ...drafts, [r.id]: e.target.value })} placeholder="ধন্যবাদ জানিয়ে উত্তর লিখুন" aria-label="উত্তর" />
                  <Button disabled={(drafts[r.id] ?? "").trim().length < 2} loading={reply.isPending && reply.variables?.id === r.id} onClick={() => reply.mutate({ id: r.id, body: drafts[r.id] })}>উত্তর দিন</Button>
                </div>
              )}
              {biz?.myPermissions.includes("reviews.report") && (
                <button onClick={() => setReporting(r)} className="flex min-h-9 items-center gap-1.5 self-start text-sm text-red-700 hover:underline"><Flag className="size-4" aria-hidden />মডারেটরকে রিপোর্ট করুন</button>
              )}
            </li>
          ))}
        </ul>
      ) : <EmptyState icon={<Star className="size-8" aria-hidden />} title="এখনও কোনো রিভিউ নেই" />}
      {list.data && <Pagination page={page} total={list.data.total} onChange={setPage} label="রিভিউর পাতা" />}
      {reporting && biz && <ReportReview businessId={biz.id} review={reporting} onClose={() => setReporting(null)} />}
    </>
  );
}

function ReportReview({ businessId, review, onClose }: { businessId: string; review: ManagedReview; onClose: () => void }) {
  const [reason, setReason] = useState("spam");
  const [description, setDescription] = useState("");
  const toast = useToast();
  const m = useMutation({
    mutationFn: () => api.post(`/api/businesses/${businessId}/reviews/${review.id}/report`, { targetType: "review", targetReviewId: review.id, reason, description }),
    onSuccess: () => { toast("রিপোর্ট জমা হয়েছে।"); onClose(); },
  });
  return (
    <Modal open onClose={onClose} title="রিভিউ রিপোর্ট করুন"
      footer={<><Button variant="ghost" onClick={onClose}>বাতিল</Button><Button variant="danger" loading={m.isPending} onClick={() => m.mutate()}>রিপোর্ট করুন</Button></>}>
      <div className="flex flex-col gap-4">
        <ErrorBox error={m.error} />
        <label className="label">কারণ<select className="input" value={reason} onChange={(e) => setReason(e.target.value)}>{Object.entries(REPORT_REASONS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
        <label className="label">বিস্তারিত<textarea className="input" rows={3} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
      </div>
    </Modal>
  );
}
