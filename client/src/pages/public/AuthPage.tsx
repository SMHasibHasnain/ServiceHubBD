import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { Eye, EyeOff } from "lucide-react";
import { ApiError } from "../../api/client";
import { useAuth } from "../../auth/AuthContext";
import { Logo } from "../../components/Layouts";
import { Band, LogoFull, PetalPattern, Rosette } from "../../components/brand";
import { Button, ErrorBox, Field } from "../../components/ui";

function PasswordInput({ id, value, onChange, autoComplete, placeholder }: {
  id: string; value: string; onChange: (v: string) => void; autoComplete: string; placeholder: string;
}) {
  const [show, setShow] = useState(false);
  return (
    <div className="relative">
      <input id={id} className="input pr-12" type={show ? "text" : "password"} value={value} onChange={(e) => onChange(e.target.value)}
        autoComplete={autoComplete} placeholder={placeholder} required />
      <button type="button" onClick={() => setShow((s) => !s)} className="absolute top-1/2 right-1 flex size-11 -translate-y-1/2 items-center justify-center rounded-md text-subtle hover:text-ink"
        aria-label={show ? "পাসওয়ার্ড লুকান" : "পাসওয়ার্ড দেখুন"}>
        {show ? <EyeOff className="size-5" aria-hidden /> : <Eye className="size-5" aria-hidden />}
      </button>
    </div>
  );
}

function strength(p: string) {
  let s = 0;
  if (p.length >= 8) s++;
  if (/[A-Z]/.test(p) && /[a-z]/.test(p)) s++;
  if (/\d/.test(p)) s++;
  if (/[^A-Za-z0-9]/.test(p)) s++;
  return s;
}

