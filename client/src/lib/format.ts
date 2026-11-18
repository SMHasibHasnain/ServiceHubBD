import type { BookingStatus, PriceType, VerificationStatus } from "../api/types";

const TZ = "Asia/Dhaka";
const BN_DIGITS = "০১২৩৪৫৬৭৮৯";

export const bn = (value: string | number) => String(value).replace(/[0-9]/g, (d) => BN_DIGITS[Number(d)]);

export const num = (n: number, fraction = 0) =>
  new Intl.NumberFormat("bn-BD", { maximumFractionDigits: fraction, minimumFractionDigits: fraction }).format(n);

export const taka = (n: number | null | undefined) => (n == null ? "—" : `৳${num(n)}`);

export function priceLabel(type: PriceType, price: number | null, max?: number | null) {
  switch (type) {
    case "contact_for_price":
      return "যোগাযোগ করুন";
    case "starting_from":
      return `শুরু ${taka(price)}`;
    case "range":
      return `${taka(price)} – ${taka(max ?? null)}`;
    default:
      return taka(price);
  }
}

export const duration = (minutes: number) =>
  minutes % 60 === 0 ? `${bn(minutes / 60)} ঘণ্টা` : minutes > 60 ? `${bn(Math.floor(minutes / 60))} ঘণ্টা ${bn(minutes % 60)} মিনিট` : `${bn(minutes)} মিনিট`;

export const dateTime = (iso: string) =>
  new Intl.DateTimeFormat("bn-BD", { timeZone: TZ, day: "numeric", month: "long", hour: "numeric", minute: "2-digit" }).format(new Date(iso));

export const dateLong = (iso: string) =>
  new Intl.DateTimeFormat("bn-BD", { timeZone: TZ, weekday: "long", day: "numeric", month: "long" }).format(new Date(iso));

export const dateShort = (iso: string) =>
  new Intl.DateTimeFormat("bn-BD", { timeZone: TZ, day: "numeric", month: "short", year: "numeric" }).format(new Date(iso));

export const timeOnly = (iso: string) =>
  new Intl.DateTimeFormat("bn-BD", { timeZone: TZ, hour: "numeric", minute: "2-digit" }).format(new Date(iso));

/** "14:30:00" → "২:৩০ PM"-style local label. */
export function clock(time: string) {
  const [h, m] = time.split(":").map(Number);
  const d = new Date(Date.UTC(2000, 0, 1, h - 6, m)); // render as Dhaka (UTC+6) wall time
  return new Intl.DateTimeFormat("bn-BD", { timeZone: TZ, hour: "numeric", minute: "2-digit" }).format(d);
}

export function relative(iso: string) {
  const diff = (Date.now() - new Date(iso).getTime()) / 1000;
  const rtf = new Intl.RelativeTimeFormat("bn-BD", { numeric: "auto" });
  if (Math.abs(diff) < 60) return "এইমাত্র";
  if (Math.abs(diff) < 3600) return rtf.format(-Math.round(diff / 60), "minute");
  if (Math.abs(diff) < 86400) return rtf.format(-Math.round(diff / 3600), "hour");
  return rtf.format(-Math.round(diff / 86400), "day");
}

/** Today's date in Bangladesh as yyyy-MM-dd. */
export function todayBd(offsetDays = 0): string {
  const d = new Date(Date.now() + offsetDays * 86400_000);
  return new Intl.DateTimeFormat("en-CA", { timeZone: TZ, year: "numeric", month: "2-digit", day: "2-digit" }).format(d);
}

/** Bangladesh week: 0 = Saturday … 6 = Friday. */
export const DAY_NAMES = ["শনিবার", "রবিবার", "সোমবার", "মঙ্গলবার", "বুধবার", "বৃহস্পতিবার", "শুক্রবার"];

export function bdDayOfWeek(isoDate: string) {
  const js = new Date(`${isoDate}T00:00:00Z`).getUTCDay(); // 0 = Sunday
  return (js + 1) % 7;
}

export type Tone = "neutral" | "blue" | "amber" | "green" | "red" | "violet" | "teal";

export const BOOKING_STATUS: Record<BookingStatus, { label: string; tone: Tone }> = {
  pending: { label: "অপেক্ষমাণ", tone: "amber" },
  confirmed: { label: "নিশ্চিত", tone: "blue" },
  rescheduled: { label: "সময় পরিবর্তন", tone: "violet" },
  completed: { label: "সম্পন্ন", tone: "green" },
  cancelled: { label: "বাতিল", tone: "red" },
  rejected: { label: "প্রত্যাখ্যাত", tone: "red" },
  no_show: { label: "উপস্থিত হননি", tone: "neutral" },
};

export const VERIFICATION_STATUS: Record<VerificationStatus, { label: string; tone: Tone }> = {
  draft: { label: "খসড়া", tone: "neutral" },
  submitted: { label: "জমা হয়েছে", tone: "amber" },
  under_review: { label: "পর্যালোচনা চলছে", tone: "blue" },
  info_requested: { label: "তথ্য প্রয়োজন", tone: "violet" },
  approved: { label: "যাচাইকৃত", tone: "teal" },
  rejected: { label: "সংশোধন প্রয়োজন", tone: "red" },
};

export const DOCUMENT_TYPES: Record<string, string> = {
  national_id: "জাতীয় পরিচয়পত্র",
  trade_license: "ট্রেড লাইসেন্স",
  tin_certificate: "TIN সার্টিফিকেট",
  bin_certificate: "BIN সার্টিফিকেট",
  professional_certificate: "পেশাগত সনদ",
  utility_bill: "ইউটিলিটি বিল",
  other: "অন্যান্য",
};

export const ROLE_NAMES: Record<string, string> = {
  business_owner: "মালিক",
  business_manager: "ম্যানেজার",
  business_staff: "স্টাফ",
  page_moderator: "পেজ মডারেটর",
  business_analyst: "বিশ্লেষক",
  super_admin: "সুপার এ্যাডমিন",
  admin: "এ্যাডমিন",
  system_moderator: "সিস্টেম মডারেটর",
  inspector: "ইন্সপেক্টর",
  marketer: "মার্কেটার",
  support_agent: "সাপোর্ট এজেন্ট",
};

export const REPORT_REASONS: Record<string, string> = {
  fake_business: "ভুয়া ব্যবসা",
  spam: "স্প্যাম",
  fraud: "প্রতারণা",
  abusive_behaviour: "অশোভন আচরণ",
  misleading_information: "বিভ্রান্তিকর তথ্য",
  other: "অন্যান্য",
};

export const MODERATION_ACTIONS: Record<string, string> = {
  start_review: "পর্যালোচনা শুরু",
  approve_business: "অনুমোদন",
  reject_business: "প্রত্যাখ্যান",
  request_information: "তথ্য চাওয়া হয়েছে",
  request_inspection: "মাঠ পরিদর্শনের অনুরোধ",
  suspend_business: "স্থগিত",
  reinstate_business: "পুনর্বহাল",
  resolve_report: "রিপোর্ট নিষ্পত্তি",
  dismiss_report: "রিপোর্ট খারিজ",
  escalate_report: "রিপোর্ট উচ্চতর পর্যায়ে",
  hide_review: "রিভিউ লুকানো",
  remove_review: "রিভিউ অপসারণ",
};
