import { useQuery } from "@tanstack/react-query";
import { api } from "../api/client";

/**
 * Homepage content model, editable by the super admin at /admin/homepage.
 *
 * The server stores one JSON document per page (`GET /api/site/home`). Everything here is the client's: the field
 * schema the editor renders, the built-in defaults, and `mergeHome`, which lays a stored document over the defaults.
 * Merging goes through the schema, so a stored document can only ever produce strings in known places: unknown keys
 * are dropped, missing ones fall back to the default, and lists are capped. Strings are always rendered as text.
 */

// ---------- Schema ----------

export type FieldDef =
  | { key: string; label: string; kind: "text"; hint?: string }
  | { key: string; label: string; kind: "textarea"; hint?: string }
  | { key: string; label: string; kind: "link"; hint?: string }
  | { key: string; label: string; kind: "select"; options: { value: string; label: string }[] }
  | { key: string; label: string; kind: "group"; fields: FieldDef[] }
  | { key: string; label: string; kind: "list"; itemLabel: string; titleKey: string; fields: FieldDef[]; max?: number };

export type Data = { [key: string]: string | Data | Data[] };

export const BUILT_IN_TYPES = ["hero", "verified", "categories", "steps", "featured", "seasonal", "testimonials", "areas", "faq", "business"] as const;
export const CUSTOM_TYPES = ["text", "cards", "banner"] as const;
export type SectionType = (typeof BUILT_IN_TYPES)[number] | (typeof CUSTOM_TYPES)[number];

export interface Section { id: string; type: SectionType; visible: boolean; data: Data }
export interface HomeContent { version: 1; site: Data; sections: Section[] }

const MAX_TEXT = 2000;
const MAX_LIST = 30;

const text = (key: string, label: string, hint?: string): FieldDef => ({ key, label, kind: "text", hint });
const area = (key: string, label: string, hint?: string): FieldDef => ({ key, label, kind: "textarea", hint });
const TONES: FieldDef = {
  key: "tone", label: "রং", kind: "select",
  options: [{ value: "cream", label: "ক্রিম (হালকা)" }, { value: "sand", label: "বালু রং" }, { value: "navy", label: "নীল (গাঢ়)" }, { value: "marigold", label: "হলুদ (নরম)" }],
};
const LINK_HINT = "সাইটের ভেতরের লিংক / দিয়ে শুরু করুন (যেমন /search?q=এসি), বাইরের লিংক https:// দিয়ে।";

const seasonFields = (label: string): FieldDef => ({
  key: "", label, kind: "group",
  fields: [
    text("title", "শিরোনাম"),
    text("note", "পাশের ছোট লেখা", "যেমন মাসের নাম"),
    { key: "picks", label: "সেবা", kind: "list", itemLabel: "সেবা", titleKey: "title", max: 6,
      fields: [text("title", "নাম"), area("body", "বিবরণ"), text("query", "খোঁজার শব্দ", "ক্লিক করলে এই শব্দ দিয়ে সার্চ হবে")] },
  ],
});

export const SITE_FIELDS: FieldDef[] = [
  text("navSearch", "মেনু: সেবা খোঁজা"),
  text("navBusiness", "মেনু: ব্যবসার জন্য"),
  text("navPricing", "মেনু: দাম"),
  text("login", "লগইন বাটন"),
  text("register", "অ্যাকাউন্ট খোলার বাটন"),
  area("footerAbout", "ফুটারের পরিচিতি"),
  text("footerCustomers", "ফুটার কলাম: গ্রাহকের জন্য"),
  text("footerBusiness", "ফুটার কলাম: ব্যবসার জন্য"),
  text("footerCopyright", "কপিরাইট লেখা", "সালের পরে বসবে"),
  text("footerTagline", "ফুটারের ট্যাগলাইন"),
];

export const SITE_DEFAULTS: Data = {
  navSearch: "সেবা খুঁজুন",
  navBusiness: "ব্যবসার জন্য",
  navPricing: "দাম",
  login: "লগইন",
  register: "অ্যাকাউন্ট খুলুন",
  footerAbout: "পাড়ার ভরসার মানুষগুলো খুঁজুন, বুক করুন। আর আপনার ব্যবসাকে দিন ডিজিটাল সাইনবোর্ড।",
  footerCustomers: "গ্রাহকের জন্য",
  footerBusiness: "ব্যবসার জন্য",
  footerCopyright: "ServiceHub BD · সর্বস্বত্ব সংরক্ষিত",
  footerTagline: "খুঁজুন · বুক করুন · বাড়ান",
};

