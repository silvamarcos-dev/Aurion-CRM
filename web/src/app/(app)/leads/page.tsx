"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Plus, Search, Users } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useDeferredValue, useState } from "react";
import { NewLeadDialog } from "@/components/new-lead-dialog";
import { useCan } from "@/components/providers";
import { Avatar } from "@/components/ui/avatar";
import { EmptyState, PageHeader } from "@/components/ui/page-header";
import { StageBadge } from "@/components/ui/stage-badge";
import { api } from "@/lib/api";
import { formatDateTime, formatPhone, statusLabels } from "@/lib/format";
import type { LeadCard, Paged, Pipeline } from "@/lib/types";

function LeadsList() {
  const params = useSearchParams();
  const can = useCan();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const [stageId, setStageId] = useState(params.get("stageId") ?? "");
  const [page, setPage] = useState(1);
  const [newOpen, setNewOpen] = useState(false);
  const deferredSearch = useDeferredValue(search);

  const { data: pipelines } = useQuery({ queryKey: ["pipelines"], queryFn: () => api.get<Pipeline[]>("pipelines") });
  const stages = pipelines?.find((p) => p.isDefault)?.stages ?? [];
  const stageOf = (id: string) => stages.find((s) => s.id === id);

  const query = new URLSearchParams({ page: String(page), pageSize: "25" });
  if (deferredSearch.trim()) query.set("search", deferredSearch.trim());
  if (status) query.set("status", status);
  if (stageId) query.set("stageId", stageId);

  const { data, isLoading } = useQuery({
    queryKey: ["leads", query.toString()],
    queryFn: () => api.get<Paged<LeadCard>>(`leads?${query}`),
    placeholderData: keepPreviousData,
  });

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const filtered = Boolean(deferredSearch.trim() || status || stageId);

  return (
    <div>
      <PageHeader
        title="Leads"
        description={data ? `${data.total} ${data.total === 1 ? "lead" : "leads"}${filtered ? " com os filtros aplicados" : " visíveis para você"}` : "Carregando…"}
        actions={can("leads.create") && (
          <button className="btn-primary" onClick={() => setNewOpen(true)}>
            <Plus className="size-4" /> Novo lead
          </button>
        )}
      />

      <div className="space-y-4 px-4 py-5 sm:px-6 lg:px-8">
        <div className="flex flex-wrap gap-2">
          <label className="relative min-w-60 flex-1 sm:max-w-sm">
            <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
            <input className="input pl-9" placeholder="Buscar por nome, telefone ou e-mail" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} aria-label="Buscar" />
          </label>
          <select className="input w-auto" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} aria-label="Status">
            <option value="">Todos os status</option>
            {Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
          <select className="input w-auto" value={stageId} onChange={(e) => { setStageId(e.target.value); setPage(1); }} aria-label="Etapa">
            <option value="">Todas as etapas</option>
            {stages.filter((s) => !s.isArchived).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
          {filtered && (
            <button className="btn-ghost" onClick={() => { setSearch(""); setStatus(""); setStageId(""); setPage(1); }}>Limpar filtros</button>
          )}
        </div>

        {data && data.items.length === 0 ? (
          <EmptyState
            icon={<Users className="size-5" />}
            title={filtered ? "Nenhum lead encontrado" : "Nenhum lead ainda"}
            description={filtered ? "Tente ajustar a busca ou os filtros." : "Cadastre o primeiro lead para começar a acompanhar o funil."}
            action={!filtered && can("leads.create") ? <button className="btn-primary" onClick={() => setNewOpen(true)}><Plus className="size-4" /> Novo lead</button> : undefined}
          />
        ) : (
          <div className="card overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-200/80 bg-slate-50/80 text-[11px] uppercase tracking-wide text-slate-500">
                  <tr>
                    <th className="px-4 py-3 font-medium">Lead</th>
                    <th className="px-4 py-3 font-medium">Etapa</th>
                    <th className="px-4 py-3 font-medium">Responsável</th>
                    <th className="px-4 py-3 font-medium">Origem</th>
                    <th className="px-4 py-3 font-medium">Próximo follow-up</th>
                    <th className="px-4 py-3 font-medium">Entrada</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {isLoading && Array.from({ length: 6 }).map((_, i) => (
                    <tr key={i}><td colSpan={6} className="px-4 py-3"><div className="h-8 animate-pulse rounded bg-slate-100" /></td></tr>
                  ))}
                  {data?.items.map((lead) => {
                    const late = lead.nextFollowUpAt && new Date(lead.nextFollowUpAt) < new Date();
                    return (
                      <tr key={lead.id} className="group transition hover:bg-brand-50/40">
                        <td className="px-4 py-3">
                          <Link href={`/leads/${lead.id}`} className="flex items-center gap-3">
                            <Avatar name={lead.contactName} size="md" />
                            <span className="min-w-0">
                              <span className="block truncate font-medium text-slate-900 group-hover:text-brand-700">{lead.contactName}</span>
                              <span className="block text-xs text-slate-500">{formatPhone(lead.phone)}</span>
                            </span>
                          </Link>
                        </td>
                        <td className="px-4 py-3"><StageBadge stage={stageOf(lead.stageId)} /></td>
                        <td className="px-4 py-3">
                          <span className="flex items-center gap-2 text-slate-700"><Avatar name={lead.ownerName} size="xs" />{lead.ownerName}</span>
                        </td>
                        <td className="px-4 py-3 text-slate-600">{lead.source ?? "—"}</td>
                        <td className={`px-4 py-3 ${late ? "font-medium text-red-600" : "text-slate-600"}`}>{formatDateTime(lead.nextFollowUpAt)}</td>
                        <td className="px-4 py-3 text-slate-500">{formatDateTime(lead.enteredAt)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            {data && totalPages > 1 && (
              <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3 text-sm text-slate-600">
                <span>Página {page} de {totalPages}</span>
                <div className="flex gap-1">
                  <button className="btn-secondary px-2" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} aria-label="Página anterior"><ChevronLeft className="size-4" /></button>
                  <button className="btn-secondary px-2" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} aria-label="Próxima página"><ChevronRight className="size-4" /></button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      <NewLeadDialog open={newOpen} onClose={() => setNewOpen(false)} />
    </div>
  );
}

export default function LeadsPage() {
  return (
    <Suspense>
      <LeadsList />
    </Suspense>
  );
}
