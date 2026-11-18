import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useOutletContext, useParams } from "react-router";
import { BadgeDollarSign, CalendarCheck, CalendarDays, Clock, Repeat, Star, UserPlus, XCircle } from "lucide-react";
import { api } from "../../api/client";
import type { BusinessAnalytics, BusinessDetail } from "../../api/types";
import {
  AnalyticsToolbar, BarList, ChartCard, ColumnChart, Heatmap, KpiTile, LineChart, STATUS_COLOR, SegmentedBar, VIZ,
  fmt, heatmapTable, sliceTable, trendTable, useRange,
} from "../../components/charts";
import { EmptyState, ErrorBox, LoadingList, PageHeader } from "../../components/ui";
import { bn } from "../../lib/format";

const TREND = [
  { key: "bookings", label: "মোট বুকিং", color: VIZ.series[0] },
  { key: "completed", label: "সম্পন্ন", color: STATUS_COLOR.completed },
];

export default function BusinessAnalyticsPage() {
  const { businessId } = useParams();
  const biz = useOutletContext<BusinessDetail | undefined>();
  const [days, setDays] = useRange(30);
  const q = useQuery({
    queryKey: ["business", businessId, "analytics", days],
    queryFn: () => api.get<BusinessAnalytics>(`/api/businesses/${businessId}/analytics`, { days }),
    placeholderData: keepPreviousData,
  });
  const allowed = biz?.myPermissions.includes("analytics.view");
  const a = q.data;
  const k = a?.kpis ?? {};

  if (biz && !allowed) {
    return <EmptyState title="বিশ্লেষণ দেখার অনুমতি নেই" description="মালিক, ম্যানেজার বা বিশ্লেষক ভূমিকার সদস্যরা এই পাতা দেখতে পারেন।" />;
  }

  return (
    <>
      <PageHeader title="ব্যবসার বিশ্লেষণ" subtitle="বুকিং, আয়, গ্রাহক ও রিভিউ — নির্বাচিত সময়ের সাথে আগের সময়ের তুলনা" />
      <AnalyticsToolbar days={days} onChange={setDays} from={a?.from} to={a?.to} />
      <ErrorBox error={q.error} />
      {!a ? <LoadingList rows={3} /> : (
        <div className={`flex flex-col gap-5 transition-opacity ${q.isFetching ? "opacity-60" : ""}`}>
          <section aria-label="মূল সূচক" className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
            <KpiTile label="মোট বুকিং" metric={k.bookings} icon={<CalendarDays />} />
            <KpiTile label="সম্পন্ন বুকিং" metric={k.completed} icon={<CalendarCheck />} />
            <KpiTile label="আয়" metric={k.revenue} format={fmt.taka} icon={<BadgeDollarSign />} note={<span>সম্পন্ন বুকিং থেকে</span>} />
            <KpiTile label="গড় বিল" metric={k.avg_ticket} format={fmt.taka} />
            <KpiTile label="সম্পন্নের হার" metric={k.completion_rate} format={fmt.pct} deltaMode="points" />
            <KpiTile label="বাতিলের হার" metric={k.cancellation_rate} format={fmt.pct} deltaMode="points" upIsGood={false} icon={<XCircle />} />
            <KpiTile label="নতুন গ্রাহক" metric={k.new_customers} icon={<UserPlus />} />
            <KpiTile label="ফিরে আসা গ্রাহক" metric={k.returning_rate} format={fmt.pct} deltaMode="points" icon={<Repeat />} />
            <KpiTile label="গড় রেটিং" metric={k.avg_rating} format={fmt.rating} deltaMode="points" icon={<Star />} note={<span>{bn(k.reviews?.value ?? 0)}টি নতুন রিভিউ</span>} />
            <KpiTile label="নিশ্চিত করতে গড় সময়" metric={k.response_minutes} format={fmt.minutes} upIsGood={false} icon={<Clock />} />
          </section>

          <div className="grid gap-5 xl:grid-cols-[1.6fr_1fr]">
            <ChartCard title="বুকিংয়ের প্রবণতা" subtitle="প্রতিদিন কতগুলো বুকিং এসেছে ও তার কতগুলো সম্পন্ন হয়েছে" table={trendTable(a.trend, TREND)}>
              <LineChart data={a.trend} series={TREND} />
            </ChartCard>
            <ChartCard title="বুকিংয়ের অবস্থা" subtitle="এই সময়ে আসা বুকিংগুলো এখন কোন অবস্থায়" table={sliceTable(a.statusMix, "বুকিং")}>
              <SegmentedBar parts={a.statusMix} colors={STATUS_COLOR} />
              <dl className="mt-5 grid grid-cols-2 gap-3 border-t border-line pt-4 text-sm">
                <div><dt className="text-subtle">আসেননি</dt><dd className="text-lg font-semibold">{fmt.pct(k.no_show_rate?.value ?? 0)}</dd></div>
                <div><dt className="text-subtle">বাতিল/প্রত্যাখ্যাত</dt><dd className="text-lg font-semibold">{fmt.pct(k.cancellation_rate?.value ?? 0)}</dd></div>
              </dl>
            </ChartCard>
          </div>

          <ChartCard title="দৈনিক আয়" subtitle="সম্পন্ন বুকিংয়ের চূড়ান্ত মূল্য (৳)" table={trendTable(a.trend, [{ key: "revenue", label: "আয়", color: VIZ.series[0] }], [fmt.taka])}>
            <ColumnChart data={a.trend} valueKey="revenue" label="আয়" format={fmt.taka} />
          </ChartCard>

          <div className="grid gap-5 lg:grid-cols-2">
            <ChartCard title="জনপ্রিয় সেবা" subtitle="বুকিং সংখ্যা · সম্পন্ন বুকিং থেকে আয়" table={sliceTable(a.topServices, "বুকিং", fmt.count, { label: "আয়", format: fmt.taka })}>
              <BarList items={a.topServices} secondary={{ label: "আয়", format: fmt.taka }} />
            </ChartCard>
            <ChartCard title="কখন চাহিদা বেশি" subtitle="সপ্তাহের দিন ও সময় অনুযায়ী অ্যাপয়েন্টমেন্ট" table={heatmapTable(a.heatmap)}>
              <Heatmap grid={a.heatmap} />
            </ChartCard>
          </div>

          <div className="grid gap-5 lg:grid-cols-3">
            <ChartCard title="টিমের কাজ" subtitle="নির্ধারিত বুকিং · সম্পন্ন" table={sliceTable(a.staff, "বুকিং", fmt.count, { label: "সম্পন্ন", format: fmt.count })}>
              <BarList items={a.staff} secondary={{ label: "সম্পন্ন", format: fmt.count }} />
            </ChartCard>
            <ChartCard title="রেটিং বণ্টন" subtitle="সব প্রকাশিত রিভিউ" table={sliceTable(a.ratings, "রিভিউ")}>
              <BarList items={a.ratings} showShare max={Math.max(1, ...a.ratings.map((r) => r.value))} />
            </ChartCard>
            <ChartCard title="বুকিং কোথা থেকে আসছে" table={sliceTable(a.sources, "বুকিং")}>
              <BarList items={a.sources} showShare />
            </ChartCard>
          </div>
        </div>
      )}
    </>
  );
}
