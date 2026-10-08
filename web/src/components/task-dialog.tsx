"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, type FormEvent } from "react";
import { LeadPicker, type PickedLead } from "@/components/lead-picker";
import { Modal } from "@/components/modal";
import { useCan, useMe, useToast } from "@/components/providers";
import { api, ApiError } from "@/lib/api";
import { addDays, todayInZone, zonedToIso } from "@/lib/time";
import type { TaskKind, UserSummary } from "@/lib/types";

export const taskKindLabels: Record<TaskKind, string> = {
  FollowUp: "Follow-up",
  Call: "Ligação",
  Message: "Mensagem",
  Document: "Documentação",
  Other: "Outro",
};

export function TaskDialog({ open, onClose, lead: initialLead = null }: { open: boolean; onClose: () => void; lead?: PickedLead | null }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const can = useCan();
  const { data: me } = useMe();
  const [lead, setLead] = useState<PickedLead | null>(initialLead);
  const [kind, setKind] = useState<TaskKind>("FollowUp");
  const [title, setTitle] = useState("");
  const [date, setDate] = useState(addDays(todayInZone(), 1));
  const [time, setTime] = useState("09:00");
  const [notes, setNotes] = useState("");
  const [assignee, setAssignee] = useState("");

  const canDelegate = me?.capabilities["tasks.manage"] && me.capabilities["tasks.manage"] !== "own";
  const { data: users } = useQuery({
    queryKey: ["users-in-scope", "tasks.manage"],
    queryFn: () => api.get<UserSummary[]>("users/in-scope?capability=tasks.manage"),
    enabled: open && Boolean(canDelegate),
  });

  useEffect(() => {
    if (open) setLead(initialLead);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initialLead?.id]);

  const create = useMutation({
    mutationFn: () =>
      api.post("tasks", {
        leadId: lead?.id ?? null,
        assigneeUserId: assignee || null,
        kind,
        title,
        notes: notes || null,
        dueAt: zonedToIso(date, time),
      }),
    onSuccess: () => {
      toast("Tarefa criada.", "success");
      for (const key of ["tasks", "lead-tasks", "lead", "board", "lead-history"]) queryClient.invalidateQueries({ queryKey: [key] });
      setTitle("");
      setNotes("");
      onClose();
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Não foi possível criar a tarefa.", "error"),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate();
  }

  if (!can("tasks.manage")) return null;

  return (
    <Modal title="Nova tarefa" open={open} onClose={onClose}>
      <form onSubmit={submit} className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="label" htmlFor="tk-kind">Tipo</label>
            <select id="tk-kind" className="input" value={kind} onChange={(e) => setKind(e.target.value as TaskKind)}>
              {Object.entries(taskKindLabels).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </select>
          </div>
          {canDelegate && (
            <div>
              <label className="label" htmlFor="tk-assignee">Responsável</label>
              <select id="tk-assignee" className="input" value={assignee} onChange={(e) => setAssignee(e.target.value)}>
                <option value="">{lead ? "Dono do lead" : "Eu"}</option>
                {users?.map((u) => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}
              </select>
            </div>
          )}
        </div>
        <div>
          <label className="label" htmlFor="tk-title">Título *</label>
          <input id="tk-title" className="input" required maxLength={160} value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Retornar sobre a proposta" />
        </div>
        <div>
          <label className="label" htmlFor="tk-lead">Lead</label>
          <LeadPicker id="tk-lead" value={lead} onChange={setLead} />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="label" htmlFor="tk-date">Vencimento</label>
            <input id="tk-date" type="date" className="input" required value={date} onChange={(e) => setDate(e.target.value)} />
          </div>
          <div>
            <label className="label" htmlFor="tk-time">Horário</label>
            <input id="tk-time" type="time" className="input" required value={time} onChange={(e) => setTime(e.target.value)} />
          </div>
        </div>
        <div>
          <label className="label" htmlFor="tk-notes">Observações</label>
          <textarea id="tk-notes" className="input min-h-16" maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>
        <div className="flex justify-end gap-2">
          <button type="button" className="btn-secondary" onClick={onClose}>Cancelar</button>
          <button className="btn-primary" disabled={create.isPending}>{create.isPending ? "Salvando…" : "Criar tarefa"}</button>
        </div>
      </form>
    </Modal>
  );
}
