"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, type FormEvent } from "react";
import { LeadPicker, type PickedLead } from "@/components/lead-picker";
import { Modal } from "@/components/modal";
import { useToast } from "@/components/providers";
import { api, ApiError } from "@/lib/api";
import { addDays, formatDayLabel, todayInZone, zonedDate, zonedTime, zonedToIso } from "@/lib/time";
import type { AppointmentKind, Availability } from "@/lib/types";

export const kindLabels: Record<AppointmentKind, string> = { Visit: "Visita", Meeting: "Reunião", Call: "Ligação", Personal: "Pessoal" };

type Props = {
  open: boolean;
  onClose: () => void;
  /** Corretor dono da agenda (omitido = usuário atual ou dono do lead). */
  agentUserId?: string;
  lead?: PickedLead | null;
  initialDate?: string;
  initialTime?: string;
};

/**
 * Agendamento feito por HUMANO. Sugere apenas horários realmente livres (calculados pela API).
 * Avisos (fora da jornada, antecedência, feriado…) exigem confirmação explícita; conflitos e ausências bloqueiam.
 */
export function AppointmentDialog({ open, onClose, agentUserId, lead: initialLead = null, initialDate, initialTime }: Props) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [lead, setLead] = useState<PickedLead | null>(initialLead);
  const [kind, setKind] = useState<AppointmentKind>("Visit");
  const [title, setTitle] = useState("");
  const [date, setDate] = useState(initialDate ?? addDays(todayInZone(), 1));
  const [time, setTime] = useState(initialTime ?? "10:00");
  const [duration, setDuration] = useState(60);
  const [location, setLocation] = useState("");
  const [notes, setNotes] = useState("");
  const [warning, setWarning] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setLead(initialLead);
    setDate(initialDate ?? addDays(todayInZone(), 1));
    setTime(initialTime ?? "10:00");
    setWarning(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initialDate, initialTime, initialLead?.id]);

  const userParam = agentUserId ? `&userId=${agentUserId}` : "";
  const { data: availability } = useQuery({
    queryKey: ["availability", agentUserId ?? "me", date, duration],
    queryFn: () => api.get<Availability>(`agenda/availability?from=${date}&to=${addDays(date, 6)}&durationMinutes=${duration}${userParam}`),
    enabled: open,
  });

  const create = useMutation({
    mutationFn: (confirmWarnings: boolean) => {
      const startsAt = zonedToIso(date, time);
      const endsAt = new Date(new Date(startsAt).getTime() + duration * 60_000).toISOString();
      return api.post("appointments", {
        agentUserId: agentUserId ?? null,
        leadId: lead?.id ?? null,
        kind,
        title: title || null,
        startsAt,
        endsAt,
        location: location || null,
        notes: notes || null,
        confirmWarnings,
      });
    },
    onSuccess: () => {
      toast("Compromisso agendado.", "success");
      queryClient.invalidateQueries({ queryKey: ["agenda"] });
      queryClient.invalidateQueries({ queryKey: ["availability"] });
      queryClient.invalidateQueries({ queryKey: ["lead-appointments"] });
      queryClient.invalidateQueries({ queryKey: ["lead-history"] });
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === "appointment.needs_confirmation") {
        setWarning(err.message);
        return;
      }
      toast(err instanceof ApiError ? err.message : "Não foi possível agendar.", "error");
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate(false);
  }

  const slotsByDay = new Map<string, string[]>();
  for (const slot of availability?.slots ?? []) {
    const day = zonedDate(slot.start);
    slotsByDay.set(day, [...(slotsByDay.get(day) ?? []), zonedTime(slot.start)]);
  }

  return (
    <Modal title="Novo compromisso" open={open} onClose={onClose}>
      <form onSubmit={submit} className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="label" htmlFor="ap-kind">Tipo</label>
            <select id="ap-kind" className="input" value={kind} onChange={(e) => setKind(e.target.value as AppointmentKind)}>
              {Object.entries(kindLabels).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </select>
          </div>
          <div>
            <label className="label" htmlFor="ap-duration">Duração</label>
            <select id="ap-duration" className="input" value={duration} onChange={(e) => setDuration(Number(e.target.value))}>
              {[30, 45, 60, 90, 120].map((m) => <option key={m} value={m}>{m} min</option>)}
            </select>
          </div>
        </div>

        <div>
          <label className="label" htmlFor="ap-lead">Lead {kind === "Visit" && "*"}</label>
          <LeadPicker id="ap-lead" value={lead} onChange={setLead} />
        </div>

        {kind !== "Visit" && (
          <div>
            <label className="label" htmlFor="ap-title">Título</label>
            <input id="ap-title" className="input" maxLength={160} value={title} onChange={(e) => setTitle(e.target.value)} />
          </div>
        )}

        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="label" htmlFor="ap-date">Data</label>
            <input id="ap-date" type="date" className="input" required value={date} onChange={(e) => { setDate(e.target.value); setWarning(null); }} />
          </div>
          <div>
            <label className="label" htmlFor="ap-time">Horário</label>
            <input id="ap-time" type="time" className="input" required step={900} value={time} onChange={(e) => { setTime(e.target.value); setWarning(null); }} />
          </div>
        </div>

        <div className="rounded-md bg-slate-50 p-3">
          <p className="mb-2 text-xs font-medium text-slate-600">Horários livres na agenda (próximos 7 dias)</p>
          {availability && !availability.isScheduleConfigured && (
            <p className="text-xs text-amber-700">O corretor ainda não cadastrou a jornada de trabalho — não há disponibilidade calculada.</p>
          )}
          {availability?.isScheduleConfigured && slotsByDay.size === 0 && <p className="text-xs text-slate-500">Nenhum horário livre no período.</p>}
          <div className="max-h-36 space-y-1.5 overflow-y-auto">
            {[...slotsByDay.entries()].map(([day, times]) => (
              <div key={day} className="flex flex-wrap items-center gap-1">
                <span className="w-20 shrink-0 text-xs text-slate-500">{formatDayLabel(day)}</span>
                {times.map((t) => (
                  <button
                    key={t}
                    type="button"
                    onClick={() => { setDate(day); setTime(t); setWarning(null); }}
                    className={`rounded px-2 py-0.5 text-xs ring-1 ${day === date && t === time ? "bg-brand-700 text-white ring-brand-700" : "bg-white text-slate-700 ring-slate-200 hover:ring-brand-500"}`}
                  >
                    {t}
                  </button>
                ))}
              </div>
            ))}
          </div>
        </div>

        <div>
          <label className="label" htmlFor="ap-location">Local</label>
          <input id="ap-location" className="input" maxLength={300} placeholder="Stand de vendas, lote 12…" value={location} onChange={(e) => setLocation(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="ap-notes">Observações</label>
          <textarea id="ap-notes" className="input min-h-16" maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>

        {warning && (
          <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
            <p>{warning}</p>
            <button type="button" className="btn-secondary mt-2" onClick={() => create.mutate(true)} disabled={create.isPending}>
              Agendar mesmo assim
            </button>
          </div>
        )}

        <div className="flex justify-end gap-2">
          <button type="button" className="btn-secondary" onClick={onClose}>Cancelar</button>
          <button className="btn-primary" disabled={create.isPending || (kind === "Visit" && !lead)}>
            {create.isPending ? "Agendando…" : "Agendar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
