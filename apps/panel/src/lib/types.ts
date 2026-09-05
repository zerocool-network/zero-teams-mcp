export interface TenantConfig {
  tenantId: string;
  clientId: string;
  clientSecret: string;
  displayName: string;
}

export interface ConsentRecord {
  tenantId: string;
  status: "Pending" | "Granted" | "Revoked" | "Failed";
  consentUrl?: string;
  grantedAt?: string;
  failedAt?: string;
  failureReason?: string;
  requestedAt: string;
}

export interface HealthResponse {
  status: string;
  service: string;
  timestamp: string;
  tenantCount: number;
  consentCount: number;
  mcpServerUrl?: string;
  panelUrl?: string;
}