export interface SectionDef { type: SectionType; label: string; description: string; custom: boolean; fields: FieldDef[]; defaults: Data }

export const SECTION_DEFS: Record<SectionType, SectionDef> = {
  hero: {
    type: "hero", label: "হিরো (সবার উপরে)", description: "বড় শিরোনাম, সার্চ বক্স আর জনপ্রিয় ক্যাটাগরি।", custom: false,
    fields: [
      text("eyebrow", "উপরের ছোট লেখা"),
      text("titleLine1", "শিরোনাম, প্রথম লাইন"),
      text("titleLine2", "শিরোনাম, রঙিন দ্বিতীয় লাইন"),
      area("subtitle", "শিরোনামের নিচের লেখা"),
      text("searchPlaceholder", "সার্চ বক্সের ভেতরের লেখা"),
      text("searchButton", "সার্চ বাটন"),
      text("popularLabel", "জনপ্রিয় ক্যাটাগরির আগের লেখা"),
    ],
    defaults: {
      eyebrow: "ঢাকা থেকে কুড়িগ্রাম, পাড়ার সেবা",
      titleLine1: "যেকোনো কাজে,",
      titleLine2: "ঠিক মানুষটা।",
      subtitle: "মিস্ত্রি, পার্লার, টিউটর, ফটোগ্রাফার। যাচাই করা, রিভিউ পড়া, দাম আগে থেকে জানা। ফোনে দরদাম ছাড়াই বুক করুন।",
      searchPlaceholder: "এসি মেরামত, মিরপুর",
      searchButton: "খুঁজুন",
      popularLabel: "জনপ্রিয়:",
    },
  },
  verified: {
    type: "verified", label: "আজকের যাচাইকৃত সেবাদাতা", description: "দোকানের মতো করে আঁকা একজন যাচাইকৃত সেবাদাতা, কয়েক সেকেন্ড পরপর বদলায়।", custom: false,
    fields: [
      text("eyebrow", "উপরের রঙিন লেখা"), text("title", "শিরোনাম"), area("body", "বিবরণ"),
      text("bookButton", "বুক বাটন"), text("profileButton", "প্রোফাইল বাটন"), text("newLabel", "রিভিউ না থাকলে লেখা"),
    ],
    defaults: {
      eyebrow: "যাচাই করা, ভরসার",
      title: "আজকের যাচাইকৃত সেবাদাতা",
      body: "প্রতিটি সেবাদাতার কাগজপত্র আমাদের মডারেটর দেখেছেন, দরকার হলে মাঠ পরিদর্শক নিজে গিয়ে কাজের জায়গা দেখে এসেছেন।",
      bookButton: "বুক করুন",
      profileButton: "প্রোফাইল দেখুন",
      newLabel: "নতুন সেবাদাতা",
    },
  },
  categories: {
    type: "categories", label: "ক্যাটাগরি", description: "সব প্রধান ক্যাটাগরি। ক্যাটাগরি যোগ বা লুকাতে “ক্যাটাগরি” পেজে যান।", custom: false,
    fields: [text("eyebrow", "উপরের রঙিন লেখা"), text("title", "শিরোনাম")],
    defaults: { eyebrow: "", title: "কী কাজ করাবেন?" },
  },
  steps: {
    type: "steps", label: "কীভাবে কাজ করে", description: "ধাপে ধাপে বুকিংয়ের নিয়ম।", custom: false,
    fields: [
      text("eyebrow", "উপরের রঙিন লেখা"), text("title", "শিরোনাম"),
      { key: "items", label: "ধাপ", kind: "list", itemLabel: "ধাপ", titleKey: "title", max: 6, fields: [text("title", "নাম"), area("body", "বিবরণ")] },
    ],
    defaults: {
      eyebrow: "সহজ নিয়ম",
      title: "তিন ধাপে কাজ শেষ",
      items: [
        { title: "খুঁজুন", body: "কী কাজ আর কোন এলাকা লিখুন। কাছের যাচাইকৃত সেবাদাতারা সামনে চলে আসবেন।" },
        { title: "মিলিয়ে দেখুন", body: "দাম, রিভিউ, কাজের ছবি আর ফাঁকা সময় এক পাতায়। ফোনে দরদাম লাগবে না।" },
        { title: "বুক করুন", body: "সময় বেছে বুকিং পাঠান। সেবাদাতা নিশ্চিত করলেই নোটিফিকেশন পাবেন।" },
      ],
    },
  },
  featured: {
    type: "featured", label: "সেরা সেবাদাতা", description: "সবচেয়ে বেশি রেটিংয়ের ছয়জন সেবাদাতার কার্ড।", custom: false,
    fields: [
      text("eyebrow", "উপরের রঙিন লেখা"), text("title", "শিরোনাম"), text("linkLabel", "“সব দেখুন” লিংক"),
      area("emptyText", "কোনো সেবাদাতা না থাকলে লেখা"), text("emptyButton", "কোনো সেবাদাতা না থাকলে বাটন"),
    ],
    defaults: {
      eyebrow: "প্রতিবেশীদের পছন্দ",
      title: "পাড়ার সেরা সেবাদাতা",
      linkLabel: "সবগুলো দেখুন",
      emptyText: "এখনও কোনো যাচাইকৃত ব্যবসা প্রকাশিত হয়নি। প্রথম সাইনবোর্ডটি আপনিই টাঙান!",
      emptyButton: "ব্যবসা পেজ খুলুন",
    },
  },
  seasonal: {
    type: "seasonal", label: "এই মৌসুমে", description: "মাস অনুযায়ী নিজে থেকে বদলায়: শীত ও বিয়ে (নভেম্বর–ফেব্রুয়ারি), গরম (মার্চ–জুন), বর্ষা (জুলাই–অক্টোবর)।", custom: false,
    fields: [
      text("eyebrow", "উপরের রঙিন লেখা"), text("linkLabel", "কার্ডের লিংকের লেখা"),
      { ...seasonFields("শীত ও বিয়ের মৌসুম (নভেম্বর–ফেব্রুয়ারি)"), key: "winter" },
      { ...seasonFields("গরম (মার্চ–জুন)"), key: "summer" },
      { ...seasonFields("বর্ষা (জুলাই–অক্টোবর)"), key: "monsoon" },
    ],
    defaults: {
      eyebrow: "এই মৌসুমে",
      linkLabel: "সেবাদাতা দেখুন",
      winter: {
        title: "বিয়ের মৌসুম আর শীত, দুটোই দরজায়।", note: "অগ্রহায়ণ থেকে ফাল্গুন",
        picks: [
          { title: "গিজার সার্ভিসিং", body: "কুয়াশা নামার আগে চেক করিয়ে নিন। প্রথম ঠান্ডার সপ্তাহে ভিড় লাগে।", query: "গিজার" },
          { title: "মেহেদি ও ব্রাইডাল মেকআপ", body: "হলুদ আর বিয়ের দিনের জন্য আগে থেকে বুক করুন, বাড়িতে এসে করে দেবে।", query: "মেকআপ" },
          { title: "বিয়ের ফটোগ্রাফি", body: "আগের কাজের অ্যালবাম দেখে বেছে নিন। তারিখ ফাঁকা কিনা সাথে সাথে জানুন।", query: "ফটোগ্রাফি" },
        ],
      },
      summer: {
        title: "গরম পড়ছে, ঠান্ডা রাখুন।", note: "চৈত্র থেকে আষাঢ়",
        picks: [
          { title: "এসি সার্ভিসিং", body: "প্রথম গরমের সপ্তাহে স্লট পাওয়া কঠিন। আগেভাগে গ্যাস আর ফিল্টার দেখিয়ে নিন।", query: "এসি" },
          { title: "ফ্রিজ মেরামত", body: "লোডশেডিংয়ের পর কম্প্রেসর ঝামেলা? বাসায় এসে দেখে যাবে।", query: "ফ্রিজ" },
          { title: "আইপিএস ও ওয়্যারিং", body: "ব্যাটারি চেক আর লোড হিসাব করে নিন, গরমে বিদ্যুৎ যায়ই।", query: "আইপিএস" },
        ],
      },
      monsoon: {
        title: "বর্ষায় ঘর রাখুন শুকনো।", note: "শ্রাবণ থেকে কার্তিক",
        picks: [
          { title: "ছাদ ও পানির লিক", body: "দেয়ালে স্যাঁতসেঁতে দাগ বড় হওয়ার আগেই সারিয়ে নিন।", query: "লিক" },
          { title: "পোকামাকড় দমন", body: "বর্ষায় তেলাপোকা আর উইপোকা বাড়ে। পরিবারের জন্য নিরাপদ ওষুধে।", query: "পেস্ট কন্ট্রোল" },
          { title: "উৎসবের আগে ক্লিনিং", body: "পূজা আর ছুটির আগে পুরো বাসা এক দিনে।", query: "ক্লিনিং" },
        ],
      },
    },
  },
  testimonials: {
    type: "testimonials", label: "গ্রাহকদের কথা", description: "গ্রাহকদের মন্তব্যের দেয়াল। প্রকাশের আগে আসল গ্রাহকের অনুমতি নিয়ে আসল মন্তব্য দিন।", custom: false,
    fields: [
      text("eyebrow", "উপরের রঙিন লেখা"), text("title", "শিরোনাম"),
      { key: "items", label: "মন্তব্য", kind: "list", itemLabel: "মন্তব্য", titleKey: "name", max: MAX_LIST,
        fields: [area("quote", "মন্তব্য"), text("name", "নাম"), text("area", "এলাকা"), text("service", "সেবা")] },
    ],
    // SAMPLE CONTENT: written placeholders, not real reviews. Replace with real, permitted quotes before launch.
    defaults: {
      eyebrow: "খুশি গ্রাহকদের কথা",
      title: "পাড়ার মানুষ যা বলছেন",
      items: [
        { quote: "সকাল দশটায় আসার কথা, পৌনে দশটায় দরজায়। আধা ঘণ্টায় গ্যাস লিক ধরে ফেলল, বিলও আগে যা বলেছিল তাই।", name: "নাসরিন আক্তার", area: "উত্তরা, ঢাকা", service: "এসি মেরামত" },
        { quote: "ঈদের আগে পুরো ফ্ল্যাট এক দিনে ঝকঝকে। ফোনে দশবার দরদাম করতে হয়নি, দাম আগেই লেখা ছিল।", name: "তানভীর হাসান", area: "ধানমন্ডি, ঢাকা", service: "বাসা পরিষ্কার" },
        { quote: "রাত নয়টায় ফিউজ গেল, অ্যাপে দেখলাম কাছেই একজন ফাঁকা। চল্লিশ মিনিটে আলো ফিরল।", name: "শাহানা পারভীন", area: "মোহাম্মদপুর, ঢাকা", service: "ইলেকট্রিক্যাল" },
        { quote: "হলুদের দিনে বাড়িতে এসে মেহেদি দিয়ে গেল। আগের কাজের ছবি দেখে বেছেছিলাম, ঠকিনি।", name: "রুমানা ইসলাম", area: "গাজীপুর", service: "মেহেদি ও মেকআপ" },
        { quote: "ছেলের জন্য গণিতের টিউটর খুঁজছিলাম। রিভিউ পড়ে বেছে নিলাম, তিন মাসে রেজাল্ট বদলে গেছে।", name: "মোস্তফা কামাল", area: "আগ্রাবাদ, চট্টগ্রাম", service: "টিউশন" },
        { quote: "বাথরুমের পাইপ লিক নিয়ে দুই সপ্তাহ ভুগেছি। এখানে বুক করলাম, পরদিন সকালেই কাজ শেষ।", name: "ফারহানা ইয়াসমিন", area: "জিন্দাবাজার, সিলেট", service: "প্লাম্বিং" },
        { quote: "যাচাইকৃত ব্যাজ দেখে ভরসা পেয়েছি। একা থাকি, অচেনা কাউকে ঘরে ঢোকাতে এটা খুব জরুরি।", name: "সুলতানা রাজিয়া", area: "মিরপুর, ঢাকা", service: "ফ্রিজ মেরামত" },
        { quote: "বিয়ের ফটোগ্রাফার খুঁজতে গিয়ে তারিখ ফাঁকা কিনা সাথে সাথে দেখতে পেলাম। অ্যালবাম অসাধারণ হয়েছে।", name: "আরিফ রহমান", area: "রাজশাহী শহর", service: "ফটোগ্রাফি" },
        { quote: "অফিসের দশটা কম্পিউটারের নেটওয়ার্ক এক বিকেলে ঠিক। বিল আর কাজের বিবরণ সব অ্যাপেই পেলাম।", name: "জাকির হোসেন", area: "খুলনা সদর", service: "আইটি সাপোর্ট" },
      ],
    },
  },
  areas: {
    type: "areas", label: "বিভাগ", description: "দেশের সব বিভাগের বোতাম, ক্লিক করলে সেই এলাকার সার্চ।", custom: false,
    fields: [text("eyebrow", "উপরের রঙিন লেখা"), text("title", "শিরোনাম")],
    defaults: { eyebrow: "সারা দেশে", title: "আপনার বিভাগ বেছে নিন" },
  },
  faq: {
    type: "faq", label: "প্রশ্ন ও উত্তর", description: "গ্রাহকদের সাধারণ প্রশ্ন।", custom: false,
    fields: [
      text("eyebrow", "উপরের রঙিন লেখা"), text("title", "শিরোনাম"),
      { key: "items", label: "প্রশ্ন", kind: "list", itemLabel: "প্রশ্ন", titleKey: "q", max: MAX_LIST, fields: [text("q", "প্রশ্ন"), area("a", "উত্তর")] },
    ],
    defaults: {
      eyebrow: "জিজ্ঞাসা",
      title: "যা সবাই জানতে চান",
      items: [
        { q: "বুকিং করতে কি টাকা লাগে?", a: "না, বুকিং বিনামূল্যে। সেবার দাম আপনি সরাসরি সেবাদাতাকে দেবেন, তাঁদের পেজে আগে থেকেই দাম লেখা থাকে।" },
        { q: "“যাচাইকৃত” মানে কী?", a: "সেবাদাতা তাঁদের কাগজপত্র জমা দিয়েছেন, আমাদের মডারেটর তা দেখেছেন, আর দরকার হলে মাঠ পরিদর্শক গিয়ে কাজের জায়গা দেখে এসেছেন।" },
        { q: "রিভিউগুলো কি আসল?", a: "শুধু যাঁদের বুকিং সম্পন্ন হয়েছে তাঁরাই রিভিউ দিতে পারেন। তাই না গিয়ে কেউ রিভিউ লিখতে পারে না।" },
        { q: "সময় বদলাতে বা বাতিল করতে চাইলে?", a: "“আমার বুকিং” থেকে সময় বদলানো বা বাতিল করা যায়। প্রতিটি সেবাদাতার নিজস্ব বাতিলের নিয়ম বুকিংয়ের সময় দেখানো হয়।" },
        { q: "আমার এলাকায় কি সেবা পাওয়া যায়?", a: "বিভাগ থেকে ইউনিয়ন পর্যন্ত এলাকা বেছে খুঁজতে পারেন। নতুন সেবাদাতা প্রতিদিন যুক্ত হচ্ছেন।" },
      ],
    },
  },
  business: {
    type: "business", label: "ব্যবসার জন্য", description: "সেবাদাতাদের পেজ খোলার আমন্ত্রণ (মেনুর “ব্যবসার জন্য” এখানে নামে)।", custom: false,
    fields: [
      text("titleLine1", "শিরোনাম, প্রথম লাইন"), text("titleLine2", "শিরোনাম, রঙিন দ্বিতীয় লাইন"), area("body", "বিবরণ"),
      text("primaryButton", "প্রধান বাটন"), text("secondaryButton", "দ্বিতীয় বাটন"),
      { key: "points", label: "সুবিধা", kind: "list", itemLabel: "সুবিধা", titleKey: "title", max: 6, fields: [text("title", "নাম"), area("body", "বিবরণ")] },
    ],
    defaults: {
      titleLine1: "আপনার ব্যবসার",
      titleLine2: "ডিজিটাল সাইনবোর্ড।",
      body: "পেজ খুলতে টাকা লাগে না। ব্যবসা বাড়লে প্ল্যান বেছে নিন।",
      primaryButton: "ব্যবসা পেজ খুলুন",
      secondaryButton: "প্ল্যান ও দাম",
      points: [
        { title: "বুকিং খাতা", body: "কে কখন আসবে, কোন মিস্ত্রি যাবে, সব এক পাতায়। ডাবল বুকিং আর হবে না।" },
        { title: "পুরোনো গ্রাহক", body: "কার বাসায় কোন কাজ হয়েছে, শেষ সার্ভিস কবে, সব মনে থাকবে।" },
        { title: "যাচাই সিল", body: "ডকুমেন্ট আর মাঠ পরিদর্শনের পর আপনার পেজে সবুজ সিল।" },
      ],
    },
  },

  // ----- custom sections: added, removed and repeated freely -----
  text: {
    type: "text", label: "লেখা ও বাটন", description: "শিরোনাম, অনুচ্ছেদ আর একটি বাটন। ঘোষণা, অফার বা যেকোনো বার্তার জন্য।", custom: true,
    fields: [
      text("eyebrow", "উপরের রঙিন লেখা"), text("title", "শিরোনাম"), area("body", "লেখা"),
      text("buttonLabel", "বাটনের লেখা", "খালি রাখলে বাটন দেখাবে না"), { key: "buttonLink", label: "বাটনের লিংক", kind: "link", hint: LINK_HINT },
      TONES,
    ],
    defaults: { eyebrow: "", title: "নতুন সেকশন", body: "", buttonLabel: "", buttonLink: "", tone: "cream" },
  },
  cards: {
    type: "cards", label: "কার্ডের সারি", description: "শিরোনামের নিচে কয়েকটি কার্ড, প্রতিটিতে ঐচ্ছিক লিংক।", custom: true,
    fields: [
      text("eyebrow", "উপরের রঙিন লেখা"), text("title", "শিরোনাম"), TONES,
      { key: "items", label: "কার্ড", kind: "list", itemLabel: "কার্ড", titleKey: "title", max: 12,
        fields: [text("title", "শিরোনাম"), area("body", "লেখা"), text("linkLabel", "লিংকের লেখা"), { key: "link", label: "লিংক", kind: "link", hint: LINK_HINT }] },
    ],
    defaults: {
      eyebrow: "", title: "নতুন কার্ড সেকশন", tone: "cream",
      items: [{ title: "প্রথম কার্ড", body: "", linkLabel: "", link: "" }, { title: "দ্বিতীয় কার্ড", body: "", linkLabel: "", link: "" }],
    },
  },
  banner: {
    type: "banner", label: "ঘোষণার ফিতা", description: "এক লাইনের ঘোষণা আর একটি বাটন, পুরো প্রস্থ জুড়ে।", custom: true,
    fields: [
      text("text", "ঘোষণা"), text("buttonLabel", "বাটনের লেখা", "খালি রাখলে বাটন দেখাবে না"),
      { key: "buttonLink", label: "বাটনের লিংক", kind: "link", hint: LINK_HINT }, TONES,
    ],
    defaults: { text: "নতুন ঘোষণা", buttonLabel: "", buttonLink: "", tone: "marigold" },
  },
};

