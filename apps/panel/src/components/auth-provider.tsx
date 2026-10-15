"use client";

import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from "react";

interface AuthUser {
  userId: string;
  email: string;
}

interface AuthContextType {
  user: AuthUser | null;
  isAdmin: boolean;
  loading: boolean;
  login: () => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType>({
  user: null,
  isAdmin: false,
  loading: true,
  login: async () => {},
  logout: async () => {},
});

const CONTROLPLANE_URL = process.env.NEXT_PUBLIC_CONTROLPLANE_URL ?? "http://localhost:5100";

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [isAdmin, setIsAdmin] = useState(false);
  const [loading, setLoading] = useState(true);

  // La sesión vive en una cookie HttpOnly firmada por el ControlPlane: aquí
  // solo se consulta la identidad, nunca se inventa desde localStorage.
  const refresh = useCallback(async () => {
    try {
      const res = await fetch(`${CONTROLPLANE_URL}/api/session`, {
        credentials: "same-origin",
        cache: "no-store",
      });
      if (!res.ok) {
        setUser(null);
        setIsAdmin(false);
        return;
      }
      const s = await res.json();
      setUser({ userId: s.userId, email: s.email });
      setIsAdmin(Boolean(s.isAdmin));
    } catch {
      setUser(null);
      setIsAdmin(false);
    }
  }, []);

  useEffect(() => {
    refresh().finally(() => setLoading(false));
  }, [refresh]);

  useEffect(() => {
    const handler = (e: MessageEvent) => {
      if (e.data?.type === "auth-success") {
        // El callback del ControlPlane ya dejó la cookie de sesión.
        void refresh();
      }
    };
    window.addEventListener("message", handler);
    return () => window.removeEventListener("message", handler);
  }, [refresh]);

  const login = useCallback(async () => {
    const res = await fetch(`${CONTROLPLANE_URL}/auth/login`);
    const { url } = await res.json();
    window.open(url, "auth-popup", "width=600,height=700");
  }, []);

  const logout = useCallback(async () => {
    try {
      await fetch(`${CONTROLPLANE_URL}/api/session/logout`, {
        method: "POST",
        credentials: "same-origin",
      });
    } catch {
      // sin red: limpiar igualmente el estado local
    }
    setUser(null);
    setIsAdmin(false);
    window.location.href = `${process.env.NEXT_PUBLIC_BASE_PATH ?? ""}/login`;
  }, []);

  return (
    <AuthContext.Provider value={{ user, isAdmin, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  return useContext(AuthContext);
}
