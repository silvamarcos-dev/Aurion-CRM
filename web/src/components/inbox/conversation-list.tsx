"use client";

import { useQuery } from "@tanstack/react-query";
import { Clock4, MessageCircle, Search } from "lucide-react";
import { useDeferredValue, useState } from "react";
import { Avatar } from "@/components/ui/avatar";
import { api } from "@/lib/api";
import { zonedDate, zonedTime, todayInZone } from "@/lib/time";
import type { ConversationSummary } from "@/lib/types";

export type InboxFilter = "Mine" | "Unassigned" | "All";

const filters: { key: InboxFilter; label: string }[] = [
  { key: "Mine", label: "Minhas" },
  { key: "Unassigned", label: "Sem dono" },
  { key: "All", label: "Todas" },
];

function shortTime(iso: string | null) {
  if (!iso) return "";
  const day = zonedDate(iso);
  if (day === todayInZone()) return zonedTime(iso);
  const [, m, d] = day.split("-");
  return `${d}/${m}`;
}

export function ConversationList({ selectedId, onSelect, filter, onFilterChange }: {
  selectedId: string | null;
  onSelect: (id: string) => void;
  filter: InboxFilter;
  onFilterChange: (f: InboxFilter) => void;
}) {
  const [search, setSearch] = useState("");
  const term = useDeferredValue(search.trim());
  const params = new URLSearchParams({ filter, limit: "100" });
  if (term) params.set("search", term);

  const { data, isLoading } = useQuery({
    queryKey: ["conversations", "list", params.toString()],
    queryFn: () => api.get<ConversationSummary[]>(`conversations?${params}`),
    refetchInterval: 5_000,
  });

  return (
    <div className="flex h-full flex-col">
      <div className="space-y-3 border-b border-slate-200/80 p-3">
        <div className="flex rounded-lg bg-slate-100 p-0.5" role="tablist">
          {filters.map((f) => (
            <button key={f.key} role="tab" aria-selected={filter === f.key} onClick={() => onFilterChange(f.key)}
              className={`flex-1 rounded-md px-2 py-1.5 text-xs font-medium transition ${filter === f.key ? "bg-white text-slate-900 shadow-card" : "text-slate-500 hover:text-slate-800"}`}>
              {f.label}
            </button>
          ))}
        </div>
        <label className="relative block">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
          <input className="input py-1.5 pl-8" placeholder="Buscar conversa…" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Buscar conversa" />
        </label>
      </div>

      <ul className="flex-1 overflow-y-auto">
        {isLoading && Array.from({ length: 6 }).map((_, i) => (
          <li key={i} className="flex gap-3 border-b border-slate-100 p-3"><div className="size-9 animate-pulse rounded-full bg-slate-100" /><div className="h-9 flex-1 animate-pulse rounded bg-slate-100" /></li>
        ))}
        {data?.map((c) => {
          const active = c.id === selectedId;
          const expired = !c.windowEndsAt || new Date(c.windowEndsAt) < new Date();
          return (
            <li key={c.id}>
              <button onClick={() => onSelect(c.id)}
                className={`flex w-full gap-3 border-b border-slate-100 px-3 py-3 text-left transition ${active ? "bg-brand-50/70" : "hover:bg-slate-50"}`}>
                <Avatar name={c.contactName} size="md" />
                <span className="min-w-0 flex-1">
                  <span className="flex items-center gap-2">
                    <span className={`flex-1 truncate text-sm ${c.unreadCount > 0 ? "font-semibold text-slate-900" : "font-medium text-slate-800"}`}>{c.contactName}</span>
                    <span className={`shrink-0 text-[11px] ${c.unreadCount > 0 ? "font-semibold text-brand-700" : "text-slate-400"}`}>{shortTime(c.lastMessageAt)}</span>
                  </span>
                  <span className="mt-0.5 flex items-center gap-2">
                    <span className={`flex-1 truncate text-xs ${c.unreadCount > 0 ? "text-slate-700" : "text-slate-500"}`}>
                      {c.lastMessageDirection === "Outbound" && <span className="text-slate-400">Você: </span>}
                      {c.lastMessagePreview ?? "—"}
                    </span>
                    {c.unreadCount > 0 && <span className="min-w-5 rounded-full bg-brand-600 px-1.5 text-center text-[10px] font-semibold leading-5 text-white">{c.unreadCount}</span>}
                  </span>
                  <span className="mt-1 flex items-center gap-2 text-[10px] text-slate-400">
                    {c.ownerName ? <span className="truncate">{c.ownerName}</span> : <span className="font-medium text-amber-600">Sem dono</span>}
                    {expired && <span className="flex items-center gap-0.5" title="Janela de 24h encerrada: só template"><Clock4 className="size-3" />24h</span>}
                    {c.state === "Closed" && <span>· encerrada</span>}
                  </span>
                </span>
              </button>
            </li>
          );
        })}
        {data?.length === 0 && (
          <li className="flex flex-col items-center gap-2 px-6 py-12 text-center text-sm text-slate-500">
            <MessageCircle className="size-6 text-slate-300" />
            {filter === "Mine" ? "Nenhuma conversa sua por aqui." : filter === "Unassigned" ? "Nenhuma conversa sem dono." : "Nenhuma conversa encontrada."}
          </li>
        )}
      </ul>
    </div>
  );
}
