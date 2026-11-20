import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { MapPin } from "lucide-react";
import { api } from "../../api/client";
import type { AdminAnalytics, InspectionAnalytics, ModerationAnalytics } from "../../api/types";
import {
  AnalyticsToolbar, BarList, ChartCard, ColumnChart, Funnel, KpiTile, LineChart, STATUS_COLOR, SegmentedBar, VIZ,
  fmt, sliceTable, trendTable, useRange,
} from "../../components/charts";
import { ErrorBox, LoadingList, PageHeader } from "../../components/ui";
import { bn } from "../../lib/format";

function useAnalytics<T>(key: string, url: string, days: number) {
  return useQuery({ queryKey: ["analytics", key, days], queryFn: () => api.get<T>(url, { days }), placeholderData: keepPreviousData });
}

const Frame = ({ fetching, children }: { fetching: boolean; children: React.ReactNode }) =>
  <div className={`flex flex-col gap-5 transition-opacity ${fetching ? "opacity-60" : ""}`}>{children}</div>;

// ---------- Moderation ----------

const MOD_TREND = [
  { key: "submitted", label: "জমা পড়েছে", color: VIZ.series[0] },
  { key: "decided", label: "সিদ্ধান্ত হয়েছে", color: STATUS_COLOR.completed },
];

export function ModerationAnalyticsPage() {
  const [days, setDays] = useRange(30);
  const q = useAnalytics<ModerationAnalytics>("moderation", "/api/moderation/analytics", days);
  const a = q.data, k = a?.kpis ?? {};
  return (
    <>
      <PageHeader title="মডারেশন পরিসংখ্যান" subtitle="যাচাইকরণের গতি, সারির অবস্থা ও রিপোর্ট নিষ্পত্তি — আপনার এলাকা ও ক্যাটাগরির মধ্যে" />
      <AnalyticsToolbar days={days} onChange={setDays} from={a?.from} to={a?.to} />
      <ErrorBox error={q.error} />
      {!a ? <LoadingList rows={3} /> : (
        <Frame fetching={q.isFetching}>
          <section aria-label="মূল সূচক" className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
            <KpiTile label="এখন অপেক্ষমাণ" metric={k.pending_now} note={<span>বর্তমান সারি</span>} />
            <KpiTile label="নতুন আবেদন" metric={k.submitted} />
            <KpiTile label="সিদ্ধান্ত দেওয়া হয়েছে" metric={k.decided} />
            <KpiTile label="অনুমোদনের হার" metric={k.approval_rate} format={fmt.pct} deltaMode="points" upIsGood={null} />
            <KpiTile label="সিদ্ধান্তে গড় সময়" metric={k.decision_hours} format={fmt.hours} upIsGood={false} />
            <KpiTile label="প্রথম পর্যালোচনায় গড় সময়" metric={k.first_review_hours} format={fmt.hours} upIsGood={false} />
            <KpiTile label="খোলা রিপোর্ট" metric={k.reports_open_now} note={<span>বর্তমানে</span>} />
            <KpiTile label="নতুন রিপোর্ট" metric={k.reports_opened} upIsGood={false} />
            <KpiTile label="নিষ্পত্তি হওয়া রিপোর্ট" metric={k.reports_resolved} />
            <KpiTile label="রিপোর্ট নিষ্পত্তিতে গড় সময়" metric={k.report_resolution_hours} format={fmt.hours} upIsGood={false} />
          </section>

          <ChartCard title="জমা বনাম সিদ্ধান্ত" subtitle="প্রতিদিন কত আবেদন এসেছে ও কতগুলোর সিদ্ধান্ত হয়েছে — সিদ্ধান্ত পিছিয়ে থাকলে সারি বাড়ে" table={trendTable(a.trend, MOD_TREND)}>
            <LineChart data={a.trend} series={MOD_TREND} />
          </ChartCard>

          <div className="grid gap-5 lg:grid-cols-2">
            <ChartCard title="বর্তমান সারি" subtitle="অপেক্ষমাণ আবেদনের ধাপ" table={sliceTable(a.queue, "আবেদন")}>
              <SegmentedBar parts={a.queue} colors={VIZ.series} />
            </ChartCard>
            <ChartCard title="কতদিন ধরে অপেক্ষায়" subtitle="জমা দেওয়ার পর থেকে সময় — ডানের দিকে যত বেশি, তত জরুরি" table={sliceTable(a.aging, "আবেদন")}>
              <BarList items={a.aging} colors={VIZ.ordinal4} max={Math.max(1, ...a.aging.map((x) => x.value))} />
            </ChartCard>
          </div>

          <div className="grid gap-5 lg:grid-cols-3">
            <ChartCard title="সিদ্ধান্তের ধরন" subtitle="এই সময়ে নেওয়া পদক্ষেপ" table={sliceTable(a.outcomes, "সংখ্যা")}>
              <BarList items={a.outcomes} showShare />
            </ChartCard>
            <ChartCard title="রিপোর্টের কারণ" table={sliceTable(a.reportReasons, "রিপোর্ট")}>
              <BarList items={a.reportReasons} showShare />
            </ChartCard>
            <ChartCard title="ক্যাটাগরি অনুযায়ী অপেক্ষমাণ" table={sliceTable(a.pendingByCategory, "আবেদন")}>
              <BarList items={a.pendingByCategory} />
            </ChartCard>
          </div>

          <ChartCard title="মডারেটরদের কাজ" subtitle="মোট পদক্ষেপ · অনুমোদন/প্রত্যাখ্যান সিদ্ধান্ত" table={sliceTable(a.moderators, "পদক্ষেপ", fmt.count, { label: "সিদ্ধান্ত", format: fmt.count })}>
            <BarList items={a.moderators} secondary={{ label: "সিদ্ধান্ত", format: fmt.count }} />
          </ChartCard>
        </Frame>
      )}
    </>
  );
}

