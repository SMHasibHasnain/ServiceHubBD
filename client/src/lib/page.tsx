import type { CSSProperties } from "react";
import {
  Award, BadgePercent, Clock, Heart, Leaf, ShieldCheck, Sparkles, ThumbsUp, Truck, Users, Wallet, Wrench, type LucideIcon,
} from "lucide-react";
import type { HeaderStyle, SectionKey, ThemeKey } from "../api/types";

/** Accent colours a business can pick. Each passes WCAG AA for white text and as text on white. */
export const THEMES: Record<ThemeKey, { label: string; accent: string; hover: string; soft: string }> = {
  blue: { label: "নীল", accent: "#1f62c9", hover: "#164b98", soft: "#eaf1fc" },
  navy: { label: "গাঢ় নীল", accent: "#0d3169", hover: "#0a2652", soft: "#e8eef8" },
  teal: { label: "টিল", accent: "#0f766e", hover: "#115e59", soft: "#e6f5f3" },
  green: { label: "সবুজ", accent: "#2f7d1c", hover: "#256316", soft: "#eef7e9" },
  violet: { label: "বেগুনি", accent: "#6d28d9", hover: "#5b21b6", soft: "#f1ebfd" },
  rose: { label: "গোলাপি", accent: "#be123c", hover: "#9f1239", soft: "#fdecef" },
  orange: { label: "কমলা", accent: "#c2410c", hover: "#9a3412", soft: "#fdf0e8" },
  slate: { label: "ধূসর", accent: "#334155", hover: "#1e293b", soft: "#eef1f5" },
};

/** CSS variables that recolour primary buttons, links and accents inside the business page only. */
export const themeStyle = (key: ThemeKey | undefined): CSSProperties => {
  const t = THEMES[key ?? "blue"] ?? THEMES.blue;
  return {
    "--color-primary": t.accent,
    "--color-primary-hover": t.hover,
    "--color-primary-light": t.soft,
  } as CSSProperties;
};

export const HEADER_STYLES: Record<HeaderStyle, { label: string; hint: string }> = {
  cover: { label: "কভার ছবি", hint: "উপরে আপনার কভার ছবি দেখাবে" },
  color: { label: "রঙিন ব্যানার", hint: "ভালো কভার ছবি না থাকলে থিমের রঙে ব্যানার" },
};

export const SECTIONS: Record<SectionKey, { label: string; hideable: boolean }> = {
  notices: { label: "নোটিশ বোর্ড", hideable: true },
  highlights: { label: "কেন আমাদের বেছে নেবেন", hideable: true },
  services: { label: "সেবাসমূহ", hideable: false },
  gallery: { label: "গ্যালারি", hideable: true },
  reviews: { label: "গ্রাহক রিভিউ", hideable: false },
  faqs: { label: "সাধারণ প্রশ্নোত্তর", hideable: true },
  team: { label: "আমাদের টিম", hideable: true },
  about: { label: "পরিচিতি ও কাজের সময়", hideable: false },
};

export const HIGHLIGHT_ICONS: Record<string, { icon: LucideIcon; label: string }> = {
  shield: { icon: ShieldCheck, label: "নিরাপত্তা" },
  clock: { icon: Clock, label: "সময়" },
  award: { icon: Award, label: "পুরস্কার" },
  wallet: { icon: Wallet, label: "মূল্য" },
  users: { icon: Users, label: "টিম" },
  wrench: { icon: Wrench, label: "কারিগরি" },
  sparkles: { icon: Sparkles, label: "মান" },
  heart: { icon: Heart, label: "যত্ন" },
  truck: { icon: Truck, label: "হোম সার্ভিস" },
  "thumbs-up": { icon: ThumbsUp, label: "সন্তুষ্টি" },
  "badge-percent": { icon: BadgePercent, label: "অফার" },
  leaf: { icon: Leaf, label: "পরিবেশবান্ধব" },
};

export const LANGUAGES: Record<string, string> = { bn: "বাংলা", en: "ইংরেজি", hi: "হিন্দি", ar: "আরবি", ur: "উর্দু" };

export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
