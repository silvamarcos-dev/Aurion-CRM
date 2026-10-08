"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { ChevronLeft, ChevronRight, Plus, TriangleAlert } from "lucide-react";
import { AppointmentDialog, kindLabels } from "@/components/appointment-dialog";
import { PageHeader } from "@/components/ui/page-header";
import { Modal } from "@/components/modal";
import { useCan, useMe, useToast } from "@/components/providers";
import { api, ApiError } from "@/lib/api";
import { addDays, formatDayLabel, minutesOfDay, startOfWeek, todayInZone, zonedDate, zonedTime } from "@/lib/time";
import type { Agenda, Appointment, UserSummary } from "@/lib/types";

const DAY_START = 7 * 60;
const DAY_END = 21 * 60;
const PX_PER_MIN = 0.8;

const statusLabels: Record<string, string> = { Scheduled: "Agendado", Completed: "Realizado", Cancelled: "Cancelado", NoShow: "Não compareceu" };

export default function AgendaPage() {
  const can = useCan();
  const { data: me } = useMe();
  const [weekStart, setWeekStart] = useState(() => startOfWeek(todayInZone()));
  const [userId, setUserId] = useState<string>("");
  const [create, setCreate] = useState<{ date: string; time: string } | null>(null);
  const [selected, setSelected] = useState<Appointment | null>(null);

  const scope = me?.capabilities["calendar.read"];
  const { data: users } = useQuery({
    queryKey: ["users-in-scope", "calendar.read"],
    queryFn: () => api.get<UserSummary[]>("users/in-scope?capability=calendar.read"),
    enabled: Boolean(scope && scope !== "own"),
  });

  const to = addDays(weekStart, 6);
  const { data: agenda, isLoading, error } = useQuery({
    queryKey: ["agenda", userId || "me", weekStart],
    queryFn: () => api.get<Agenda>(`agenda?from=${weekStart}&to=${to}${userId ? `&userId=${userId}` : ""}`),
  });

  const days = agenda?.days ?? [];
  const today = todayInZone();
  const height = (DAY_END - DAY_START) * PX_PER_MIN;
  const hours = Array.from({ length: (DAY_END - DAY_START) / 60 + 1 }, (_, i) => DAY_START / 60 + i);

  const top = (iso: string) => Math.max(0, (minutesOfDay(zonedTime(iso)) - DAY_START) * PX_PER_MIN);
  const blockHeight = (start: string, end: string) => {
    const s = Math.max(minutesOfDay(zonedTime(start)), DAY_START);
    const e = zonedDate(end) !== zonedDate(start) ? DAY_END : Math.min(minutesOfDay(zonedTime(end)), DAY_END);
    return Math.max(18, (e - s) * PX_PER_MIN);
  };

  return (
    <div className="flex h-[calc(100dvh-60px)] flex-col md:h-dvh">
      <PageHeader
        title="Agenda"
        description={`${formatDayLabel(weekStart)} – ${formatDayLabel(to)} · fuso ${agenda?.timeZone ?? "da organização"}`}
        actions={
          <>
            {users && users.length > 1 && (
              <select className="input w-auto" value={userId} onChange={(e) => setUserId(e.target.value)} aria-label="Agenda de">
                <option value="">Minha agenda</option>
                {users.filter((u) => u.userId !== me?.userId).map((u) => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}
              </select>
            )}
            <div className="flex items-center rounded-lg border border-slate-200 bg-white shadow-[0_1px_2px_rgb(0_0_0/0.04)]">
              <button className="rounded-l-lg p-2 text-slate-600 hover:bg-slate-50" onClick={() => setWeekStart(addDays(weekStart, -7))} aria-label="Semana anterior"><ChevronLeft className="size-4" /></button>
              <button className="border-x border-slate-200 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50" onClick={() => setWeekStart(startOfWeek(todayInZone()))}>Hoje</button>
              <button className="rounded-r-lg p-2 text-slate-600 hover:bg-slate-50" onClick={() => setWeekStart(addDays(weekStart, 7))} aria-label="Próxima semana"><ChevronRight className="size-4" /></button>
            </div>
            {can("calendar.manage") && (
              <button className="btn-primary" onClick={() => setCreate({ date: addDays(today, 1), time: "10:00" })}><Plus className="size-4" /> Compromisso</button>
            )}
          </>
        }
      />

      {agenda && !agenda.schedule.isConfigured && (
        <div className="flex items-center gap-2 border-b border-amber-200 bg-amber-50 px-4 py-2.5 text-sm text-amber-900 sm:px-6 lg:px-8">
          <TriangleAlert className="size-4 shrink-0" />
          Jornada de trabalho não cadastrada — sem ela não há disponibilidade para visitas.{" "}
          {!userId && <Link href="/configuracoes" className="font-medium underline">Cadastrar jornada</Link>}
        </div>
      )}

      {isLoading && <p className="p-6 text-sm text-slate-500">Carregando agenda…</p>}
      {error && <p className="p-6 text-sm text-red-600">{error instanceof ApiError ? error.message : "Erro ao carregar a agenda."}</p>}

      {agenda && (
        <div className="flex-1 overflow-auto px-4 py-4 sm:px-6 lg:px-8">
          <div className="grid min-w-[860px] grid-cols-[48px_repeat(7,minmax(0,1fr))] gap-x-1">
            <div />
            {days.map((d) => (
              <div key={d.date} className="px-1 pb-2 text-center text-xs text-slate-600">
                <span className={`inline-flex items-center rounded-full px-2.5 py-1 ${d.date === today ? "bg-brand-700 font-semibold text-white" : ""}`}>{formatDayLabel(d.date)}</span>
                {d.holidayName && <div className="truncate text-[10px] font-normal text-rose-600" title={d.holidayName}>{d.holidayName}</div>}
              </div>
            ))}

            <div className="relative" style={{ height }}>
              {hours.map((h) => (
                <span key={h} className="absolute right-1 -translate-y-1/2 text-[10px] text-slate-400" style={{ top: (h * 60 - DAY_START) * PX_PER_MIN }}>
                  {String(h).padStart(2, "0")}h
                </span>
              ))}
            </div>

            {days.map((d) => (
              <div
                key={d.date}
                className="relative overflow-hidden rounded-xl border border-slate-200/80 bg-slate-100/80"
                style={{ height }}
                onDoubleClick={(e) => {
                  if (!can("calendar.manage")) return;
                  const rect = e.currentTarget.getBoundingClientRect();
                  const minutes = DAY_START + Math.floor((e.clientY - rect.top) / PX_PER_MIN / 30) * 30;
                  setCreate({ date: d.date, time: `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}` });
                }}
              >
                {hours.map((h) => (
                  <div key={h} className="absolute inset-x-0 border-t border-slate-200/70" style={{ top: (h * 60 - DAY_START) * PX_PER_MIN }} />
                ))}
                {d.workWindows.map((w) => (
                  <div key={w.start} className="absolute inset-x-0 bg-white" style={{ top: top(w.start), height: blockHeight(w.start, w.end) }} title="Jornada de trabalho" />
                ))}
                {agenda.absences.filter((a) => zonedDate(a.startsAt) <= d.date && zonedDate(a.endsAt) >= d.date).map((a) => {
                  const startsToday = zonedDate(a.startsAt) === d.date;
                  const endsToday = zonedDate(a.endsAt) === d.date;
                  const t = startsToday ? top(a.startsAt) : 0;
                  const bottom = endsToday ? (minutesOfDay(zonedTime(a.endsAt)) - DAY_START) * PX_PER_MIN : height;
                  return (
                    <div key={a.id} className="absolute inset-x-0.5 rounded bg-[repeating-linear-gradient(45deg,#fecaca_0,#fecaca_6px,#fee2e2_6px,#fee2e2_12px)] px-1 text-[10px] text-red-800"
                      style={{ top: t, height: Math.max(16, bottom - t) }} title={a.note ?? "Ausência"}>
                      Ausência
                    </div>
                  );
                })}
                {agenda.appointments.filter((a) => zonedDate(a.startsAt) === d.date && a.status !== "Cancelled").map((a) => (
                  <button
                    key={a.id}
                    onClick={() => setSelected(a)}
                    className={`absolute inset-x-1 overflow-hidden rounded-lg border-l-[3px] px-1.5 py-1 text-left text-[11px] leading-tight shadow-card transition hover:shadow-md ${
                      a.kind === "Visit" ? "border-brand-500 bg-brand-50 text-brand-900" : "border-sky-500 bg-sky-50 text-sky-900"
                    } ${a.status !== "Scheduled" ? "opacity-60" : ""}`}
                    style={{ top: top(a.startsAt), height: blockHeight(a.startsAt, a.endsAt) }}
                  >
                    <span className="font-medium">{zonedTime(a.startsAt)}</span> {a.leadName ?? a.title}
                  </button>
                ))}
              </div>
            ))}
          </div>
          <p className="mt-3 text-xs text-slate-500">
            Branco = jornada de trabalho · cinza = fora da jornada · {can("calendar.manage") ? "clique duas vezes para criar um compromisso." : ""}
          </p>
        </div>
      )}

      <AppointmentDialog open={create !== null} onClose={() => setCreate(null)} agentUserId={userId || undefined} initialDate={create?.date} initialTime={create?.time} />
      <AppointmentDetails appointment={selected} onClose={() => setSelected(null)} />
    </div>
  );
}

function AppointmentDetails({ appointment, onClose }: { appointment: Appointment | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const can = useCan();
  const [reason, setReason] = useState("");

  const action = useMutation({
    mutationFn: ({ path, body }: { path: string; body: unknown }) => api.post(`appointments/${appointment!.id}/${path}`, body),
    onSuccess: () => {
      toast("Compromisso atualizado.", "success");
      queryClient.invalidateQueries({ queryKey: ["agenda"] });
      queryClient.invalidateQueries({ queryKey: ["lead-appointments"] });
      setReason("");
      onClose();
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao atualizar.", "error"),
  });

  if (!appointment) return null;
  const a = appointment;
  const editable = can("calendar.manage") && a.status === "Scheduled";

  return (
    <Modal title={a.title} open onClose={onClose}>
      <dl className="grid grid-cols-[110px_1fr] gap-y-2 text-sm">
        <dt className="text-slate-500">Tipo</dt><dd>{kindLabels[a.kind]}</dd>
        <dt className="text-slate-500">Quando</dt><dd>{formatDayLabel(zonedDate(a.startsAt))}, {zonedTime(a.startsAt)}–{zonedTime(a.endsAt)}</dd>
        <dt className="text-slate-500">Corretor</dt><dd>{a.agentName}</dd>
        {a.leadId && (<><dt className="text-slate-500">Lead</dt><dd><Link className="text-brand-800 hover:underline" href={`/leads/${a.leadId}`}>{a.leadName}</Link></dd></>)}
        {a.location && (<><dt className="text-slate-500">Local</dt><dd>{a.location}</dd></>)}
        {a.notes && (<><dt className="text-slate-500">Observações</dt><dd className="whitespace-pre-wrap">{a.notes}</dd></>)}
        <dt className="text-slate-500">Situação</dt><dd>{statusLabels[a.status]}{a.cancelReason ? ` — ${a.cancelReason}` : ""}</dd>
        <dt className="text-slate-500">Criado por</dt><dd>{a.createdByKind === "Copilot" ? "Copilot" : a.createdByKind === "System" ? "Sistema" : "Usuário"}</dd>
      </dl>

      {editable && (
        <div className="mt-5 space-y-3 border-t border-slate-200 pt-4">
          <div className="flex flex-wrap gap-2">
            <button className="btn-primary" disabled={action.isPending} onClick={() => action.mutate({ path: "complete", body: { attended: true } })}>Realizado</button>
            <button className="btn-secondary" disabled={action.isPending} onClick={() => action.mutate({ path: "complete", body: { attended: false } })}>Não compareceu</button>
          </div>
          <div className="flex gap-2">
            <input className="input" placeholder="Motivo do cancelamento" maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} />
            <button className="btn-danger shrink-0" disabled={action.isPending} onClick={() => action.mutate({ path: "cancel", body: { reason: reason || null } })}>Cancelar</button>
          </div>
        </div>
      )}
    </Modal>
  );
}
