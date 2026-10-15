import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  // Permite servir el panel bajo un prefijo (p.ej. /panel) sin romper el uso raíz en Docker.
  basePath: process.env.NEXT_PUBLIC_BASE_PATH || undefined,
};

export default nextConfig;
