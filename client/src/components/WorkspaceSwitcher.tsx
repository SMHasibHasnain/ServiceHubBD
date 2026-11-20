import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { Check, ChevronDown, LogOut, Plus } from "lucide-react";
import { api } from "../api/client";
import type { Workspace, WorkspaceType } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { VERIFICATION_STATUS, bn } from "../lib/format";
import { Avatar } from "./ui";

export const workspacePath = (w: Workspace) => {
  switch (w.type) {
    case "business": return `/w/${w.businessId}`;
    case "moderation": return "/moderation";
    case "inspection": return "/inspections";
    case "administration": return "/admin";
    case "marketing": return "/me/bookings"; // marketing workspace not built yet
    default: return "/me/bookings";
  }
};

const TONES: Record<WorkspaceType, "neutral" | "blue" | "violet" | "amber" | "teal"> = {
  customer: "neutral", business: "blue", moderation: "violet", inspection: "amber", administration: "neutral", marketing: "teal",
};

const GROUPS: { label: string; types: WorkspaceType[] }[] = [
  { label: "ব্যক্তিগত", types: ["customer"] },
  { label: "ব্যবসা পেজ", types: ["business"] },
  { label: "প্ল্যাটফর্ম", types: ["moderation", "inspection", "marketing", "administration"] },
];

/** One account, many workspaces: switching never logs the user out. */
export function WorkspaceSwitcher({ current }: { current: { type: WorkspaceType; businessId?: string } }) {
  const { user, workspaces, logout } = useAuth();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false);
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => { document.removeEventListener("mousedown", onDown); document.removeEventListener("keydown", onKey); };
  }, [open]);

  const isCurrent = (w: Workspace) => w.type === current.type && (w.type !== "business" || w.businessId === current.businessId);
  const active = workspaces.find(isCurrent) ?? workspaces[0];
  const totalUnread = workspaces.filter((w) => !isCurrent(w)).reduce((s, w) => s + w.unreadNotifications, 0);

  const pick = (w: Workspace) => {
    setOpen(false);
    api.put("/api/me/workspace", { type: w.type, businessId: w.businessId }).catch(() => {});
    navigate(workspacePath(w));
  };

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="menu"
        aria-expanded={open}
        className="flex h-9 items-center gap-2 rounded-md border border-line bg-surface py-1 pr-2 pl-1 shadow-soft transition-colors duration-150 hover:bg-zinc-50"
      >
        <span className="relative">
          <Avatar name={active?.name ?? user?.fullName ?? "?"} tone={TONES[active?.type ?? "customer"]} className="size-7 text-xs" />
          {totalUnread > 0 && <span className="absolute -top-1 -right-1 size-3 rounded-full border-2 border-white bg-red-500" aria-label="অন্য ওয়ার্কস্পেসে নতুন নোটিফিকেশন" />}
        </span>
        <span className="hidden flex-col items-start text-left sm:flex">
          <span className="max-w-40 truncate text-sm font-medium">{active?.type === "customer" ? "গ্রাহক" : active?.name}</span>
        </span>
        <ChevronDown className="size-4 text-subtle" aria-hidden />
      </button>

      {open && (
        <div role="menu" className="absolute right-0 z-40 mt-2 w-[min(340px,calc(100vw-32px))] rounded-lg border border-line bg-surface p-1.5 shadow-pop">
          <div className="mb-1 flex items-center gap-3 border-b border-zinc-100 px-2.5 pt-1.5 pb-3">
            <Avatar name={user?.fullName ?? "?"} tone="neutral" className="size-8 rounded-full text-xs" />
            <div className="min-w-0">
              <div className="truncate text-sm font-medium">{user?.fullName}</div>
              <div className="text-xs text-subtle">একটি অ্যাকাউন্ট · {bn(workspaces.length)}টি ওয়ার্কস্পেস</div>
            </div>
          </div>
          {GROUPS.map((g) => {
            const items = workspaces.filter((w) => g.types.includes(w.type));
            if (items.length === 0) return null;
            return (
              <div key={g.label}>
                <div className="px-2.5 pt-3 pb-1 text-xs font-medium text-subtle">{g.label}</div>
                {items.map((w) => (
                  <button
                    key={`${w.type}-${w.businessId ?? w.zoneId ?? w.roleKey}`}
                    role="menuitem"
                    onClick={() => pick(w)}
                    className={`flex min-h-11 w-full items-center gap-2.5 rounded-md px-2.5 py-1.5 text-left ${isCurrent(w) ? "bg-zinc-100" : "hover:bg-zinc-100"}`}
                  >
                    <Avatar name={w.name} tone={TONES[w.type]} className="size-7 text-xs" />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-medium">{w.type === "customer" ? "গ্রাহক" : w.name}</span>
                      <span className="block truncate text-xs text-subtle">
                        {w.type === "business" ? `${w.roleName} · ${VERIFICATION_STATUS[w.verificationStatus ?? "draft"].label}` : w.subtitle}
                      </span>
                    </span>
                    {w.unreadNotifications > 0 && (
                      <span className="rounded-full bg-zinc-900 px-1.5 font-num text-[11px] font-medium leading-5 text-white" aria-label={`${w.unreadNotifications}টি অপঠিত`}>
                        {bn(w.unreadNotifications)}
                      </span>
                    )}
                    {isCurrent(w) && <Check className="size-4 text-primary" aria-label="বর্তমান" />}
                  </button>
                ))}
              </div>
            );
          })}
          <div className="mt-1 border-t border-zinc-100 pt-1">
            <button role="menuitem" onClick={() => { setOpen(false); navigate("/business/new"); }}
              className="flex min-h-10 w-full items-center gap-2.5 rounded-md px-2.5 text-sm font-medium hover:bg-zinc-100">
              <span className="flex size-7 items-center justify-center rounded-md border border-dashed border-zinc-300"><Plus className="size-4" aria-hidden /></span>
              নতুন ব্যবসা পেজ তৈরি করুন
            </button>
            <button role="menuitem" onClick={async () => { setOpen(false); await logout(); navigate("/"); }}
              className="flex min-h-10 w-full items-center gap-2.5 rounded-md px-2.5 text-sm text-zinc-600 hover:bg-zinc-100">
              <span className="flex size-7 items-center justify-center"><LogOut className="size-4" aria-hidden /></span>
              লগআউট
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
