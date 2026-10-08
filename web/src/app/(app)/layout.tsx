"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { useMe } from "@/components/providers";
import { logout } from "@/lib/api";

const nav = [
  { href: "/kanban", label: "Kanban" },
  { href: "/leads", label: "Leads" },
];

const upcoming = ["Atendimento", "Agenda", "Call Manager", "Automações", "Copilot"];

export default function AppLayout({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const { data: me } = useMe();

  return (
    <div className="flex min-h-screen">
      <aside className="hidden w-56 shrink-0 flex-col border-r border-slate-200 bg-white md:flex">
        <div className="flex items-center gap-2 px-5 py-5">
          <span className="inline-block size-3 rounded-sm bg-emerald-700" aria-hidden />
          <span className="font-semibold">CRM Loteadora</span>
        </div>
        <nav className="flex flex-col gap-0.5 px-3">
          {nav.map((item) => {
            const active = pathname.startsWith(item.href);
            return (
              <Link
                key={item.href}
                href={item.href}
                className={`rounded-md px-3 py-2 text-sm ${active ? "bg-emerald-50 font-medium text-emerald-800" : "text-slate-700 hover:bg-slate-100"}`}
              >
                {item.label}
              </Link>
            );
          })}
          <p className="mt-5 px-3 text-[11px] font-medium uppercase tracking-wide text-slate-400">Próximas fases</p>
          {upcoming.map((label) => (
            <span key={label} className="cursor-default px-3 py-1.5 text-sm text-slate-400">
              {label}
            </span>
          ))}
        </nav>
        <div className="mt-auto border-t border-slate-200 p-4 text-sm">
          {me && (
            <>
              <p className="truncate font-medium">{me.displayName}</p>
              <p className="truncate text-xs text-slate-500">
                {me.role} · {me.organizationName}
              </p>
            </>
          )}
          <button onClick={logout} className="mt-3 text-xs text-slate-500 underline-offset-2 hover:underline">
            Sair
          </button>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center justify-between border-b border-slate-200 bg-white px-4 py-3 md:hidden">
          <span className="font-semibold">CRM Loteadora</span>
          <div className="flex gap-3 text-sm">
            {nav.map((item) => (
              <Link key={item.href} href={item.href} className={pathname.startsWith(item.href) ? "font-medium text-emerald-800" : "text-slate-600"}>
                {item.label}
              </Link>
            ))}
            <button onClick={logout} className="text-slate-500">Sair</button>
          </div>
        </header>
        <main className="min-w-0 flex-1">{children}</main>
      </div>
    </div>
  );
}
