import { createContext, useCallback, useContext, useEffect, useId, useRef, useState, type ButtonHTMLAttributes, type ReactNode } from "react";
import { Link, useParams, type LinkProps } from "react-router";
import { ApiError } from "../api/client";
import { AlertCircle, CheckCircle2, Loader2, Lock, X } from "lucide-react";
import type { Tone } from "../lib/format";
import { EmptyArt } from "./brand";

// ---------- Button ----------
type Variant = "primary" | "secondary" | "outline" | "ghost" | "danger";
type Size = "md" | "sm" | "lg";

const VARIANT: Record<Variant, string> = {
  primary: "bg-primary text-white shadow-[inset_0_1px_0_rgb(255_255_255/0.18),0_1px_2px_rgb(13_49_105/0.25)] hover:bg-primary-hover active:translate-y-px disabled:bg-zinc-200 disabled:text-zinc-500 disabled:shadow-none",
  secondary: "bg-secondary-light text-brand-green hover:bg-lime-100",
  outline: "border border-zinc-300 bg-surface text-ink shadow-soft hover:border-zinc-400 hover:bg-zinc-50",
  ghost: "text-zinc-600 hover:bg-zinc-100 hover:text-ink",
  danger: "border border-zinc-300 bg-surface text-red-700 shadow-soft hover:border-red-300 hover:bg-red-50",
};
const SIZE: Record<Size, string> = {
  sm: "min-h-10 px-3.5 text-sm",
  md: "min-h-11 px-5 text-[15px]",
  lg: "min-h-12 px-6 text-base",
};

export const buttonClass = (variant: Variant = "primary", size: Size = "md", extra = "") =>
  `inline-flex items-center justify-center gap-2 rounded-md font-semibold transition disabled:cursor-not-allowed ${VARIANT[variant]} ${SIZE[size]} ${extra}`;

export function Button({
  variant = "primary", size = "md", loading, className = "", children, disabled, ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; size?: Size; loading?: boolean }) {
  return (
    <button className={buttonClass(variant, size, className)} disabled={disabled || loading} aria-busy={loading || undefined} {...rest}>
      {loading && <Loader2 className="size-4 animate-spin" aria-hidden />}
      {children}
    </button>
  );
}

export function LinkButton({ variant = "primary", size = "md", className = "", ...rest }: LinkProps & { variant?: Variant; size?: Size }) {
  return <Link className={buttonClass(variant, size, className)} {...rest} />;
}

// ---------- Form fields ----------
export function Field({ label, error, hint, optional, children }: {
  label: string; error?: string; hint?: string; optional?: boolean; children: (id: string) => ReactNode;
}) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {label} {optional && <span className="text-sm font-normal text-subtle">(ঐচ্ছিক)</span>}
      </label>
      {children(id)}
      {hint && !error && <p className="text-sm text-subtle">{hint}</p>}
      {error && <p className="text-sm text-red-700" role="alert">{error}</p>}
    </div>
  );
}

export function ErrorBox({ error }: { error: unknown }) {
  const { businessId } = useParams();
  if (!error) return null;
  const message = error instanceof Error ? error.message : "দুঃখিত, একটি সমস্যা হয়েছে।";
  // Plan limits read as an upgrade prompt rather than a failure.
  if (error instanceof ApiError && error.code === "plan_limit_reached") {
    return (
      <div role="alert" className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-md border border-amber-200 bg-amber-50 px-3.5 py-2.5 text-sm text-amber-900">
        <Lock className="size-4 shrink-0" aria-hidden />
        <span className="flex-1">{message}</span>
        {businessId && <Link to={`/w/${businessId}/billing`} className="font-semibold text-primary hover:text-primary-hover">প্ল্যান দেখুন →</Link>}
      </div>
    );
  }
  return (
    <div role="alert" className="flex items-start gap-2 rounded-md border border-red-200 bg-red-50 px-3.5 py-2.5 text-sm text-red-800">
      <AlertCircle className="mt-0.5 size-4 shrink-0" aria-hidden />
      <span>{message}</span>
    </div>
  );
}

// ---------- Badge ----------
const TONE: Record<Tone, string> = {
  neutral: "bg-zinc-100 text-zinc-700 ring-zinc-200",
  blue: "bg-blue-50 text-blue-700 ring-blue-200",
  amber: "bg-amber-50 text-amber-800 ring-amber-200",
  green: "bg-emerald-50 text-emerald-700 ring-emerald-200",
  red: "bg-red-50 text-red-700 ring-red-200",
  violet: "bg-violet-50 text-violet-700 ring-violet-200",
  teal: "bg-teal-50 text-teal-700 ring-teal-200",
};

export function Badge({ tone = "neutral", children, className = "" }: { tone?: Tone; children: ReactNode; className?: string }) {
  return <span className={`inline-flex items-center gap-1 whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${TONE[tone]} ${className}`}>{children}</span>;
}

// ---------- Layout helpers ----------
export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <h1 className="text-2xl font-semibold text-ink sm:text-[28px]">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-muted">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  );
}

