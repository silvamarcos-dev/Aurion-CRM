"use client";

import { DndContext, DragOverlay, PointerSensor, KeyboardSensor, useDraggable, useDroppable, useSensor, useSensors, type DragEndEvent, type DragStartEvent } from "@dnd-kit/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { CalendarClock, Clock3, Phone, Plus, Search } from "lucide-react";
import { Modal } from "@/components/modal";
import { NewLeadDialog } from "@/components/new-lead-dialog";
import { useCan, useToast } from "@/components/providers";
import { Avatar } from "@/components/ui/avatar";
import { PageHeader } from "@/components/ui/page-header";
import { api, ApiError } from "@/lib/api";
import { formatDateTime, formatPhone, formatRelative } from "@/lib/format";
import type { Board, BoardStage, LeadCard } from "@/lib/types";

type PendingMove = { lead: LeadCard; target: BoardStage };

export default function KanbanPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const can = useCan();
  const { data: board, isLoading, error } = useQuery({ queryKey: ["board"], queryFn: () => api.get<Board>("board") });
  const [dragging, setDragging] = useState<LeadCard | null>(null);
  const [lostMove, setLostMove] = useState<PendingMove | null>(null);
  const [lostReason, setLostReason] = useState("");
  const [newLead, setNewLead] = useState<{ open: boolean; stageId?: string }>({ open: false });
  const [filter, setFilter] = useState("");

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }), useSensor(KeyboardSensor));

  const move = useMutation({
    mutationFn: ({ lead, target, reason }: PendingMove & { reason?: string }) =>
      api.post<{ version: number }>(`leads/${lead.id}/move`, { stageId: target.id, expectedVersion: lead.version, reason }),
    // Atualização otimista: o card muda de coluna imediatamente; desfaz se a API recusar.
    onMutate: async ({ lead, target }) => {
      await queryClient.cancelQueries({ queryKey: ["board"] });
      const previous = queryClient.getQueryData<Board>(["board"]);
      queryClient.setQueryData<Board>(["board"], (current) =>
        current && {
          ...current,
          stages: current.stages.map((stage) => {
            if (stage.id === lead.stageId) return { ...stage, total: stage.total - 1, leads: stage.leads.filter((l) => l.id !== lead.id) };
            if (stage.id === target.id) return { ...stage, total: stage.total + 1, leads: [{ ...lead, stageId: target.id, stageEnteredAt: new Date().toISOString() }, ...stage.leads] };
            return stage;
          }),
        },
      );
      return { previous };
    },
    onError: (err, _vars, context) => {
      if (context?.previous) queryClient.setQueryData(["board"], context.previous);
      const message = err instanceof ApiError ? err.message : "Não foi possível mover o lead.";
      toast(message, "error");
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["board"] }),
  });

  function onDragStart(event: DragStartEvent) {
    setDragging((event.active.data.current as { lead: LeadCard }).lead);
  }

  function onDragEnd(event: DragEndEvent) {
    setDragging(null);
    const lead = (event.active.data.current as { lead: LeadCard } | undefined)?.lead;
    const target = board?.stages.find((s) => s.id === event.over?.id);
    if (!lead || !target || target.id === lead.stageId) return;

    if (target.kind === "Lost") {
      setLostReason("");
      setLostMove({ lead, target });
      return;
    }
    move.mutate({ lead, target });
  }

  const canMove = can("pipeline.move");
  const term = filter.trim().toLowerCase();
  const visible = (leads: LeadCard[]) =>
    term ? leads.filter((l) => l.contactName.toLowerCase().includes(term) || (l.phone ?? "").includes(term) || (l.interest ?? "").toLowerCase().includes(term)) : leads;

  if (isLoading) return <BoardSkeleton />;
  if (error || !board) return <div className="p-8 text-sm text-red-600">Não foi possível carregar o funil.</div>;

  const total = board.stages.reduce((n, s) => n + s.total, 0);
  const open = board.stages.filter((s) => s.kind === "Open").reduce((n, s) => n + s.total, 0);
  const won = board.stages.filter((s) => s.kind === "Won").reduce((n, s) => n + s.total, 0);

  return (
    <div className="flex h-[calc(100dvh-60px)] flex-col md:h-dvh">
      <PageHeader
        title={board.pipelineName}
        description={
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <span>{total} leads visíveis</span>
            <span className="flex items-center gap-1"><span className="size-1.5 rounded-full bg-sky-500" />{open} em andamento</span>
            <span className="flex items-center gap-1"><span className="size-1.5 rounded-full bg-brand-500" />{won} vendas</span>
          </span>
        }
        actions={
          <>
            <label className="relative">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
              <input className="input w-56 pl-8" placeholder="Filtrar cards…" value={filter} onChange={(e) => setFilter(e.target.value)} aria-label="Filtrar cards" />
            </label>
            {can("leads.create") && (
              <button className="btn-primary" onClick={() => setNewLead({ open: true })}>
                <Plus className="size-4" /> Novo lead
              </button>
            )}
          </>
        }
      />

      <DndContext sensors={sensors} onDragStart={onDragStart} onDragEnd={onDragEnd} onDragCancel={() => setDragging(null)}>
        <div className="flex flex-1 gap-3 overflow-x-auto px-4 py-4 sm:px-6 lg:px-8">
          {board.stages.map((stage) => (
            <Column
              key={stage.id}
              stage={stage}
              leads={visible(stage.leads)}
              canMove={canMove}
              onAdd={can("leads.create") && stage.kind === "Open" ? () => setNewLead({ open: true, stageId: stage.id }) : undefined}
            />
          ))}
        </div>
        <DragOverlay dropAnimation={{ duration: 180, easing: "cubic-bezier(0.2, 0, 0, 1)" }}>{dragging ? <Card lead={dragging} overlay /> : null}</DragOverlay>
      </DndContext>

      <Modal title="Motivo da perda" open={lostMove !== null} onClose={() => setLostMove(null)}>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (lostMove) move.mutate({ ...lostMove, reason: lostReason });
            setLostMove(null);
          }}
          className="space-y-4"
        >
          <p className="text-sm text-slate-600">
            Informe por que <strong>{lostMove?.lead.contactName}</strong> foi perdido. Essa informação alimenta os relatórios de perda.
          </p>
          <textarea className="input min-h-24" required maxLength={500} value={lostReason} onChange={(e) => setLostReason(e.target.value)} autoFocus />
          <div className="flex justify-end gap-2">
            <button type="button" className="btn-secondary" onClick={() => setLostMove(null)}>Cancelar</button>
            <button className="btn-danger" disabled={!lostReason.trim()}>Marcar como perdido</button>
          </div>
        </form>
      </Modal>

      <NewLeadDialog open={newLead.open} stageId={newLead.stageId} onClose={() => setNewLead({ open: false })} />
    </div>
  );
}

