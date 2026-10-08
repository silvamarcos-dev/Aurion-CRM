import type { ReactNode } from "react";
import { GuestBanner } from "@/components/guest-banner";
import { SuggestWidget } from "@/components/suggest-widget";
import { AppSidebar } from "@/components/ui/sidebar";

export default function AppLayout({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-dvh md:flex">
      <AppSidebar />
      <main className="min-w-0 flex-1">
        <GuestBanner />
        {children}
      </main>
      <SuggestWidget />
    </div>
  );
}
