"use client";

import { useRouter } from "next/navigation";
import { useAuth } from "@/components/auth-provider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { LogIn, LogOut, User } from "lucide-react";

export default function LoginPage() {
  const router = useRouter();
  const { user, loading, login, logout } = useAuth();

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-[60vh]">
        <div className="h-8 w-8 animate-spin rounded-full border-2 border-neon-cyan border-t-transparent" />
      </div>
    );
  }

  return (
    <div className="flex items-center justify-center min-h-[60vh]">
      <Card className="w-full max-w-md border-border/50 bg-card/50 backdrop-blur-xl">
        <CardHeader className="text-center">
          <CardTitle className="font-display text-xl">Microsoft Teams Auth</CardTitle>
          <CardDescription>
            {user
              ? "Sesión iniciada. Consulta tu API key e integración en «Mi conexión»."
              : "Accede al panel con tu cuenta de Microsoft (Teams)."}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col items-center gap-4">
          {user ? (
            <>
              <div className="flex items-center gap-3 rounded-lg border border-neon-cyan/20 bg-neon-cyan/5 px-4 py-3">
                <User className="h-5 w-5 text-neon-cyan" />
                <div>
                  <p className="text-sm font-medium">{user.email}</p>
                  <p className="text-[10px] text-muted-foreground font-mono">{user.userId}</p>
                </div>
              </div>
              <Button
                onClick={() => router.replace("/connection")}
                className="gap-2 bg-neon-cyan text-black hover:bg-neon-cyan/90"
              >
                Ir a mi conexión
              </Button>
              <Button
                variant="outline"
                onClick={logout}
                className="gap-2 text-muted-foreground"
              >
                <LogOut className="h-4 w-4" />
                Logout
              </Button>
            </>
          ) : (
            <Button
              onClick={login}
              className="gap-2 bg-neon-cyan text-black hover:bg-neon-cyan/90"
            >
              <LogIn className="h-4 w-4" />
              Login with Microsoft
            </Button>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
