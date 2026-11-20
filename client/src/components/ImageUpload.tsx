import { useRef, useState } from "react";
import { ImagePlus, Trash2 } from "lucide-react";
import { api, ApiError } from "../api/client";
import { MAX_IMAGE_BYTES } from "../lib/page";
import { Button, useToast } from "./ui";

const ACCEPT = "image/jpeg,image/png,image/webp";

/** Uploads one image for the business and returns its URL. Checks type and size before sending. */
export const useImageUpload = (businessId: string) => useUpload(`/api/businesses/${businessId}/uploads`);

/** Uploads one image to `path` (any upload endpoint) and returns its URL, or null after a toast. */
export function useUpload(path: string) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const upload = async (file: File): Promise<string | null> => {
    if (!ACCEPT.split(",").includes(file.type)) { toast("শুধু JPG, PNG বা WebP ছবি আপলোড করা যায়।", "error"); return null; }
    if (file.size > MAX_IMAGE_BYTES) { toast("ছবির আকার সর্বোচ্চ ৫ এমবি হতে পারে।", "error"); return null; }
    setBusy(true);
    try {
      return (await api.upload(path, file)).url;
    } catch (e) {
      toast(e instanceof ApiError ? e.message : "ছবি আপলোড করা যায়নি।", "error");
      return null;
    } finally {
      setBusy(false);
    }
  };
  return { upload, busy };
}

/** Hidden file input + trigger. Use for "add photos" buttons; supports picking several files. */
export function FilePicker({ onFiles, multiple, children, disabled, className }: {
  onFiles: (files: File[]) => void; multiple?: boolean; children: React.ReactNode; disabled?: boolean; className?: string;
}) {
  const ref = useRef<HTMLInputElement>(null);
  return (
    <>
      <input ref={ref} type="file" accept={ACCEPT} multiple={multiple} className="sr-only" tabIndex={-1} aria-hidden
        onChange={(e) => { onFiles(Array.from(e.target.files ?? [])); e.target.value = ""; }} />
      <Button type="button" variant="outline" disabled={disabled} className={className} onClick={() => ref.current?.click()}>{children}</Button>
    </>
  );
}

/** One image field (logo, cover, service photo): preview, replace and remove. */
export function ImageField({ businessId, label, value, onChange, shape = "square", hint }: {
  businessId: string; label: string; value: string | null; onChange: (url: string | null) => void;
  shape?: "square" | "wide"; hint?: string;
}) {
  const { upload, busy } = useImageUpload(businessId);
  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-sm font-medium">{label}</span>
      <div className="flex flex-wrap items-center gap-3">
        <div className={`flex shrink-0 items-center justify-center overflow-hidden rounded-md border border-dashed border-zinc-300 bg-zinc-50 text-subtle ${shape === "wide" ? "h-20 w-36" : "size-20"}`}>
          {value ? <img src={value} alt="" className="size-full object-cover" /> : <ImagePlus className="size-6" aria-hidden />}
        </div>
        <div className="flex flex-wrap gap-2">
          <FilePicker disabled={busy} onFiles={async ([f]) => { if (!f) return; const url = await upload(f); if (url) onChange(url); }}>
            {busy ? "আপলোড হচ্ছে..." : value ? "ছবি বদলান" : "ছবি আপলোড করুন"}
          </FilePicker>
          {value && <Button type="button" variant="ghost" onClick={() => onChange(null)} aria-label={`${label} সরান`}><Trash2 className="size-4" aria-hidden /></Button>}
        </div>
      </div>
      {hint && <p className="text-sm text-subtle">{hint}</p>}
    </div>
  );
}
