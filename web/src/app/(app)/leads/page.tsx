"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useDeferredValue, useState } from "react";
import { NewLeadDialog } from "@/components/new-lead-dialog";
import { useCan } from "@/components/providers";
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
  const stageName = (id: string) => stages.find((s) => s.id === id)?.name ?? "—";

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

  return (
    <div className="p-6">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-lg font-semibold">Leads</h1>
        {can("leads.create") && (
          <button className="btn-primary" onClick={() => setNewOpen(true)}>
            + Novo lead
          </button>
        )}
      </div>

      <div className="mb-4 flex flex-wrap gap-2">
        <input className="input max-w-xs" placeholder="Buscar por nome, telefone ou e-mail" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} aria-label="Buscar" />
        <select className="input w-auto" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} aria-label="Status">
          <option value="">Todos os status</option>
          {Object.entries(statusLabels).map(([value, label]) => (
            <option key={value} value={value}>{label}</option>
          ))}
        </select>
        <select className="input w-auto" value={stageId} onChange={(e) => { setStageId(e.target.value); setPage(1); }} aria-label="Etapa">
          <option value="">Todas as etapas</option>
          {stages.filter((s) => !s.isArchived).map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </select>
      </div>

      <div className="card overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th className="px-4 py-3 font-medium">Nome</th>
              <th className="px-4 py-3 font-medium">Telefone</th>
              <th className="px-4 py-3 font-medium">Etapa</th>
              <th className="px-4 py-3 font-medium">Responsável</th>
              <th className="px-4 py-3 font-medium">Origem</th>
              <th className="px-4 py-3 font-medium">Entrada</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {isLoading && (
              <tr><td colSpan={6} className="px-4 py-6 text-center text-slate-500">Carregando…</td></tr>
            )}
            {data?.items.map((lead) => (
              <tr key={lead.id} className="hover:bg-slate-50">
                <td className="px-4 py-3">
                  <Link href={`/leads/${lead.id}`} className="font-medium text-slate-900 hover:text-emerald-800">{lead.contactName}</Link>
                </td>
                <td className="px-4 py-3 text-slate-600">{formatPhone(lead.phone)}</td>
                <td className="px-4 py-3">{stageName(lead.stageId)}</td>
                <td className="px-4 py-3 text-slate-600">{lead.ownerName}</td>
                <td className="px-4 py-3 text-slate-600">{lead.source ?? "—"}</td>
                <td className="px-4 py-3 text-slate-600">{formatDateTime(lead.enteredAt)}</td>
              </tr>
            ))}
            {data && data.items.length === 0 && (
              <tr><td colSpan={6} className="px-4 py-6 text-center text-slate-500">Nenhum lead encontrado.</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="mt-3 flex items-center justify-between text-sm text-slate-600">
        <span>{data ? `${data.total} leads` : ""}</span>
        <div className="flex items-center gap-2">
          <button className="btn-secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Anterior</button>
          <span>{page} / {totalPages}</span>
          <button className="btn-secondary" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Próxima</button>
        </div>
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
