import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BellOff } from "lucide-react";
import { useParams } from "react-router";
import { api } from "../../api/client";
import type { NotificationItem, Paged, WorkspaceType } from "../../api/types";
import { Button, EmptyState, ErrorBox, LoadingList, PAGE_SIZE, PageHeader, Pagination, usePage } from "../../components/ui";
import { relative } from "../../lib/format";

/** Notification list for one workspace (customer by default, or the business in the URL). */
export default function Notifications({ workspace = "customer" }: { workspace?: WorkspaceType }) {
  const { businessId } = useParams();
  const qc = useQueryClient();
  const filter = { workspace, businessId: workspace === "business" ? businessId : undefined };
  const [page, setPage] = usePage(filter);
  const list = useQuery({
    queryKey: ["notifications", filter, page],
    queryFn: () => api.get<Paged<NotificationItem>>("/api/me/notifications", { ...filter, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["notifications"] });
    qc.invalidateQueries({ queryKey: ["workspaces"] });
  };
  const readOne = useMutation({ mutationFn: (id: string) => api.post(`/api/me/notifications/${id}/read`), onSuccess: refresh });
  const readAll = useMutation({
    mutationFn: () => api.post(`/api/me/notifications/read-all?workspace=${workspace}${filter.businessId ? `&businessId=${filter.businessId}` : ""}`),
    onSuccess: refresh,
  });
  const unread = list.data?.items.some((n) => !n.readAt);

  return (
    <>
      <PageHeader title="নোটিফিকেশন" actions={unread && <Button variant="outline" loading={readAll.isPending} onClick={() => readAll.mutate()}>সব পঠিত চিহ্নিত করুন</Button>} />
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <ul className="card divide-y divide-zinc-100">
          {list.data.items.map((n) => (
            <li key={n.id}>
              <button onClick={() => !n.readAt && readOne.mutate(n.id)} className={`flex w-full items-start gap-3 px-5 py-4 text-left ${n.readAt ? "" : "bg-zinc-50 hover:bg-zinc-100"}`}>
                <span className={`mt-2 size-2.5 shrink-0 rounded-full ${n.readAt ? "bg-transparent" : "bg-primary"}`} aria-label={n.readAt ? undefined : "অপঠিত"} />
                <span className="min-w-0 flex-1">
                  <span className={`block ${n.readAt ? "" : "font-semibold"}`}>{n.title}</span>
                  {n.body && <span className="block text-[15px] text-muted">{n.body}</span>}
                </span>
                <span className="shrink-0 text-sm text-subtle">{relative(n.createdAt)}</span>
              </button>
            </li>
          ))}
        </ul>
      ) : <EmptyState icon={<BellOff className="size-8" aria-hidden />} title="কোনো নোটিফিকেশন নেই" />}
      {list.data && <Pagination page={page} total={list.data.total} onChange={setPage} label="নোটিফিকেশনের পাতা" />}
    </>
  );
}
