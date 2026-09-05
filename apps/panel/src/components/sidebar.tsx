"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";
import { LayoutDashboard, Server, Shield, Activity, Settings } from "lucide-react";

const nav = [
  { href: "/", label: "Dashboard", icon: LayoutDashboard },
  { href: "/tenants", label: "Tenants", icon: Server },
  { href: "/consents", label: "Consents", icon: Shield },
  { href: "/health", label: "Health", icon: Activity },
];

export function Sidebar() {
  const pathname = usePathname();

  return (
    <aside className="fixed left-0 top-0 z-40 h-screen w-64 border-r border-border/50 bg-card/50 backdrop-blur-xl">
      <div className="flex h-16 items-center px-6 border-b border-border/50">
        <div className="flex items-center gap-3">
          <div className="h-8 w-8 rounded-lg bg-neon-cyan/20 flex items-center justify-center">
            <span className="font-display text-sm font-bold text-neon-cyan">ZT</span>
          </div>
          <div>
            <p className="font-display text-sm font-bold">ZeroTeams</p>
            <p className="text-[10px] text-muted-foreground font-mono">MCP Server</p>
          </div>
        </div>
      </div>
      <nav className="flex flex-col gap-1 p-4">
        {nav.map((item) => {
          const active = pathname === item.href;
          return (
            <Link
              key={item.href}
              href={item.href}
              className={cn(
                "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                active
                  ? "bg-neon-cyan/10 text-neon-cyan border border-neon-cyan/20"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground"
              )}
            >
              <item.icon className="h-4 w-4" />
              {item.label}
            </Link>
          );
        })}
      </nav>
      <div className="absolute bottom-4 left-4 right-4">
        <div className="rounded-lg border border-border/50 bg-muted/30 p-3">
          <p className="text-[10px] text-muted-foreground font-mono">v0.1.0 — F3</p>
          <p className="text-[10px] text-muted-foreground font-mono">.NET 10 + Next.js 15</p>
        </div>
      </div>
    </aside>
  );
}