export default function AuthPage({ mode }: { mode: "login" | "register" }) {
  const { status, login, register } = useAuth();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const next = params.get("next") || "/me/bookings";

  const [form, setForm] = useState({ login: "", fullName: "", phone: "", email: "", password: "", remember: true, terms: false });
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const set = (k: keyof typeof form) => (v: string | boolean) => setForm((f) => ({ ...f, [k]: v }));
  const fieldError = (name: string) =>
    error instanceof ApiError
      ? (error.fieldErrors[name] ?? error.fieldErrors[name[0].toLowerCase() + name.slice(1)])?.[0]
      : undefined;

  if (status === "authenticated") return <Navigate to={next} replace />;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (mode === "register" && !form.terms) {
      setError(new Error("শর্তাবলি ও গোপনীয়তা নীতিতে সম্মতি দিন।"));
      return;
    }
    setBusy(true);
    try {
      if (mode === "login") await login(form.login, form.password, form.remember);
      else await register({ fullName: form.fullName, phone: form.phone, email: form.email || undefined, password: form.password });
      navigate(next, { replace: true });
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const score = strength(form.password);

  return (
    <div className="flex min-h-dvh flex-wrap">
      <aside className="relative hidden flex-[1_1_440px] flex-col justify-between gap-10 overflow-hidden bg-primary p-12 text-cream lg:flex">
        <PetalPattern />
        <div className="relative self-start rounded-full bg-cream px-5 py-2.5"><Logo /></div>
        <div className="relative flex max-w-lg flex-col gap-6">
          <h1 className="text-5xl leading-[1.1] font-semibold">
            একটি অ্যাকাউন্ট,<br /><span className="painted text-[1.15em] text-marigold">অনেক দরজা</span>
          </h1>
          <p className="text-lg text-[#cfd8ea]">গ্রাহক হিসেবে যোগ দিন। পরে চাইলে একই অ্যাকাউন্ট থেকে এক বা একাধিক ব্যবসা পেজ খুলুন, অথবা অন্য ব্যবসায় টিম সদস্য হিসেবে কাজ করুন।</p>
          <ul className="flex flex-col gap-3">
            {["যাচাইকৃত ব্যবসা থেকে সেবা নিন", "লগআউট ছাড়াই ওয়ার্কস্পেস বদলান", "দুই মিনিটে রেজিস্ট্রেশন"].map((t) => (
              <li key={t} className="flex items-center gap-3"><Rosette color="#e9b24a" className="size-5 shrink-0" />{t}</li>
            ))}
          </ul>
        </div>
        <Band className="relative -mx-12 -mb-12 w-[calc(100%+6rem)]" />
      </aside>

      <main className="flex bg-cream flex-[999_1_480px] items-center justify-center px-4 py-10">
        <div className="w-full max-w-md">
          <div className="mb-8 flex justify-center lg:hidden"><LogoFull className="h-24 w-auto" /></div>
          <form onSubmit={submit} className="flex flex-col gap-5 rounded-xl border border-line bg-surface p-6 shadow-card sm:p-8" noValidate>
            <div>
              <h2 className="text-[28px] font-semibold text-primary">{mode === "login" ? "লগইন" : "অ্যাকাউন্ট তৈরি করুন"}</h2>
              <p className="mt-1 text-muted">{mode === "login" ? "আপনার অ্যাকাউন্টে লগইন করুন" : "শুধু প্রয়োজনীয় তথ্য — ব্যবসা পেজ পরে তৈরি করতে পারবেন"}</p>
            </div>
            <ErrorBox error={error instanceof ApiError && error.code === "validation" ? null : error} />

            {mode === "login" ? (
              <>
                <Field label="মোবাইল নম্বর বা ইমেইল">{(id) => <input id={id} className="input" value={form.login} onChange={(e) => set("login")(e.target.value)} autoComplete="username" placeholder="যেমন: ০১৭১১-২৩৪৫৬৭" required />}</Field>
                <Field label="পাসওয়ার্ড">{(id) => <PasswordInput id={id} value={form.password} onChange={set("password")} autoComplete="current-password" placeholder="আপনার পাসওয়ার্ড" />}</Field>
                <label className="flex min-h-11 items-center gap-2.5">
                  <input type="checkbox" className="size-5 accent-primary" checked={form.remember} onChange={(e) => set("remember")(e.target.checked)} />
                  আমাকে মনে রাখুন
                </label>
              </>
            ) : (
              <>
                <Field label="পূর্ণ নাম" error={fieldError("FullName")}>{(id) => <input id={id} className="input" value={form.fullName} onChange={(e) => set("fullName")(e.target.value)} autoComplete="name" placeholder="যেমন: হাসিব হাসনাইন" required />}</Field>
                <Field label="মোবাইল নম্বর" error={fieldError("Phone")}>{(id) => <input id={id} className="input" type="tel" inputMode="tel" value={form.phone} onChange={(e) => set("phone")(e.target.value)} autoComplete="tel" placeholder="০১XXXXXXXXX" required />}</Field>
                <Field label="ইমেইল" optional error={fieldError("Email")}>{(id) => <input id={id} className="input" type="email" value={form.email} onChange={(e) => set("email")(e.target.value)} autoComplete="email" placeholder="name@example.com" />}</Field>
                <Field label="পাসওয়ার্ড" error={fieldError("Password")} hint="কমপক্ষে ৮ অক্ষর">{(id) => <PasswordInput id={id} value={form.password} onChange={set("password")} autoComplete="new-password" placeholder="কমপক্ষে ৮ অক্ষর" />}</Field>
                <div className="flex gap-1" aria-hidden>{[0, 1, 2, 3].map((i) => <span key={i} className={`h-1 flex-1 rounded ${i < score ? (score >= 3 ? "bg-success" : "bg-warning") : "bg-line"}`} />)}</div>
                <label className="flex items-start gap-2.5 text-[15px] text-zinc-700">
                  <input type="checkbox" className="mt-0.5 size-5 accent-primary" checked={form.terms} onChange={(e) => set("terms")(e.target.checked)} />
                  আমি শর্তাবলি ও গোপনীয়তা নীতিতে সম্মত
                </label>
              </>
            )}

            <Button type="submit" size="lg" loading={busy} className="w-full">{mode === "login" ? "লগইন করুন" : "অ্যাকাউন্ট তৈরি করুন"}</Button>
            <p className="text-center text-[15px] text-muted">
              {mode === "login"
                ? <>অ্যাকাউন্ট নেই? <Link to={`/register?next=${encodeURIComponent(next)}`} className="font-semibold text-primary hover:text-primary-hover">রেজিস্ট্রেশন করুন</Link></>
                : <>আগেই অ্যাকাউন্ট আছে? <Link to={`/login?next=${encodeURIComponent(next)}`} className="font-semibold text-primary hover:text-primary-hover">লগইন করুন</Link></>}
            </p>
          </form>
        </div>
      </main>
    </div>
  );
}