export const DEFAULT_HOME: HomeContent = {
  version: 1,
  site: SITE_DEFAULTS,
  sections: BUILT_IN_TYPES.map((type) => ({ id: type, type, visible: true, data: SECTION_DEFS[type].defaults })),
};

// ---------- Merge ----------

const isObj = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null && !Array.isArray(v);
const clip = (v: string) => v.slice(0, MAX_TEXT);

/** Lays `stored` over `def` through the schema; the result only ever holds strings, groups and lists the schema names. */
export function mergeFields(fields: FieldDef[], def: unknown, stored: unknown): Data {
  const d = isObj(def) ? def : {};
  const s = isObj(stored) ? stored : {};
  const out: Data = {};
  for (const f of fields) {
    const dv = d[f.key];
    const sv = s[f.key];
    switch (f.kind) {
      case "group":
        out[f.key] = mergeFields(f.fields, dv, sv);
        break;
      case "list": {
        const source = Array.isArray(sv) ? sv : Array.isArray(dv) ? dv : [];
        out[f.key] = source.slice(0, f.max ?? MAX_LIST).map((item) => mergeFields(f.fields, undefined, item));
        break;
      }
      case "select": {
        const fallback = typeof dv === "string" ? dv : f.options[0].value;
        out[f.key] = typeof sv === "string" && f.options.some((o) => o.value === sv) ? sv : fallback;
        break;
      }
      default:
        out[f.key] = typeof sv === "string" ? clip(sv) : typeof dv === "string" ? dv : "";
    }
  }
  return out;
}