// ---------- Inspection ----------

const INS_TREND = [
  { key: "requested", label: "নতুন অনুরোধ", color: VIZ.series[0] },
  { key: "completed", label: "সম্পন্ন পরিদর্শন", color: STATUS_COLOR.completed },
];

export function InspectionAnalyticsPage() {
  const [days, setDays] = useRange(30);
  const q = useAnalytics<InspectionAnalytics>("inspections", "/api/inspections/analytics", days);
  const a = q.data, k = a?.kpis ?? {};
  return (
    <>
      <PageHeader title="পরিদর্শন পরিসংখ্যান" subtitle="আপনার এলাকার মাঠ পর্যায়ের কাজ — চাপ, গতি ও ফলাফল" />
      <AnalyticsToolbar days={days} onChange={setDays} from={a?.from} to={a?.to} />
      <ErrorBox error={q.error} />
      {!a ? <LoadingList rows={3} /> : (
        <Frame fetching={q.isFetching}>
          <section aria-label="মূল সূচক" className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <KpiTile label="খোলা পরিদর্শন" metric={k.open_now} note={<span>বর্তমানে</span>} />
            <KpiTile label="দায়িত্বহীন" metric={k.unassigned_now} note={<span>কেউ নেননি</span>} />
            <KpiTile label="সময় পেরিয়ে গেছে" metric={k.overdue_now} note={<span>নির্ধারিত সময় অতিক্রান্ত</span>} />
            <KpiTile label="নতুন অনুরোধ" metric={k.requested} />
            <KpiTile label="সম্পন্ন (এলাকা)" metric={k.completed} />
            <KpiTile label="আমি সম্পন্ন করেছি" metric={k.mine_completed} />
            <KpiTile label="উত্তীর্ণের হার" metric={k.pass_rate} format={fmt.pct} deltaMode="points" upIsGood={null} />
            <KpiTile label="অনুরোধ থেকে পরিদর্শন" metric={k.turnaround_hours} format={fmt.hours} upIsGood={false} />
          </section>
          <ChartCard title="অনুরোধ বনাম সম্পন্ন" table={trendTable(a.trend, INS_TREND)}>
            <LineChart data={a.trend} series={INS_TREND} />
          </ChartCard>
          <div className="grid gap-5 lg:grid-cols-3">
            <ChartCard title="ফলাফল" table={sliceTable(a.outcomes, "পরিদর্শন")}><BarList items={a.outcomes} showShare /></ChartCard>
            <ChartCard title="এলাকা অনুযায়ী" subtitle="অনুরোধ · সম্পন্ন" table={sliceTable(a.byZone, "অনুরোধ", fmt.count, { label: "সম্পন্ন", format: fmt.count })}>
              <BarList items={a.byZone} secondary={{ label: "সম্পন্ন", format: fmt.count }} />
            </ChartCard>
            <ChartCard title="পরিদর্শনের ধরন" table={sliceTable(a.byType, "অনুরোধ")}><BarList items={a.byType} showShare /></ChartCard>
          </div>
        </Frame>
      )}
    </>
  );
}

