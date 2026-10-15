import { cookies } from "next/headers";

const CONTROLPLANE_URL =
  process.env.CONTROLPLANE_URL ??
  process.env.NEXT_PUBLIC_CONTROLPLANE_URL ??
  "http://127.0.0.1:5100";

export interface ServerSession {
  userId: string;
  email: string;
  isAdmin: boolean;
}

/**
 * Sesión validada contra el ControlPlane desde el servidor (cookie HttpOnly).
 * null = sin sesión válida.
 */
export async function getServerSession(): Promise<ServerSession | null> {
  try {
    const cookieHeader = (await cookies()).toString();
    const res = await fetch(`${CONTROLPLANE_URL}/api/session`, {
      headers: { cookie: cookieHeader },
      cache: "no-store",
    });
    if (!res.ok) return null;
    return (await res.json()) as ServerSession;
  } catch {
    return null;
  }
}
