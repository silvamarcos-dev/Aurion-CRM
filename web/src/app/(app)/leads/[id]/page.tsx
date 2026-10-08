"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { use, useEffect, useState, type FormEvent } from "react";
import { useCan, useToast } from "@/components/providers";
import { api, ApiError } from "@/lib/api";
import { actionLabels, formatCents, formatDateTime, formatPhone, statusLabels, timeframeLabels } from "@/lib/format";
import type { HistoryItem, LeadDetail, Pipeline, UserSummary } from "@/lib/types";

type FormState = {
  fullName: string;
  phone: string;
  email: string;
  interest: string;
  budgetMin: string;
  budgetMax: string;
  timeframe: string;
  region: string;
  notes: string;
  tags: string;
  nextFollowUpAt: string;
};

function toForm(lead: LeadDetail): FormState {
  return {
    fullName: lead.fullName,
    phone: lead.phone ?? "",
    email: lead.email ?? "",
    interest: lead.interest ?? "",
    budgetMin: lead.budgetMinCents != null ? String(lead.budgetMinCents / 100) : "",
    budgetMax: lead.budgetMaxCents != null ? String(lead.budgetMaxCents / 100) : "",
    timeframe: lead.timeframe,
    region: lead.region ?? "",
    notes: lead.notes ?? "",
    tags: lead.tags.join(", "),
    nextFollowUpAt: lead.nextFollowUpAt ? toLocalInput(lead.nextFollowUpAt) : "",
  };
}