export function Stat({ label, value, note, icon, tone = "blue" }: { label: string; value: ReactNode; note?: ReactNode; icon?: ReactNode; tone?: Tone }) {
  return (
    <div className="card flex flex-col gap-2 p-4">
      <div className="flex items-start justify-between gap-2 text-sm text-muted">
        <span>{label}</span>
        {icon && <span className={`flex size-8 shrink-0 items-center justify-center rounded-md ring-1 ring-inset [&>svg]:size-4 ${TONE[tone]}`}>{icon}</span>}
      </div>
      <span className="font-display text-[26px] leading-none font-semibold">{value}</span>
      {note && <span className="text-xs text-subtle">{note}</span>}
    </div>
  );
}

export function EmptyState({ title, description, action, icon }: { title: string; description?: string; action?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-2 rounded-lg border border-dashed border-zinc-300 bg-surface/60 px-6 py-10 text-center">
      {icon ? <span className="mb-1 flex size-14 items-center justify-center rounded-full bg-primary-light text-primary [&>svg]:size-6">{icon}</span> : <EmptyArt className="size-24" />}
      <h2 className="text-base font-semibold">{title}</h2>
      {description && <p className="max-w-md text-sm text-muted">{description}</p>}
      {action}
    </div>
  );
}

export function Skeleton({ className = "" }: { className?: string }) {
  return <div className={`animate-pulse rounded-md bg-zinc-100 ${className}`} aria-hidden />;
}

export function LoadingList({ rows = 3 }: { rows?: number }) {
  return (
    <div className="flex flex-col gap-3" aria-label="তথ্য লোড হচ্ছে..." role="status">
      {Array.from({ length: rows }, (_, i) => <Skeleton key={i} className="h-24 w-full" />)}
    </div>
  );
}

export function Avatar({ name, className = "", tone = "blue" }: { name: string; className?: string; tone?: Tone }) {
  const initials = [...name.trim()].slice(0, 2).join("");
  return <span aria-hidden className={`flex shrink-0 items-center justify-center rounded-md text-[0.8em] font-medium ring-1 ring-inset ${TONE[tone]} ${className}`}>{initials}</span>;
}

export function Stars({ value }: { value: number }) {
  return <span className="font-medium text-ink" aria-label={`রেটিং ${value}`}><span className="text-amber-500">★</span> {value.toLocaleString("bn-BD", { maximumFractionDigits: 1 })}</span>;
}

// ---------- Modal ----------
export function Modal({ open, onClose, title, children, footer, wide }: {
  open: boolean; onClose: () => void; title: string; children: ReactNode; footer?: ReactNode; wide?: boolean;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal();
    if (!open && d.open) d.close();
  }, [open]);
  return (
    <dialog
      ref={ref}
      onClose={onClose}
      onClick={(e) => e.target === ref.current && onClose()}
      className={`m-auto w-[calc(100%-24px)] ${wide ? "max-w-2xl" : "max-w-lg"} rounded-xl border-0 bg-surface p-0 text-ink shadow-pop backdrop:bg-zinc-950/30`}
    >
      {open && (
        <div className="flex max-h-[85vh] flex-col">
          <div className="flex items-center justify-between gap-4 border-b border-line px-5 py-3">
            <h2 className="font-semibold">{title}</h2>
            <button onClick={onClose} className="flex size-9 items-center justify-center rounded-md text-subtle hover:bg-zinc-100 hover:text-ink" aria-label="বন্ধ করুন">
              <X className="size-4" aria-hidden />
            </button>
          </div>
          <div className="overflow-y-auto px-5 py-5">{children}</div>
          {footer && <div className="flex flex-wrap justify-end gap-2 border-t border-line bg-zinc-50 px-5 py-3">{footer}</div>}
        </div>
      )}
    </dialog>
  );
}

// ---------- Toasts ----------
type Toast = { id: number; message: string; kind: "success" | "error" };
const ToastContext = createContext<(message: string, kind?: Toast["kind"]) => void>(() => {});

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const push = useCallback((message: string, kind: Toast["kind"] = "success") => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t, { id, message, kind }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4000);
  }, []);
  return (
    <ToastContext.Provider value={push}>
      {children}
      <div className="pointer-events-none fixed inset-x-0 bottom-4 z-50 flex flex-col items-center gap-2 px-4" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={`pointer-events-auto flex max-w-md items-center gap-2 rounded-md px-3.5 py-2.5 text-sm font-medium text-white shadow-pop ${t.kind === "success" ? "bg-zinc-900" : "bg-red-700"}`}>
            {t.kind === "success" ? <CheckCircle2 className="size-4 text-emerald-400" aria-hidden /> : <AlertCircle className="size-5" aria-hidden />}
            {t.message}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export const useToast = () => useContext(ToastContext);

// ---------- Tabs (segmented) ----------
export function Segmented<T extends string>({ value, onChange, options, label }: {
  value: T; onChange: (v: T) => void; options: { value: T; label: string }[]; label: string;
}) {
  return (
    <div role="tablist" aria-label={label} className="inline-flex max-w-full gap-0.5 overflow-x-auto rounded-[12px] bg-zinc-100 p-1">
      {options.map((o) => (
        <button
          key={o.value}
          role="tab"
          aria-selected={value === o.value}
          onClick={() => onChange(o.value)}
          className={`min-h-9 shrink-0 whitespace-nowrap rounded-[8px] px-3.5 text-sm font-medium ${value === o.value ? "bg-surface text-ink shadow-card" : "text-muted hover:text-ink"}`}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}
