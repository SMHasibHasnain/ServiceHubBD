import { NavLink } from "react-router";

export type SectionTab = { to: string; label: string; permission?: string };

/**
 * Sub-pages of one section (e.g. Settings → তথ্য / কাজের সময় / …), shown as tabs under the page title.
 * Each tab is a real URL, so the back button, reloads and shared links land on the right tab.
 * Looks the same as the `Segmented` control, which stays for in-page filters that aren't separate pages.
 */
export function SectionTabs({ items, label, permissions }: { items: SectionTab[]; label: string; permissions?: string[] }) {
  const visible = items.filter((t) => !t.permission || permissions?.includes(t.permission));
  if (visible.length < 2) return null;
  return (
    <nav aria-label={label} className="mb-6">
      <div className="inline-flex max-w-full gap-0.5 overflow-x-auto rounded-[12px] bg-zinc-100 p-1">
        {visible.map((t) => (
          <NavLink key={t.to} to={t.to}
            className={({ isActive }) => `flex min-h-9 shrink-0 items-center whitespace-nowrap rounded-[8px] px-3.5 text-sm font-medium transition-colors duration-150 ${isActive ? "bg-surface text-ink shadow-card" : "text-muted hover:text-ink"}`}>
            {t.label}
          </NavLink>
        ))}
      </div>
    </nav>
  );
}
