import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { Navigate, useLocation } from "react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, refreshSession, session } from "../api/client";
import type { AuthResponse, User, Workspace } from "../api/types";

type Status = "loading" | "anonymous" | "authenticated";

interface AuthValue {
  status: Status;
  user: User | null;
  workspaces: Workspace[];
  login(login: string, password: string, rememberMe: boolean): Promise<User>;
  register(input: { fullName: string; phone: string; email?: string; password: string }): Promise<User>;
  logout(): Promise<void>;
  setUser(user: User): void;
  hasPlatformRole(type: Workspace["type"]): boolean;
}

const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>("loading");
  const [user, setUser] = useState<User | null>(null);
  const qc = useQueryClient();

  const signedOut = useCallback(() => {
    session.clear();
    setUser(null);
    setStatus("anonymous");
    qc.clear();
  }, [qc]);

  // Restore the session from the stored refresh token on first load.
  useEffect(() => {
    session.onExpired(signedOut);
    let cancelled = false;
    (async () => {
      if (await refreshSession()) {
        try {
          const me = await api.get<User>("/api/me");
          if (!cancelled) { setUser(me); setStatus("authenticated"); }
          return;
        } catch { /* fall through */ }
      }
      if (!cancelled) setStatus("anonymous");
    })();
    return () => { cancelled = true; };
  }, [signedOut]);

  const workspacesQuery = useQuery({
    queryKey: ["workspaces"],
    queryFn: () => api.get<Workspace[]>("/api/me/workspaces"),
    enabled: status === "authenticated",
    refetchInterval: 60_000,
  });

  const accept = useCallback((res: AuthResponse) => {
    session.set(res.tokens);
    qc.clear();
    setUser(res.user);
    setStatus("authenticated");
    return res.user;
  }, [qc]);

  const value = useMemo<AuthValue>(() => ({
    status,
    user,
    workspaces: workspacesQuery.data ?? [],
    login: async (login, password, rememberMe) =>
      accept(await api.post<AuthResponse>("/api/auth/login", { login, password, rememberMe })),
    register: async (input) => accept(await api.post<AuthResponse>("/api/auth/register", input)),
    logout: async () => {
      const token = session.refreshToken();
      if (token) await api.post("/api/auth/logout", { refreshToken: token }).catch(() => {});
      signedOut();
    },
    setUser,
    hasPlatformRole: (type) => (workspacesQuery.data ?? []).some((w) => w.type === type),
  }), [status, user, workspacesQuery.data, accept, signedOut]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}

/** Redirects anonymous visitors to the login page, returning them here afterwards. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  const location = useLocation();
  if (status === "loading") return <div className="p-10 text-center text-muted" role="status">তথ্য লোড হচ্ছে...</div>;
  if (status === "anonymous") return <Navigate to={`/login?next=${encodeURIComponent(location.pathname + location.search)}`} replace />;
  return <>{children}</>;
}
