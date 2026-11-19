import { useQuery } from "@tanstack/react-query";
import { api } from "../api/client";
import type { BusinessPlan, PaymentMethod, PlanFeature, PlansResponse, SubscriptionStatus } from "../api/types";
import { bn } from "./format";

export const usePlans = () => useQuery({ queryKey: ["plans"], queryFn: () => api.get<PlansResponse>("/api/plans"), staleTime: 10 * 60_000 });

export const useBusinessPlan = (businessId?: string) =>
  useQuery({ queryKey: ["business", businessId, "plan"], queryFn: () => api.get<BusinessPlan>(`/api/businesses/${businessId}/plan`), enabled: !!businessId, staleTime: 60_000 });

export const UNLIMITED = -1;

/** "৫টি", "সীমাহীন", "✓", "—" for a plan cell. */
export function featureValue(f: PlanFeature, v: number | undefined) {
  if (f.kind === "switch") return v ? "✓" : "—";
  if (v === UNLIMITED) return "সীমাহীন";
  return `${bn(v ?? 0)}${f.unitBn === "দিন" ? " দিন" : f.unitBn === "জন" ? " জন" : "টি"}`;
}

export const PLAN_STATUS: Record<SubscriptionStatus, { label: string; tone: "blue" | "green" | "amber" | "neutral" | "red" }> = {
  trialing: { label: "ট্রায়াল চলছে", tone: "blue" },
  active: { label: "সক্রিয়", tone: "green" },
  past_due: { label: "নবায়ন বাকি", tone: "amber" },
  cancelled: { label: "বাতিল", tone: "neutral" },
  expired: { label: "মেয়াদোত্তীর্ণ", tone: "neutral" },
};

export const METHOD_LABEL: Record<PaymentMethod, string> = {
  bkash: "বিকাশ", nagad: "নগদ", bank_transfer: "ব্যাংক ট্রান্সফার", cash: "নগদ টাকা", complimentary: "বিনামূল্যে প্রদান",
};

export const daysLeft = (iso: string | null) => (iso ? Math.max(0, Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000)) : 0);