function toLocalInput(iso: string) {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

const toCents = (v: string) => (v.trim() === "" ? null : Math.round(Number(v.replace(",", ".")) * 100));

export default function LeadPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const queryClient = useQueryClient();
  const toast = useToast();
  const can = useCan();

  const { data: lead, error } = useQuery({ queryKey: ["lead", id], queryFn: () => api.get<LeadDetail>(`leads/${id}`) });
  const { data: history } = useQuery({ queryKey: ["lead-history", id], queryFn: () => api.get<HistoryItem[]>(`leads/${id}/history`) });
  const { data: pipelines } = useQuery({ queryKey: ["pipelines"], queryFn: () => api.get<Pipeline[]>("pipelines") });
  const { data: assignable } = useQuery({ queryKey: ["assignable"], queryFn: () => api.get<UserSummary[]>("users/assignable"), enabled: can("leads.assign") });

  const [form, setForm] = useState<FormState | null>(null);
  const [lostReason, setLostReason] = useState("");
  const [targetStage, setTargetStage] = useState("");

  useEffect(() => {
    if (lead) setForm(toForm(lead));
  }, [lead]);

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ["lead", id] });
    queryClient.invalidateQueries({ queryKey: ["lead-history", id] });
    queryClient.invalidateQueries({ queryKey: ["board"] });
    queryClient.invalidateQueries({ queryKey: ["leads"] });
  };
  const onError = (err: unknown) => {
    toast(err instanceof ApiError ? err.message : "Operação não concluída.", "error");
    if (err instanceof ApiError && err.status === 409) refresh();
  };

  const save = useMutation({
    mutationFn: (f: FormState) =>
      api.put(`leads/${id}`, {
        fullName: f.fullName,
        phone: f.phone || null,
        email: f.email || null,
        interest: f.interest || null,
        developmentId: lead?.developmentId ?? null,
        lotId: lead?.lotId ?? null,
        budgetMinCents: toCents(f.budgetMin),
        budgetMaxCents: toCents(f.budgetMax),
        timeframe: f.timeframe,
        region: f.region || null,
        notes: f.notes || null,
        tags: f.tags.split(",").map((t) => t.trim()).filter(Boolean),
        nextFollowUpAt: f.nextFollowUpAt ? new Date(f.nextFollowUpAt).toISOString() : null,
        expectedVersion: lead?.version,
      }),
    onSuccess: () => { toast("Alterações salvas.", "success"); refresh(); },
    onError,
  });

  const move = useMutation({
    mutationFn: () => api.post(`leads/${id}/move`, { stageId: targetStage, expectedVersion: lead?.version, reason: lostReason || null }),
    onSuccess: () => { toast("Etapa atualizada.", "success"); setTargetStage(""); setLostReason(""); refresh(); },
    onError,
  });

  const assign = useMutation({
    mutationFn: (ownerUserId: string) => api.post(`leads/${id}/assign`, { ownerUserId }),
    onSuccess: () => { toast("Responsável atualizado.", "success"); refresh(); },
    onError,
  });

  if (error) return <div className="p-6 text-sm text-red-600">Lead não encontrado ou sem permissão de acesso.</div>;
  if (!lead || !form) return <div className="p-6 text-sm text-slate-500">Carregando…</div>;

  const stages = pipelines?.find((p) => p.id === lead.pipelineId)?.stages.filter((s) => !s.isArchived) ?? [];
  const target = stages.find((s) => s.id === targetStage);
  const editable = can("leads.update");
  const set = (key: keyof FormState) => (e: { target: { value: string } }) => setForm((f) => f && { ...f, [key]: e.target.value });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (form) save.mutate(form);
  }

  return (
    <div className="mx-auto max-w-6xl p-6">
      <Link href="/kanban" className="text-xs text-slate-500 hover:underline">← Voltar ao Kanban</Link>

      <div className="mt-2 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold">{lead.fullName}</h1>
          <p className="text-sm text-slate-500">
            {formatPhone(lead.phone)} · {lead.email ?? "sem e-mail"} · entrou em {formatDateTime(lead.enteredAt)}
          </p>
        </div>
        <div className="flex items-center gap-2 text-sm">
          <span className="rounded-full bg-slate-100 px-2.5 py-1 font-medium text-slate-700">{lead.stageName}</span>
          <span className={`rounded-full px-2.5 py-1 font-medium ${lead.status === "Won" ? "bg-emerald-100 text-emerald-800" : lead.status === "Lost" ? "bg-red-100 text-red-700" : "bg-sky-100 text-sky-800"}`}>
            {statusLabels[lead.status]}
          </span>
        </div>
      </div>

      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        <form onSubmit={submit} className="card space-y-4 p-5 lg:col-span-2">
          <h2 className="text-sm font-semibold">Dados do lead</h2>
          <fieldset disabled={!editable} className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <label className="label" htmlFor="f-name">Nome</label>
              <input id="f-name" className="input" required maxLength={200} value={form.fullName} onChange={set("fullName")} />
            </div>
            <div>
              <label className="label" htmlFor="f-phone">Telefone / WhatsApp</label>
              <input id="f-phone" className="input" value={form.phone} onChange={set("phone")} />
            </div>
            <div>
              <label className="label" htmlFor="f-email">E-mail</label>
              <input id="f-email" type="email" className="input" value={form.email} onChange={set("email")} />
            </div>
            <div className="sm:col-span-2">
              <label className="label" htmlFor="f-interest">Interesse</label>
              <input id="f-interest" className="input" maxLength={500} value={form.interest} onChange={set("interest")} />
            </div>
            <div>
              <label className="label" htmlFor="f-bmin">Investimento mínimo (R$)</label>
              <input id="f-bmin" className="input" inputMode="decimal" value={form.budgetMin} onChange={set("budgetMin")} />
            </div>
            <div>
              <label className="label" htmlFor="f-bmax">Investimento máximo (R$)</label>
              <input id="f-bmax" className="input" inputMode="decimal" value={form.budgetMax} onChange={set("budgetMax")} />
            </div>
            <div>
              <label className="label" htmlFor="f-tf">Prazo de compra</label>
              <select id="f-tf" className="input" value={form.timeframe} onChange={set("timeframe")}>
                {Object.entries(timeframeLabels).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
              </select>
            </div>
            <div>
              <label className="label" htmlFor="f-region">Região</label>
              <input id="f-region" className="input" maxLength={120} value={form.region} onChange={set("region")} />
            </div>
            <div>
              <label className="label" htmlFor="f-follow">Próximo follow-up</label>
              <input id="f-follow" type="datetime-local" className="input" value={form.nextFollowUpAt} onChange={set("nextFollowUpAt")} />
            </div>
            <div>
              <label className="label" htmlFor="f-tags">Tags (separadas por vírgula)</label>
              <input id="f-tags" className="input" value={form.tags} onChange={set("tags")} />
            </div>
            <div className="sm:col-span-2">
              <label className="label" htmlFor="f-notes">Observações</label>
              <textarea id="f-notes" className="input min-h-24" maxLength={4000} value={form.notes} onChange={set("notes")} />
            </div>
          </fieldset>
          {editable && (
            <div className="flex justify-end gap-2">
              <button type="button" className="btn-secondary" onClick={() => setForm(toForm(lead))}>Descartar</button>
              <button className="btn-primary" disabled={save.isPending}>{save.isPending ? "Salvando…" : "Salvar"}</button>
            </div>
          )}
        </form>

        <div className="space-y-6">
          <section className="card space-y-3 p-5">
            <h2 className="text-sm font-semibold">Comercial</h2>
            <dl className="grid grid-cols-2 gap-y-2 text-sm">
              <dt className="text-slate-500">Origem</dt><dd>{lead.source ?? "—"}</dd>
              <dt className="text-slate-500">Campanha</dt><dd>{lead.campaign ?? "—"}</dd>
              <dt className="text-slate-500">Faixa</dt><dd>{formatCents(lead.budgetMinCents)} – {formatCents(lead.budgetMaxCents)}</dd>
              <dt className="text-slate-500">Último contato</dt><dd>{formatDateTime(lead.lastContactAt)}</dd>
              {lead.lostReason && (<><dt className="text-slate-500">Motivo da perda</dt><dd>{lead.lostReason}</dd></>)}
            </dl>

            <div>
              <label className="label" htmlFor="owner">Corretor responsável</label>
              {can("leads.assign") ? (
                <select id="owner" className="input" value={lead.ownerUserId} onChange={(e) => assign.mutate(e.target.value)} disabled={assign.isPending}>
                  {!assignable?.some((u) => u.userId === lead.ownerUserId) && <option value={lead.ownerUserId}>{lead.ownerName}</option>}
                  {assignable?.map((u) => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}
                </select>
              ) : (
                <p className="text-sm">{lead.ownerName}</p>
              )}
            </div>

            {can("pipeline.move") && (
              <form onSubmit={(e) => { e.preventDefault(); move.mutate(); }} className="space-y-2">
                <label className="label" htmlFor="stage">Mover para etapa</label>
                <select id="stage" className="input" value={targetStage} onChange={(e) => setTargetStage(e.target.value)}>
                  <option value="">Selecione…</option>
                  {stages.filter((s) => s.id !== lead.stageId).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </select>
                {target?.kind === "Lost" && (
                  <textarea className="input" required maxLength={500} placeholder="Motivo da perda (obrigatório)" value={lostReason} onChange={(e) => setLostReason(e.target.value)} />
                )}
                <button className="btn-secondary w-full" disabled={!targetStage || move.isPending}>Mover</button>
              </form>
            )}
          </section>

          <section className="card p-5">
            <h2 className="mb-3 text-sm font-semibold">Histórico</h2>
            <ol className="space-y-3 border-l border-slate-200 pl-4">
              {history?.map((h, i) => (
                <li key={i} className="relative text-sm">
                  <span className="absolute -left-[21px] top-1.5 size-2 rounded-full bg-slate-300" aria-hidden />
                  <p className="font-medium">{actionLabels[h.action] ?? h.action}</p>
                  <p className="text-xs text-slate-500">
                    {formatDateTime(h.occurredAt)} · {h.actorKind === "User" ? (h.actorName ?? "Usuário") : h.actorKind === "Copilot" ? "Copilot" : "Sistema"}
                  </p>
                  <HistoryDetail item={h} stages={stages} />
                </li>
              ))}
              {history?.length === 0 && <li className="text-sm text-slate-500">Sem eventos.</li>}
            </ol>
          </section>
        </div>
      </div>
    </div>
  );
}

function HistoryDetail({ item, stages }: { item: HistoryItem; stages: { id: string; name: string }[] }) {
  let data: Record<string, unknown> = {};
  try {
    data = JSON.parse(item.data);
  } catch {
    return null;
  }
  const name = (id: unknown) => stages.find((s) => s.id === id)?.name ?? "etapa removida";

  if (item.action === "lead.stage_changed") {
    return (
      <p className="text-xs text-slate-600">
        {name(data.fromStageId)} → {name(data.toStageId)}
        {typeof data.reason === "string" && data.reason ? ` · ${data.reason}` : ""}
      </p>
    );
  }
  if (item.action === "lead.updated" && Array.isArray(data.fields)) {
    return <p className="text-xs text-slate-600">Campos: {(data.fields as string[]).join(", ")}</p>;
  }
  return null;
}
