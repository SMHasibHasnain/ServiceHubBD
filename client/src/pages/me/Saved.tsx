import { useQuery } from "@tanstack/react-query";
import { Heart } from "lucide-react";
import { api } from "../../api/client";
import type { BusinessCard } from "../../api/types";
import { BusinessCardView } from "../../components/shared";
import { EmptyState, ErrorBox, LinkButton, LoadingList, PageHeader } from "../../components/ui";
import { bn } from "../../lib/format";

/** Businesses I saved ("সংরক্ষণ করুন" on a profile). Saving also brings their new notices to my notifications. */
export default function Saved() {
  const list = useQuery({ queryKey: ["favourites", "businesses"], queryFn: () => api.get<BusinessCard[]>("/api/me/favourites/businesses") });
  return (
    <>
      <PageHeader title="সংরক্ষিত ব্যবসা"
        subtitle={list.data?.length ? `${bn(list.data.length)}টি ব্যবসা। এদের নতুন নোটিশ (অফার, ছুটি) আপনার নোটিফিকেশনে আসবে।` : "পছন্দের ব্যবসা সংরক্ষণ করলে এখানে পাবেন"} />
      <ErrorBox error={list.error} />
      {list.isLoading ? <LoadingList /> : list.data?.length ? (
        <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {list.data.map((b) => <li key={b.id}><BusinessCardView b={b} /></li>)}
        </ul>
      ) : (
        <EmptyState icon={<Heart aria-hidden />} title="এখনও কিছু সংরক্ষণ করেননি"
          description="কোনো ব্যবসার পেজে “সংরক্ষণ করুন” চাপুন। পরে সহজে খুঁজে পাবেন, আর তাদের নতুন অফারের খবর পাবেন।"
          action={<LinkButton to="/search">সেবা খুঁজুন</LinkButton>} />
      )}
    </>
  );
}