function Column({ stage, leads, canMove, onAdd }: { stage: BoardStage; leads: LeadCard[]; canMove: boolean; onAdd?: () => void }) {
  const { setNodeRef, isOver } = useDroppable({ id: stage.id, disabled: !canMove });
  const color = stage.color ?? "#94a3b8";
  return (
    <section
      ref={setNodeRef}
      aria-label={stage.name}
      className={`flex w-[284px] shrink-0 flex-col rounded-2xl border transition-colors ${isOver ? "border-brand-400 bg-brand-50/70" : "border-slate-200/70 bg-slate-100/70"}`}
    >
      <header className="flex items-center gap-2 px-3 pb-2 pt-3">
        <span className="size-2.5 rounded-full ring-4" style={{ background: color, ["--tw-ring-color" as string]: `${color}26` }} aria-hidden />
        <h2 className="flex-1 truncate font-sans text-[13px] font-semibold text-slate-800">{stage.name}</h2>
        <span className="rounded-md bg-white px-1.5 py-0.5 text-[11px] font-medium tabular-nums text-slate-600 shadow-card">{stage.total}</span>
        {onAdd && (
          <button onClick={onAdd} className="rounded-md p-1 text-slate-400 hover:bg-white hover:text-slate-700" aria-label={`Novo lead em ${stage.name}`} title="Novo lead nesta etapa">
            <Plus className="size-4" />
          </button>
        )}
      </header>
      <div className="mx-3 mb-2 h-0.5 rounded-full" style={{ background: `linear-gradient(90deg, ${color}, ${color}00)` }} aria-hidden />
      <div className="flex flex-1 flex-col gap-2 overflow-y-auto px-2 pb-3">
        {leads.map((lead) => (
          <DraggableCard key={lead.id} lead={lead} disabled={!canMove} />
        ))}
        {stage.total > stage.leads.length && (
          <Link href={`/leads?stageId=${stage.id}`} className="px-1.5 py-1 text-xs font-medium text-slate-500 hover:text-brand-700">
            + {stage.total - stage.leads.length} leads — ver lista
          </Link>
        )}
        {leads.length === 0 && (
          <div className="mx-1 flex flex-1 items-start justify-center rounded-xl border border-dashed border-slate-300/80 px-3 py-6 text-center text-xs text-slate-400">
            {stage.total === 0 ? "Arraste leads para cá" : "Nenhum card corresponde ao filtro"}
          </div>
        )}
      </div>
    </section>
  );
}

