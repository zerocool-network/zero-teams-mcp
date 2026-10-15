import type { TenantConfig, ConsentRecord, HealthResponse } from "./types";

const API_BASE = process.env.NEXT_PUBLIC_CONTROLPLANE_URL ?? "http://localhost:5100";

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: "same-origin",
    headers: { "Content-Type": "application/json", ...options?.headers },
    ...options,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`API ${res.status}: ${text || res.statusText}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

export const api = {
  health: () => request<HealthResponse>("/health"),
  listTenants: () => request<TenantConfig[]>("/api/tenants"),
  getTenant: (id: string) => request<TenantConfig>(`/api/tenants/${id}`),
  createTenant: (data: Omit<TenantConfig, "tenantId">) =>
    request<TenantConfig>("/api/tenants", {
      method: "POST",
      body: JSON.stringify(data),
    }),
  deleteTenant: (id: string) =>
    request<void>(`/api/tenants/${id}`, { method: "DELETE" }),
  getConsent: (id: string) => request<ConsentRecord>(`/api/tenants/${id}/consent`),
  requestConsent: (id: string) =>
    request<{ tenantId: string; status: string; consentUrl: string }>(`/api/tenants/${id}/consent`, {
      method: "POST",
      body: JSON.stringify({ status: "Pending" }),
    }),
};
