import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Heart } from "lucide-react";
import { api } from "../../api/client";
import type { BusinessCard, Paged } from "../../api/types";
import { BusinessCardView } from "../../components/shared";
import { EmptyState, ErrorBox, LinkButton, LoadingList, PAGE_SIZE, PageHeader, Pagination, usePage } from "../../components/ui";
import { bn } from "../../lib/format";

/** Businesses I saved ("সংরক্ষণ করুন" on a profile). Saving also brings their new notices to my notifications. */
export default function Saved() {
  const [page, setPage] = usePage();
  const list = useQuery({
    queryKey: ["favourites", "businesses", page],
    queryFn: () => api.get<Paged<BusinessCard>>("/api/me/favourites/businesses", { page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const total = list.data?.total ?? 0;
  return (
    <>
      <PageHeader title="সংরক্ষিত ব্যবসা"
        subtitle={total ? `${bn(total)}টি ব্যবসা। এদের নতুন নোটিশ (অফার, ছুটি) আপনার নোটিফিকেশনে আসবে।` : "পছন্দের ব্যবসা সংরক্ষণ করলে এখানে পাবেন"} />
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.items.length ? (
        <>
          <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {list.data.items.map((b) => <li key={b.id} className="flex"><BusinessCardView b={b} /></li>)}
          </ul>
          <Pagination page={page} total={total} onChange={setPage} label="সংরক্ষিত ব্যবসার পাতা" />
        </>
      ) : (
        <EmptyState icon={<Heart aria-hidden />} title="এখনও কিছু সংরক্ষণ করেননি"
          description="কোনো ব্যবসার পেজে “সংরক্ষণ করুন” চাপুন। পরে সহজে খুঁজে পাবেন, আর তাদের নতুন অফারের খবর পাবেন।"
          action={<LinkButton to="/search">সেবা খুঁজুন</LinkButton>} />
      )}
    </>
  );
}
