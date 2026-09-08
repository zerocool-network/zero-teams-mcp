"use client";

import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from "react";

interface AuthUser {
  userId: string;
  email: string;
}

interface AuthContextType {
  user: AuthUser | null;
  loading: boolean;
  login: () => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType>({
  user: null,
  loading: true,
  login: async () => {},
  logout: () => {},
});

const CONTROLPLANE_URL = process.env.NEXT_PUBLIC_CONTROLPLANE_URL ?? "http://localhost:5100";

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const stored = localStorage.getItem("zt-auth-user");
    if (stored) {
      try {
        setUser(JSON.parse(stored));
      } catch {
        localStorage.removeItem("zt-auth-user");
      }
    }
    setLoading(false);
  }, []);

  useEffect(() => {
    const handler = (e: MessageEvent) => {
      if (e.data?.type === "auth-success" && e.data.userId) {
        const u = { userId: e.data.userId, email: e.data.email };
        setUser(u);
        localStorage.setItem("zt-auth-user", JSON.stringify(u));
      }
    };
    window.addEventListener("message", handler);
    return () => window.removeEventListener("message", handler);
  }, []);

  const login = useCallback(async () => {
    const res = await fetch(`${CONTROLPLANE_URL}/auth/login`);
    const { url } = await res.json();
    window.open(url, "auth-popup", "width=600,height=700");
  }, []);

  const logout = useCallback(() => {
    setUser(null);
    localStorage.removeItem("zt-auth-user");
  }, []);

  return (
    <AuthContext.Provider value={{ user, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  return useContext(AuthContext);
}