function DraggableCard({ lead, disabled }: { lead: LeadCard; disabled: boolean }) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({ id: lead.id, data: { lead }, disabled });
  return (
    <div ref={setNodeRef} {...listeners} {...attributes} className={`rounded-xl outline-none focus-visible:ring-2 focus-visible:ring-brand-500 ${isDragging ? "opacity-30" : ""}`}>
      <Card lead={lead} />
    </div>
  );
}

function Card({ lead, overlay }: { lead: LeadCard; overlay?: boolean }) {
  const followUpLate = lead.nextFollowUpAt && new Date(lead.nextFollowUpAt) < new Date();
  return (
    <article
      className={`group rounded-xl border bg-white p-3 text-sm transition ${
        overlay ? "rotate-[1.5deg] cursor-grabbing border-brand-300 shadow-pop" : "cursor-grab border-slate-200/80 shadow-card hover:border-slate-300 hover:shadow-md"
      }`}
    >
      <div className="flex items-start justify-between gap-2">
        <Link
          href={`/leads/${lead.id}`}
          className="font-medium leading-snug text-slate-900 hover:text-brand-700"
          onPointerDown={(e) => e.stopPropagation()}
        >
          {lead.contactName}
        </Link>
        {lead.source && <span className="chip shrink-0 bg-slate-100 text-slate-600">{lead.source}</span>}
      </div>
      {lead.interest && <p className="mt-1 line-clamp-2 text-xs leading-relaxed text-slate-500">{lead.interest}</p>}

      {lead.phone && (
        <p className="mt-2 flex items-center gap-1.5 text-xs text-slate-500">
          <Phone className="size-3.5 text-slate-400" /> {formatPhone(lead.phone)}
        </p>
      )}

      {lead.nextFollowUpAt && (
        <p className={`chip mt-2 ${followUpLate ? "bg-red-50 text-red-700" : "bg-amber-50 text-amber-800"}`}>
          <CalendarClock className="size-3" />
          {followUpLate ? "Atrasado · " : ""}
          {formatDateTime(lead.nextFollowUpAt)}
        </p>
      )}

      <div className="mt-3 flex items-center justify-between border-t border-slate-100 pt-2.5 text-[11px] text-slate-500">
        <span className="flex min-w-0 items-center gap-1.5">
          <Avatar name={lead.ownerName} size="xs" />
          <span className="truncate">{lead.ownerName}</span>
        </span>
        <span className="flex shrink-0 items-center gap-1" title="Tempo na etapa">
          <Clock3 className="size-3" /> {formatRelative(lead.stageEnteredAt)}
        </span>
      </div>
    </article>
  );
}

function BoardSkeleton() {
  return (
    <div className="flex h-dvh flex-col">
      <div className="h-[73px] border-b border-slate-200/80 bg-white" />
      <div className="flex gap-3 overflow-hidden px-4 py-4 sm:px-6 lg:px-8">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="w-[284px] shrink-0 animate-pulse space-y-2 rounded-2xl bg-slate-100 p-3">
            <div className="h-4 w-24 rounded bg-slate-200" />
            {Array.from({ length: 3 - (i % 2) }).map((__, j) => <div key={j} className="h-24 rounded-xl bg-white" />)}
          </div>
        ))}
      </div>
    </div>
  );
}
