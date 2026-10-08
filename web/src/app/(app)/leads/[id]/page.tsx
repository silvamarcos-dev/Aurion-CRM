"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { use, useEffect, useState, type FormEvent } from "react";
import { ArrowLeft, Briefcase, CalendarDays, CalendarPlus, CircleCheckBig, History, Mail, Phone, Plus, UserRound } from "lucide-react";
import { AppointmentDialog, kindLabels } from "@/components/appointment-dialog";
import { Avatar } from "@/components/ui/avatar";
import { StageBadge } from "@/components/ui/stage-badge";
import { useCan, useToast } from "@/components/providers";
import { TaskDialog } from "@/components/task-dialog";
import { TaskRow } from "@/components/task-row";
import { formatDayLabel, zonedDate, zonedTime } from "@/lib/time";
import { api, ApiError } from "@/lib/api";
import { actionLabels, formatCents, formatDateTime, formatPhone, statusLabels, timeframeLabels } from "@/lib/format";
import type { Appointment, HistoryItem, LeadDetail, Pipeline, TaskItem, UserSummary } from "@/lib/types";

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
  };
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

  if (error) return <div className="p-8 text-sm text-red-600">Lead não encontrado ou sem permissão de acesso.</div>;
  if (!lead || !form) return <div className="p-8"><div className="h-28 animate-pulse rounded-xl bg-white" /></div>;

  const stages = pipelines?.find((p) => p.id === lead.pipelineId)?.stages.filter((s) => !s.isArchived) ?? [];
  const target = stages.find((s) => s.id === targetStage);
  const editable = can("leads.update");
  const set = (key: keyof FormState) => (e: { target: { value: string } }) => setForm((f) => f && { ...f, [key]: e.target.value });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (form) save.mutate(form);
  }

  return (
    <div>
      <header className="border-b border-slate-200/80 bg-white">
        <div className="mx-auto max-w-6xl px-4 pb-5 pt-4 sm:px-6 lg:px-8">
          <Link href="/kanban" className="inline-flex items-center gap-1 text-xs font-medium text-slate-500 hover:text-brand-700">
            <ArrowLeft className="size-3.5" /> Funil
          </Link>
          <div className="mt-3 flex flex-wrap items-center justify-between gap-4">
            <div className="flex min-w-0 items-center gap-4">
              <Avatar name={lead.fullName} size="lg" />
              <div className="min-w-0">
                <h1 className="truncate text-2xl font-semibold text-slate-900">{lead.fullName}</h1>
                <div className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-slate-500">
                  <span className="flex items-center gap-1.5"><Phone className="size-3.5" />{formatPhone(lead.phone)}</span>
                  <span className="flex items-center gap-1.5"><Mail className="size-3.5" />{lead.email ?? "sem e-mail"}</span>
                  <span className="flex items-center gap-1.5"><CalendarPlus className="size-3.5" />entrou em {formatDateTime(lead.enteredAt)}</span>
                </div>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <StageBadge stage={stages.find((st) => st.id === lead.stageId)} name={lead.stageName} />
              <span className={`chip ring-1 ring-inset ${lead.status === "Won" ? "bg-brand-50 text-brand-800 ring-brand-200" : lead.status === "Lost" ? "bg-red-50 text-red-700 ring-red-200" : "bg-sky-50 text-sky-800 ring-sky-200"}`}>
                {statusLabels[lead.status]}
              </span>
            </div>
          </div>
        </div>
      </header>

      <div className="mx-auto grid max-w-6xl gap-6 px-4 py-6 sm:px-6 lg:grid-cols-3 lg:px-8">
        <form onSubmit={submit} className="card space-y-4 p-5 lg:col-span-2">
          <h2 className="section-title flex items-center gap-2"><UserRound className="size-4 text-slate-400" /> Dados do lead</h2>
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
            <div className="sm:col-span-2">
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

        <div className="space-y-6 lg:col-span-2 lg:row-start-2">
          <LeadTasks leadId={lead.id} leadName={lead.fullName} />
          <LeadAppointments leadId={lead.id} leadName={lead.fullName} />
        </div>

        <div className="space-y-6 lg:col-start-3 lg:row-span-2 lg:row-start-1">
          <section className="card space-y-3 p-5">
            <h2 className="section-title flex items-center gap-2"><Briefcase className="size-4 text-slate-400" /> Comercial</h2>
            <dl className="grid grid-cols-2 gap-y-2 text-sm">
              <dt className="text-slate-500">Origem</dt><dd>{lead.source ?? "—"}</dd>
              <dt className="text-slate-500">Campanha</dt><dd>{lead.campaign ?? "—"}</dd>
              <dt className="text-slate-500">Faixa</dt><dd>{lead.budgetMinCents == null && lead.budgetMaxCents == null ? "Não informada" : `${formatCents(lead.budgetMinCents)} – ${formatCents(lead.budgetMaxCents)}`}</dd>
              <dt className="text-slate-500">Último contato</dt><dd>{formatDateTime(lead.lastContactAt)}</dd>
              <dt className="text-slate-500">Próximo follow-up</dt>
              <dd className={lead.nextFollowUpAt && new Date(lead.nextFollowUpAt) < new Date() ? "font-medium text-red-600" : ""}>{formatDateTime(lead.nextFollowUpAt)}</dd>
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
            <h2 className="section-title mb-4 flex items-center gap-2"><History className="size-4 text-slate-400" /> Histórico</h2>
            <ol className="space-y-3 border-l border-slate-200 pl-4">
              {history?.map((h, i) => (
                <li key={i} className="relative text-sm">
                  <span className="absolute -left-[21px] top-1.5 size-2 rounded-full bg-brand-400 ring-4 ring-white" aria-hidden />
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

function LeadTasks({ leadId, leadName }: { leadId: string; leadName: string }) {
  const can = useCan();
  const [open, setOpen] = useState(false);
  const { data: tasks } = useQuery({ queryKey: ["lead-tasks", leadId], queryFn: () => api.get<TaskItem[]>(`tasks?leadId=${leadId}&limit=50`) });
  const openTasks = tasks?.filter((t) => t.status === "Open") ?? [];
  const closed = tasks?.filter((t) => t.status !== "Open").slice(0, 5) ?? [];

  return (
    <section className="card p-5">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="section-title flex items-center gap-2"><CircleCheckBig className="size-4 text-slate-400" /> Tarefas e follow-ups</h2>
        {can("tasks.manage") && <button className="btn-secondary" onClick={() => setOpen(true)}><Plus className="size-4" /> Tarefa</button>}
      </div>
      <ul className="space-y-2">
        {openTasks.map((t) => <TaskRow key={t.id} task={t} hideLead />)}
        {closed.map((t) => <TaskRow key={t.id} task={t} hideLead />)}
        {tasks?.length === 0 && <li className="text-sm text-slate-500">Nenhuma tarefa. Crie um follow-up para não perder o timing.</li>}
      </ul>
      <TaskDialog open={open} onClose={() => setOpen(false)} lead={{ id: leadId, name: leadName }} />
    </section>
  );
}

const appointmentStatusText: Record<string, string> = { Scheduled: "Agendado", Completed: "Realizado", Cancelled: "Cancelado", NoShow: "Não compareceu" };

function LeadAppointments({ leadId, leadName }: { leadId: string; leadName: string }) {
  const can = useCan();
  const [open, setOpen] = useState(false);
  const { data: appointments } = useQuery({ queryKey: ["lead-appointments", leadId], queryFn: () => api.get<Appointment[]>(`leads/${leadId}/appointments`) });

  return (
    <section className="card p-5">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="section-title flex items-center gap-2"><CalendarDays className="size-4 text-slate-400" /> Visitas e compromissos</h2>
        {can("calendar.manage") && <button className="btn-secondary" onClick={() => setOpen(true)}><Plus className="size-4" /> Agendar visita</button>}
      </div>
      <ul className="space-y-2">
        {appointments?.map((a) => (
          <li key={a.id} className="flex flex-wrap items-center gap-2 rounded-lg border border-slate-100 bg-slate-50/70 px-3 py-2.5 text-sm">
            <span className="font-medium">{formatDayLabel(zonedDate(a.startsAt))} {zonedTime(a.startsAt)}</span>
            <span className="text-slate-600">{kindLabels[a.kind]} com {a.agentName}</span>
            {a.location && <span className="text-xs text-slate-500">· {a.location}</span>}
            <span className={`ml-auto rounded px-1.5 text-xs ${a.status === "Scheduled" ? "bg-brand-100 text-brand-800" : "bg-slate-200 text-slate-600"}`}>{appointmentStatusText[a.status]}</span>
          </li>
        ))}
        {appointments?.length === 0 && <li className="text-sm text-slate-500">Nenhuma visita agendada.</li>}
      </ul>
      <AppointmentDialog open={open} onClose={() => setOpen(false)} lead={{ id: leadId, name: leadName }} />
    </section>
  );
}