// ---------- Administration ----------

const GROWTH = [
  { key: "signups", label: "নতুন ব্যবহারকারী", color: VIZ.series[0] },
  { key: "new_businesses", label: "নতুন ব্যবসা পেজ", color: VIZ.series[1] },
];
const BOOKINGS = [
  { key: "bookings", label: "মোট বুকিং", color: VIZ.series[0] },
  { key: "completed", label: "সম্পন্ন", color: STATUS_COLOR.completed },
];

export function AdminAnalyticsPage() {
  const [days, setDays] = useRange(30);
  const q = useAnalytics<AdminAnalytics>("admin", "/api/admin/analytics", days);
  const a = q.data, k = a?.kpis ?? {};
  return (
    <>
      <PageHeader title="প্ল্যাটফর্ম বিশ্লেষণ" subtitle="ServiceHub BD-এর প্রবৃদ্ধি, বাজারের কার্যক্রম ও আস্থার অবস্থা" />
      <AnalyticsToolbar days={days} onChange={setDays} from={a?.from} to={a?.to} />
      <ErrorBox error={q.error} />
      {!a ? <LoadingList rows={3} /> : (
        <Frame fetching={q.isFetching}>
          <div className="bg-brand relative overflow-hidden rounded-xl p-5 text-white tabular-nums sm:p-6">
            <div className="relative flex flex-wrap items-end justify-between gap-6">
              <div>
                <p className="text-sm text-[#cfd8ea]">গত {bn(a.days)} দিনে মোট লেনদেন (সম্পন্ন বুকিং)</p>
                <p className="mt-1 text-5xl leading-none font-semibold">{fmt.taka(k.gmv?.value ?? 0)}</p>
              </div>
              <dl className="flex flex-wrap gap-x-8 gap-y-2 text-sm">
                <div><dt className="text-[#cfd8ea]">মোট ব্যবহারকারী</dt><dd className="text-xl font-semibold">{fmt.count(k.total_users?.value ?? 0)}</dd></div>
                <div><dt className="text-[#cfd8ea]">সক্রিয় ব্যবসা</dt><dd className="text-xl font-semibold">{fmt.count(k.live_businesses?.value ?? 0)}</dd></div>
                <div><dt className="text-[#cfd8ea]">অপেক্ষমাণ যাচাই</dt><dd className="text-xl font-semibold">{fmt.count(k.pending_verifications?.value ?? 0)}</dd></div>
                <div><dt className="text-[#cfd8ea]">খোলা রিপোর্ট</dt><dd className="text-xl font-semibold">{fmt.count(k.open_reports?.value ?? 0)}</dd></div>
              </dl>
            </div>
          </div>

          <section aria-label="মূল সূচক" className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
            <KpiTile label="নতুন ব্যবহারকারী" metric={k.new_users} />
            <KpiTile label="সক্রিয় ব্যবহারকারী" metric={k.active_users} note={<span>এই সময়ে লগইন</span>} />
            <KpiTile label="নতুন ব্যবসা পেজ" metric={k.new_businesses} />
            <KpiTile label="প্রকাশিত হয়েছে" metric={k.published_businesses} />
            <KpiTile label="মোট বুকিং" metric={k.bookings} />
            <KpiTile label="সম্পন্ন বুকিং" metric={k.completed} />
            <KpiTile label="লেনদেন" metric={k.gmv} format={fmt.taka} />
            <KpiTile label="সম্পন্নের হার" metric={k.completion_rate} format={fmt.pct} deltaMode="points" />
            <KpiTile label="নতুন রিভিউ" metric={k.reviews} />
            <KpiTile label="গড় রেটিং" metric={k.avg_rating} format={fmt.rating} deltaMode="points" />
          </section>

          <div className="grid gap-5 xl:grid-cols-2">
            <ChartCard title="প্রবৃদ্ধি" subtitle="প্রতিদিনের নতুন অ্যাকাউন্ট ও ব্যবসা পেজ" table={trendTable(a.growth, GROWTH)}>
              <LineChart data={a.growth} series={GROWTH} />
            </ChartCard>
            <ChartCard title="বুকিং কার্যক্রম" subtitle="প্রতিদিন আসা বুকিং ও তার মধ্যে সম্পন্ন" table={trendTable(a.bookings, BOOKINGS)}>
              <LineChart data={a.bookings} series={BOOKINGS} />
            </ChartCard>
          </div>

          <ChartCard title="দৈনিক লেনদেন" subtitle="সম্পন্ন বুকিংয়ের মূল্য (৳)" table={trendTable(a.bookings, [{ key: "gmv", label: "লেনদেন", color: VIZ.series[0] }], [fmt.taka])}>
            <ColumnChart data={a.bookings} valueKey="gmv" label="লেনদেন" format={fmt.taka} />
          </ChartCard>

          <div className="grid gap-5 lg:grid-cols-2">
            <ChartCard title="ব্যবসা অনবোর্ডিং ফানেল" subtitle="শুরু থেকে এখন পর্যন্ত — প্রতিটি ধাপ আগের ধাপের অংশ" table={sliceTable(a.funnel, "ব্যবসা")}>
              <Funnel steps={a.funnel} />
            </ChartCard>
            <ChartCard title="বুকিংয়ের অবস্থা" subtitle="এই সময়ে আসা সব বুকিং" table={sliceTable(a.statusMix, "বুকিং")}>
              <SegmentedBar parts={a.statusMix} colors={STATUS_COLOR} />
            </ChartCard>
          </div>

          <div className="grid gap-5 lg:grid-cols-3">
            <ChartCard title="ক্যাটাগরি অনুযায়ী সক্রিয় ব্যবসা" subtitle="ব্যবসা · এই সময়ের বুকিং" table={sliceTable(a.byCategory, "ব্যবসা", fmt.count, { label: "বুকিং", format: fmt.count })}>
              <BarList items={a.byCategory} secondary={{ label: "বুকিং", format: fmt.count }} />
            </ChartCard>
            <ChartCard title="বিভাগ অনুযায়ী ব্যবসা" subtitle="সব পেজ · প্রকাশিত" table={sliceTable(a.byDivision, "ব্যবসা", fmt.count, { label: "প্রকাশিত", format: fmt.count })}>
              <BarList items={a.byDivision} secondary={{ label: "প্রকাশিত", format: fmt.count }} />
            </ChartCard>
            <ChartCard title="শীর্ষ ব্যবসা" subtitle="বুকিং · লেনদেন" table={sliceTable(a.topBusinesses, "বুকিং", fmt.count, { label: "লেনদেন", format: fmt.taka })}>
              <BarList items={a.topBusinesses} secondary={{ label: "লেনদেন", format: fmt.taka }} />
            </ChartCard>
          </div>

          <ChartCard title="প্ল্যাটফর্ম ভূমিকা" subtitle="সক্রিয় ভূমিকা বরাদ্দ" table={sliceTable(a.roles, "জন")}>
            <BarList items={a.roles} />
          </ChartCard>
          <p className="flex items-center gap-1.5 text-xs text-subtle"><MapPin className="size-3.5" aria-hidden />সব সময় বাংলাদেশ সময় (UTC+৬) অনুযায়ী হিসাব করা হয়েছে।</p>
        </Frame>
      )}
    </>
  );
}