/** Blank values for a new list item or custom section. */
export function emptyFrom(fields: FieldDef[]): Data {
  return mergeFields(fields, undefined, undefined);
}

const ID_RE = /^[a-z0-9-]{1,40}$/;

export function mergeHome(stored: unknown): HomeContent {
  const s = isObj(stored) ? stored : {};
  const sections: Section[] = [];
  const ids = new Set<string>();
  for (const raw of Array.isArray(s.sections) ? s.sections : []) {
    if (!isObj(raw) || typeof raw.type !== "string" || !(raw.type in SECTION_DEFS)) continue;
    const def = SECTION_DEFS[raw.type as SectionType];
    const id = typeof raw.id === "string" && ID_RE.test(raw.id) ? raw.id : null;
    if (!id || ids.has(id)) continue;
    if (!def.custom && sections.some((x) => x.type === def.type)) continue; // built-ins appear once
    ids.add(id);
    sections.push({ id, type: def.type, visible: raw.visible !== false, data: mergeFields(def.fields, def.defaults, raw.data) });
  }
  // A built-in missing from the stored list (e.g. one added in a later release) goes back in right after the
  // built-in that precedes it by default, or at the top if none of those are present.
  BUILT_IN_TYPES.forEach((type, i) => {
    if (sections.some((x) => x.type === type)) return;
    const before = BUILT_IN_TYPES.slice(0, i);
    let at = 0;
    sections.forEach((x, j) => { if ((before as readonly string[]).includes(x.type)) at = j + 1; });
    sections.splice(at, 0, { id: type, type, visible: true, data: mergeFields(SECTION_DEFS[type].fields, SECTION_DEFS[type].defaults, undefined) });
  });
  return { version: 1, site: mergeFields(SITE_FIELDS, SITE_DEFAULTS, s.site), sections };
}

