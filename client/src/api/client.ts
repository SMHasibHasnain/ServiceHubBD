import type { TokenPair } from "./types";

/** Error returned by the API as RFC 9457 problem details. `message` is the user-facing Bangla text. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, code: string, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }
}

const REFRESH_KEY = "shb.refresh";

let accessToken: string | null = null;
let refreshing: Promise<boolean> | null = null;
let onSessionExpired: (() => void) | null = null;

export const session = {
  set(tokens: TokenPair) {
    accessToken = tokens.accessToken;
    try {
      localStorage.setItem(REFRESH_KEY, tokens.refreshToken);
    } catch {
      /* private mode: session lasts until reload */
    }
  },
  clear() {
    accessToken = null;
    try {
      localStorage.removeItem(REFRESH_KEY);
    } catch {
      /* ignore */
    }
  },
  refreshToken(): string | null {
    try {
      return localStorage.getItem(REFRESH_KEY);
    } catch {
      return null;
    }
  },
  hasAccess: () => accessToken !== null,
  onExpired(cb: () => void) {
    onSessionExpired = cb;
  },
};

/** Exchanges the stored refresh token for a new pair. Concurrent callers share one request. */
export function refreshSession(): Promise<boolean> {
  const token = session.refreshToken();
  if (!token) return Promise.resolve(false);
  refreshing ??= fetch("/api/auth/refresh", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken: token }),
  })
    .then(async (res) => {
      if (!res.ok) {
        session.clear();
        return false;
      }
      session.set((await res.json()) as TokenPair);
      return true;
    })
    .catch(() => false)
    .finally(() => {
      refreshing = null;
    });
  return refreshing;
}

type Query = Record<string, string | number | boolean | null | undefined>;

function buildUrl(path: string, query?: Query) {
  if (!query) return path;
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) {
    if (v !== undefined && v !== null && v !== "") params.set(k, String(v));
  }
  const qs = params.toString();
  return qs ? `${path}?${qs}` : path;
}

async function toError(res: Response): Promise<ApiError> {
  let body: { code?: string; detail?: string; title?: string; errors?: Record<string, string[]> } = {};
  try {
    body = await res.json();
  } catch {
    /* empty or non-JSON body */
  }
  if (body.errors) {
    const first = Object.values(body.errors)[0]?.[0];
    return new ApiError(res.status, "validation", first ?? "তথ্যগুলো আবার যাচাই করুন।", body.errors);
  }
  const fallback =
    res.status === 401 ? "অনুগ্রহ করে লগইন করুন।"
    : res.status === 403 ? "এই অংশে প্রবেশের অনুমতি আপনার নেই।"
    : res.status === 404 ? "তথ্যটি পাওয়া যায়নি।"
    : res.status === 429 ? "অনেক বেশি অনুরোধ। কিছুক্ষণ পর আবার চেষ্টা করুন।"
    : "দুঃখিত, একটি সমস্যা হয়েছে। আবার চেষ্টা করুন।";
  return new ApiError(res.status, body.code ?? body.title ?? "error", body.detail ?? fallback);
}

async function request<T>(method: string, path: string, body?: unknown, query?: Query, retry = true): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;

  let res: Response;
  try {
    res = await fetch(buildUrl(path, query), {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, "network", navigator.onLine ? "সার্ভারের সাথে সংযোগ করা যাচ্ছে না।" : "আপনি বর্তমানে অফলাইনে আছেন।");
  }

  if (res.status === 401 && retry && session.refreshToken() && !path.startsWith("/api/auth/")) {
    if (await refreshSession()) return request<T>(method, path, body, query, false);
    onSessionExpired?.();
  }
  if (!res.ok) throw await toError(res);
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  get: <T>(path: string, query?: Query) => request<T>("GET", path, undefined, query),
  post: <T = void>(path: string, body: unknown = {}) => request<T>("POST", path, body),
  put: <T = void>(path: string, body: unknown) => request<T>("PUT", path, body),
  patch: <T = void>(path: string, body: unknown) => request<T>("PATCH", path, body),
  del: <T = void>(path: string) => request<T>("DELETE", path),
};
