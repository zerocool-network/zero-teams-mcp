import { Playground } from "@/components/playground";
import { Sidebar } from "@/components/sidebar";

export default function PlaygroundPage() {
  return (
    <div className="flex">
      <Sidebar />
      <main className="flex-1 ml-64 p-8">
        <div className="mb-8">
          <h1 className="font-display text-2xl font-bold">Playground</h1>
          <p className="text-sm text-muted-foreground mt-1">Test MCP tools interactively against the server</p>
        </div>
        <Playground />
      </main>
    </div>
  );
}