export function newSection(type: (typeof CUSTOM_TYPES)[number]): Section {
  const def = SECTION_DEFS[type];
  return { id: `${type}-${Math.random().toString(36).slice(2, 8)}`, type, visible: true, data: mergeFields(def.fields, def.defaults, undefined) };
}

// ---------- Rendering helpers ----------

/** Only same-site paths ("/search") and https URLs are links; anything else (javascript:, data:, //host) is dropped. */
export function safeHref(v: unknown): { href: string; external: boolean } | null {
  if (typeof v !== "string") return null;
  const t = v.trim();
  if (/^\/(?![/\\])/.test(t)) return { href: t, external: false };
  if (/^https:\/\/[^\s]+$/i.test(t)) return { href: t, external: true };
  return null;
}

export const str = (d: Data, key: string): string => (typeof d[key] === "string" ? (d[key] as string) : "");
export const list = (d: Data, key: string): Data[] => (Array.isArray(d[key]) ? (d[key] as Data[]) : []);
export const group = (d: Data, key: string): Data => (isObj(d[key]) ? (d[key] as Data) : {});

export type SiteContentResponse = { key: string; content: unknown; updatedAt: string | null };

export const HOME_QUERY_KEY = ["site", "home"] as const;

/** Saved homepage content merged over the defaults. While loading, `data` is undefined. */
export function useHomeContent() {
  return useQuery({
    queryKey: HOME_QUERY_KEY,
    queryFn: () => api.get<SiteContentResponse>("/api/site/home"),
    select: (r) => mergeHome(r.content),
    staleTime: 60_000,
  });
}

/** Header and footer text: the saved values, or the defaults until they load (or if loading fails). */
export function useSiteText(): Data {
  const { data } = useHomeContent();
  return data?.site ?? SITE_DEFAULTS;
}
