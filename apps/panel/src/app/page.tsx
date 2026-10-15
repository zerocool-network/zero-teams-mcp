import { redirect } from "next/navigation";
import { Sidebar } from "@/components/sidebar";
import { Dashboard } from "@/components/dashboard";
import { AddTenantDialog } from "@/components/add-tenant-dialog";
import { getServerSession } from "@/lib/session-server";

// El root del basePath no pasa por el middleware de Next: se guarda aquí,
// en servidor, y de paso evita el "flash" de panel sin sesión.
export const dynamic = "force-dynamic";

export default async function Home() {
  const session = await getServerSession();
  if (!session) redirect("/login");
  if (!session.isAdmin) redirect("/connection");

  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <main className="flex-1 ml-64 p-8">
        <div className="flex items-center justify-between mb-8">
          <div />
          <AddTenantDialog />
        </div>
        <Dashboard />
      </main>
    </div>
  );
}
