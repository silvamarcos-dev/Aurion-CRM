"use client";

import { useQuery } from "@tanstack/react-query";
import { useDeferredValue, useState } from "react";
import { api } from "@/lib/api";
import type { LeadCard, Paged } from "@/lib/types";

export type PickedLead = { id: string; name: string };

/** Busca leads visíveis ao usuário (o escopo é aplicado pela API). */
export function LeadPicker({ value, onChange, id }: { value: PickedLead | null; onChange: (lead: PickedLead | null) => void; id?: string }) {
  const [term, setTerm] = useState("");
  const deferred = useDeferredValue(term.trim());
  const { data } = useQuery({
    queryKey: ["lead-picker", deferred],
    queryFn: () => api.get<Paged<LeadCard>>(`leads?pageSize=8&status=Open&search=${encodeURIComponent(deferred)}`),
    enabled: !value && deferred.length >= 2,
  });

  if (value) {
    return (
      <div className="flex items-center justify-between rounded-md border border-slate-300 bg-slate-50 px-3 py-2 text-sm">
        <span>{value.name}</span>
        <button type="button" className="text-xs text-slate-500 hover:underline" onClick={() => onChange(null)}>
          trocar
        </button>
      </div>
    );
  }

  return (
    <div className="relative">
      <input id={id} className="input" placeholder="Digite ao menos 2 letras do nome, telefone ou e-mail" value={term} onChange={(e) => setTerm(e.target.value)} autoComplete="off" />
      {data && deferred.length >= 2 && (
        <ul className="absolute z-10 mt-1 max-h-56 w-full overflow-auto rounded-md border border-slate-200 bg-white shadow-lg">
          {data.items.map((l) => (
            <li key={l.id}>
              <button type="button" className="w-full px-3 py-2 text-left text-sm hover:bg-slate-50" onClick={() => { onChange({ id: l.id, name: l.contactName }); setTerm(""); }}>
                {l.contactName} <span className="text-xs text-slate-500">· {l.ownerName}</span>
              </button>
            </li>
          ))}
          {data.items.length === 0 && <li className="px-3 py-2 text-sm text-slate-500">Nenhum lead encontrado.</li>}
        </ul>
      )}
    </div>
  );
}
