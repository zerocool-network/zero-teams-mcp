import { NextResponse, type NextRequest } from "next/server";

// El panel ya no es público: sin sesión válida (cookie firmada por el
// ControlPlane) toda ruta redirige a /login.
const CONTROLPLANE_URL =
  process.env.CONTROLPLANE_URL ??
  process.env.NEXT_PUBLIC_CONTROLPLANE_URL ??
  "http://127.0.0.1:5100";

const PUBLIC_PATHS = ["/login"];

export async function middleware(req: NextRequest) {
  const { pathname } = req.nextUrl;

  if (PUBLIC_PATHS.some((p) => pathname === p || pathname.startsWith(`${p}/`))) {
    return NextResponse.next();
  }

  let ok = false;
  try {
    const res = await fetch(`${CONTROLPLANE_URL}/api/session`, {
      headers: { cookie: req.headers.get("cookie") ?? "" },
      cache: "no-store",
    });
    ok = res.ok;
  } catch {
    ok = false;
  }

  if (!ok) {
    // Host público real (detrás de nginx/Cloudflare) para no filtrar
    // localhost:3000 en el Location.
    const host =
      req.headers.get("x-forwarded-host") ?? req.headers.get("host") ?? req.nextUrl.host;
    const proto = req.headers.get("x-forwarded-proto") ?? req.nextUrl.protocol.replace(":", "");
    const basePath = process.env.NEXT_PUBLIC_BASE_PATH ?? "";
    return NextResponse.redirect(new URL(`${basePath}/login`, `${proto}://${host}`));
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico|.*\\.(?:png|jpg|jpeg|svg|ico|webp|txt|xml|map)$).*)"],
};
