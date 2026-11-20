import { Suspense, useEffect, useState, type ReactNode } from "react";
import { Link, NavLink, Outlet, useLocation, useParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import {
  BarChart3, Bell, CalendarDays, ClipboardCheck, CreditCard, Eye, Flag, Home, LayoutDashboard, Menu, Package, ScrollText,
  Search, Settings, ShieldCheck, Star, User as UserIcon, Users, UsersRound,
  Palette, Heart, Briefcase,
} from "lucide-react";
import { api, session } from "../api/client";
import type { BusinessDetail, WorkspaceType } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { VERIFICATION_STATUS, bn } from "../lib/format";
import { WorkspaceSwitcher } from "./WorkspaceSwitcher";
import { Logo, LogoFull } from "./brand";

export { Logo };
import { Avatar, Badge, LinkButton } from "./ui";

const PageLoading = () => <div className="p-10 text-center text-muted" role="status">তথ্য লোড হচ্ছে...</div>;


function NotificationBell({ to }: { to: string }) {
  const { workspaces } = useAuth();
  const unread = workspaces.reduce((s, w) => s + w.unreadNotifications, 0);
  return (
    <Link to={to} className="relative flex size-9 items-center justify-center rounded-md text-zinc-600 hover:bg-zinc-100 hover:text-ink"
      aria-label={unread ? `নোটিফিকেশন, ${unread}টি নতুন` : "নোটিফিকেশন"}>
      <Bell className="size-[18px]" aria-hidden />
      {unread > 0 && <span className="absolute top-0.5 right-0.5 min-w-4 rounded-full bg-red-600 px-1 text-center font-num text-[10px] font-medium leading-4 text-white">{bn(unread)}</span>}
    </Link>
  );
}

/** Topbar for guests and the customer workspace. */
function PublicTopbar() {
  const { status } = useAuth();
  return (
    <header className="sticky top-0 z-30 border-b border-line/80 bg-white/85 backdrop-blur-md">
      <div className="mx-auto flex h-16 max-w-6xl items-center gap-x-8 px-4 sm:px-6">
        <Logo compact />
        <nav aria-label="প্রধান মেনু" className="hidden flex-1 gap-7 text-[15px] font-medium md:flex">
          <NavLink to="/search" className="text-muted transition-colors hover:text-primary">সেবা খুঁজুন</NavLink>
          <Link to="/#how" className="text-muted transition-colors hover:text-primary">কীভাবে কাজ করে</Link>
          <Link to="/business/new" className="text-muted transition-colors hover:text-primary">ব্যবসার জন্য</Link>
        </nav>
        <div className="ml-auto flex items-center gap-1.5">
          {status === "authenticated" ? (
            <>
              <NotificationBell to="/me/notifications" />
              <WorkspaceSwitcher current={{ type: "customer" }} />
            </>
          ) : status === "anonymous" ? (
            <>
              <LinkButton to="/login" variant="ghost" size="sm">লগইন করুন</LinkButton>
              <LinkButton to="/register" size="sm" className="hidden sm:inline-flex">অ্যাকাউন্ট তৈরি করুন</LinkButton>
            </>
          ) : null}
        </div>
      </div>
    </header>
  );
}

function Footer() {
  const year = new Date().getFullYear().toLocaleString("bn-BD", { useGrouping: false });
  return (
    <footer className="mt-24 border-t border-line bg-white">
      <div className="mx-auto grid max-w-6xl gap-10 px-4 py-12 sm:px-6 md:grid-cols-[1.4fr_1fr_1fr_1fr]">
        <div className="flex flex-col items-start gap-4">
          <LogoFull className="h-28 w-auto" />
          <p className="max-w-xs text-sm text-muted">বাংলাদেশের বিশ্বস্ত স্থানীয় সেবা খুঁজুন, বুক করুন — আর আপনার ব্যবসাকে ডিজিটালভাবে এগিয়ে নিন।</p>
        </div>
        {[
          { title: "গ্রাহকের জন্য", links: [["সেবা খুঁজুন", "/search"], ["আমার বুকিং", "/me/bookings"], ["কীভাবে কাজ করে", "/#how"]] },
          { title: "ব্যবসার জন্য", links: [["ব্যবসা পেজ তৈরি", "/business/new"], ["প্ল্যান ও মূল্য", "/pricing"], ["যাচাইকরণ", "/#how"], ["লগইন", "/login"]] },
          { title: "ServiceHub BD", links: [["গোপনীয়তা নীতি", "#"], ["শর্তাবলি", "#"], ["যোগাযোগ", "#"]] },
        ].map((col) => (
          <nav key={col.title} aria-label={col.title} className="flex flex-col gap-2.5 text-sm">
            <span className="font-display text-[15px] font-semibold text-ink">{col.title}</span>
            {col.links.map(([label, to]) => <Link key={label} to={to} className="text-muted hover:text-primary">{label}</Link>)}
          </nav>
        ))}
      </div>
      <div className="border-t border-line">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-2 px-4 py-5 text-xs text-subtle sm:px-6">
          <span>© {year} ServiceHub BD · সর্বস্বত্ব সংরক্ষিত</span>
          <span className="font-num tracking-[0.2em]">DISCOVER · BOOK · CONNECT · GROW</span>
        </div>
      </div>
    </footer>
  );
}

export function PublicLayout() {
  return (
    <div className="flex min-h-dvh flex-col">
      <PublicTopbar />
      <main className="flex-1"><Suspense fallback={<PageLoading />}><Outlet /></Suspense></main>
      <Footer />
    </div>
  );
}

const CUSTOMER_NAV = [
  { to: "/", label: "হোম", icon: Home, end: true },
  { to: "/search", label: "অনুসন্ধান", icon: Search },
  { to: "/me/bookings", label: "বুকিং", icon: CalendarDays },
  { to: "/me/saved", label: "সংরক্ষিত", icon: Heart, desktopOnly: true },
  { to: "/me/notifications", label: "নোটিফিকেশন", icon: Bell },
  { to: "/me/profile", label: "প্রোফাইল", icon: UserIcon },
];

/**
 * Customer workspace in the same `Shell` as business and platform workspaces: sidebar at the screen edge,
 * full-width topbar, readable content width. Phones also get a bottom bar, but only on `/me/...` pages: the
 * business profile and booking wizard have their own fixed bottom "book" bar, and the drawer still has the menu.
 */
export function CustomerLayout({ wide = false }: { wide?: boolean }) {
  const { pathname } = useLocation();
  const bottomBar = pathname.startsWith("/me/");
  const sidebar = (
    <>
      <SideNav items={CUSTOMER_NAV} header={null} />
      <div className="px-3 pb-4">
        <div className="bg-brand relative flex flex-col gap-1.5 overflow-hidden rounded-lg p-4 text-sm text-white shadow-card">
          <div className="absolute -right-6 -bottom-8 size-24 rounded-full bg-brand-lime/30" aria-hidden />
          <span className="font-display text-base font-semibold">আপনার কি ব্যবসা আছে?</span>
          <span className="text-blue-100">একই অ্যাকাউন্ট থেকে ব্যবসা পেজ তৈরি করুন</span>
          <Link to="/business/new" className="relative mt-2 inline-flex min-h-9 items-center justify-center rounded-md bg-white px-3 font-semibold text-brand-navy hover:bg-blue-50">ব্যবসা পেজ তৈরি করুন →</Link>
        </div>
      </div>
    </>
  );
  return (
    <>
      <Shell wide={wide} bottomPadding={bottomBar} sidebar={sidebar}
        topbar={<div className="ml-auto flex items-center gap-1.5"><NotificationBell to="/me/notifications" /><WorkspaceSwitcher current={{ type: "customer" }} /></div>}>
        <Suspense fallback={<PageLoading />}><Outlet /></Suspense>
      </Shell>
      {bottomBar && (
        <nav aria-label="নিচের মেনু" className="fixed inset-x-0 bottom-0 z-30 grid grid-cols-5 border-t border-line bg-white/95 pb-[env(safe-area-inset-bottom)] backdrop-blur-md md:hidden">
          {/* The bottom bar has room for five; "saved" is reached from My bookings on phones. */}
          {CUSTOMER_NAV.filter((n) => !n.desktopOnly).map(({ to, label, icon: Icon, end }) => (
            <NavLink key={to} to={to} end={end}
              className={({ isActive }) => `group flex min-h-16 flex-col items-center justify-center gap-1 text-[11px] font-medium ${isActive ? "text-primary" : "text-subtle"}`}>
              {({ isActive }) => <><span className={`flex h-7 w-12 items-center justify-center rounded-full transition-colors ${isActive ? "bg-primary-light" : ""}`}><Icon className="size-5" aria-hidden /></span>{label}</>}
            </NavLink>
          ))}
        </nav>
      )}
    </>
  );
}

/**
 * Search, business profiles and the booking wizard: guests get the public topbar and footer; signed-in people keep
 * their customer sidebar. While the session is still loading, a stored refresh token predicts the outcome, so the
 * page doesn't flash from one layout to the other.
 */
export function BrowseLayout() {
  const { status } = useAuth();
  const signedIn = status === "authenticated" || (status === "loading" && !!session.refreshToken());
  return signedIn ? <CustomerLayout wide /> : <PublicLayout />;
}

// ---------- Sidebar workspaces (business / platform) ----------

type NavItem = { to: string; label: string; icon: typeof Home; end?: boolean; permission?: string };

/**
 * App shell for every signed-in workspace (customer, business, platform). `wide` lets the page set its own width
 * and padding (public pages shown inside the shell); `bottomPadding` leaves room for a phone bottom bar.
 */
function Shell({ sidebar, topbar, children, wide = false, bottomPadding = false }: {
  sidebar: ReactNode; topbar: ReactNode; children: ReactNode; wide?: boolean; bottomPadding?: boolean;
}) {
  const [drawer, setDrawer] = useState(false);
  const location = useLocation();
  useEffect(() => setDrawer(false), [location.pathname]);
  return (
    <div className="min-h-dvh lg:flex">
      <aside className="sticky top-0 hidden h-dvh w-64 shrink-0 overflow-y-auto border-r border-line bg-white lg:block">{sidebar}</aside>
      {drawer && (
        <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label="মেনু">
          <button className="absolute inset-0 bg-zinc-950/30" onClick={() => setDrawer(false)} aria-label="মেনু বন্ধ করুন" />
          <aside className="relative h-full w-72 max-w-[85vw] overflow-y-auto bg-surface shadow-pop">{sidebar}</aside>
        </div>
      )}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-16 items-center gap-3 border-b border-line bg-white/85 px-4 backdrop-blur-md sm:px-6">
          <button className="flex size-10 items-center justify-center rounded-md border border-line bg-white lg:hidden" onClick={() => setDrawer(true)} aria-label="মেনু খুলুন">
            <Menu className="size-[18px]" aria-hidden />
          </button>
          {topbar}
        </header>
        <main className={`${wide ? "w-full" : "mx-auto w-full max-w-6xl px-4 py-8 sm:px-8"} flex-1 ${bottomPadding ? "pb-24 md:pb-8" : ""}`}>{children}</main>
      </div>
    </div>
  );
}

function SideNav({ items, permissions, header }: { items: (NavItem & { desktopOnly?: boolean })[]; permissions?: string[]; header: ReactNode }) {
  return (
    <nav className="flex flex-col gap-0.5 p-3" aria-label="ওয়ার্কস্পেস মেনু">
      <div className="flex h-12 items-center px-2 pb-2"><Logo to="/me/bookings" /></div>
      {header}
      {items.filter((i) => !i.permission || permissions?.includes(i.permission)).map(({ to, label, icon: Icon, end }) => (
        <NavLink key={to} to={to} end={end}
          className={({ isActive }) => `flex min-h-10 items-center gap-2.5 rounded-md px-3 text-sm font-medium transition-colors ${isActive ? "bg-primary-light text-primary" : "text-muted hover:bg-zinc-100 hover:text-ink"}`}>
          <Icon className="size-4" aria-hidden />{label}
        </NavLink>
      ))}
    </nav>
  );
}

export function useBusiness(businessId: string | undefined) {
  return useQuery({
    queryKey: ["business", businessId],
    queryFn: () => api.get<BusinessDetail>(`/api/businesses/${businessId}`),
    enabled: !!businessId,
  });
}

/** Business workspace: everything is scoped to /w/:businessId. */
export function BusinessLayout() {
  const { businessId } = useParams();
  const { data: biz, error } = useBusiness(businessId);
  const base = `/w/${businessId}`;
  const items: NavItem[] = [
    { to: base, label: "ড্যাশবোর্ড", icon: LayoutDashboard, end: true },
    { to: `${base}/my-day`, label: "আমার কাজ", icon: Briefcase },
    { to: `${base}/bookings`, label: "অ্যাপয়েন্টমেন্ট", icon: CalendarDays },
    { to: `${base}/customers`, label: "গ্রাহক", icon: Users, permission: "customers.view" },
    { to: `${base}/services`, label: "সেবাসমূহ", icon: Package },
    { to: `${base}/reviews`, label: "রিভিউ", icon: Star },
    { to: `${base}/analytics`, label: "বিশ্লেষণ", icon: BarChart3, permission: "analytics.view" },
    { to: `${base}/team`, label: "টিম ও ভূমিকা", icon: UsersRound, permission: "members.view" },
    { to: `${base}/page`, label: "পেজ সাজান", icon: Palette, permission: "business.update" },
    { to: `${base}/settings`, label: "ব্যবসার সেটিংস", icon: Settings, permission: "business.update" },
    { to: `${base}/billing`, label: "সাবস্ক্রিপশন", icon: CreditCard, permission: "billing.manage" },
  ];
  const header = biz && (
    <div className="mt-1 mb-3 flex items-center gap-2.5 rounded-lg border border-line bg-bg px-2.5 py-2.5">
      <Avatar name={biz.name} className="size-9 text-xs" />
      <div className="min-w-0">
        <div className="truncate text-sm font-medium">{biz.name}</div>
        <Badge tone={VERIFICATION_STATUS[biz.verificationStatus].tone} className="mt-0.5">{VERIFICATION_STATUS[biz.verificationStatus].label}</Badge>
      </div>
    </div>
  );
  return (
    <Shell
      sidebar={
        <>
          <SideNav items={items} permissions={biz?.myPermissions} header={header} />
          {biz?.status === "published" && (
            <div className="px-3 pb-4">
              <Link to={`/b/${biz.slug}`} className="flex min-h-9 items-center gap-2.5 rounded-md px-2.5 text-sm font-medium text-zinc-600 hover:bg-zinc-100 hover:text-ink">
                <Eye className="size-4" aria-hidden />পাবলিক পেজ দেখুন
              </Link>
            </div>
          )}
        </>
      }
      topbar={<><div className="min-w-0 flex-1 truncate text-sm font-medium lg:hidden">{biz?.name}</div><div className="ml-auto flex items-center gap-1.5"><NotificationBell to={`${base}/notifications`} /><WorkspaceSwitcher current={{ type: "business", businessId }} /></div></>}
    >
      {error ? <div className="card p-8 text-center text-muted">{(error as Error).message}</div> : <Suspense fallback={<PageLoading />}><Outlet context={biz} /></Suspense>}
    </Shell>
  );
}

const PLATFORM_NAV: Record<"moderation" | "inspection" | "administration", NavItem[]> = {
  moderation: [
    { to: "/moderation", label: "যাচাইকরণ", icon: ShieldCheck, end: true },
    { to: "/moderation/reports", label: "রিপোর্ট", icon: Flag },
    { to: "/moderation/history", label: "সিদ্ধান্তের ইতিহাস", icon: ScrollText },
    { to: "/moderation/analytics", label: "পরিসংখ্যান", icon: BarChart3 },
  ],
  inspection: [
    { to: "/inspections", label: "পরিদর্শন", icon: ClipboardCheck, end: true },
    { to: "/inspections/analytics", label: "পরিসংখ্যান", icon: BarChart3 },
  ],
  administration: [
    { to: "/admin", label: "বিশ্লেষণ ড্যাশবোর্ড", icon: LayoutDashboard, end: true },
    { to: "/admin/users", label: "ব্যবহারকারী", icon: Users },
    { to: "/admin/roles", label: "ভূমিকা ও অনুমতি", icon: ShieldCheck },
    { to: "/admin/billing", label: "সাবস্ক্রিপশন ও পেমেন্ট", icon: CreditCard },
    { to: "/admin/audit", label: "অডিট লগ", icon: ScrollText },
  ],
};

const PLATFORM_TITLE = { moderation: "মডারেশন", inspection: "পরিদর্শন", administration: "এ্যাডমিনিস্ট্রেশন" } as const;

export function PlatformLayout({ type }: { type: "moderation" | "inspection" | "administration" }) {
  const { workspaces, status } = useAuth();
  const allowed = workspaces.some((w) => w.type === type);
  const header = <div className="mt-1 mb-3 rounded-lg border border-line bg-bg px-3 py-2.5 font-display text-[15px] font-semibold">{PLATFORM_TITLE[type]}</div>;
  return (
    <Shell
      sidebar={<SideNav items={PLATFORM_NAV[type]} header={header} />}
      topbar={<div className="ml-auto flex gap-2"><WorkspaceSwitcher current={{ type: type as WorkspaceType }} /></div>}
    >
      {status === "authenticated" && workspaces.length > 0 && !allowed
        ? <div className="card p-8 text-center text-muted">এই অংশে প্রবেশের অনুমতি আপনার নেই।</div>
        : <Suspense fallback={<PageLoading />}><Outlet /></Suspense>}
    </Shell>
  );
}

