"use client";

import { DndContext, DragOverlay, PointerSensor, KeyboardSensor, useDraggable, useDroppable, useSensor, useSensors, type DragEndEvent, type DragStartEvent } from "@dnd-kit/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { Modal } from "@/components/modal";
import { NewLeadDialog } from "@/components/new-lead-dialog";
import { useCan, useToast } from "@/components/providers";
import { api, ApiError } from "@/lib/api";
import { formatPhone, formatRelative } from "@/lib/format";
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
  const [newLeadOpen, setNewLeadOpen] = useState(false);

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

  if (isLoading) return <div className="p-6 text-sm text-slate-500">Carregando funil…</div>;
  if (error || !board) return <div className="p-6 text-sm text-red-600">Não foi possível carregar o funil.</div>;

  const canMove = can("pipeline.move");

  return (
    <div className="flex h-[calc(100dvh-49px)] flex-col md:h-dvh">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-white px-6 py-4">
        <div>
          <h1 className="text-lg font-semibold">{board.pipelineName}</h1>
          <p className="text-xs text-slate-500">{board.stages.reduce((n, s) => n + s.total, 0)} leads visíveis para você</p>
        </div>
        {can("leads.create") && (
          <button className="btn-primary" onClick={() => setNewLeadOpen(true)}>
            + Novo lead
          </button>
        )}
      </div>

      <DndContext sensors={sensors} onDragStart={onDragStart} onDragEnd={onDragEnd} onDragCancel={() => setDragging(null)}>
        <div className="flex flex-1 gap-3 overflow-x-auto p-4">
          {board.stages.map((stage) => (
            <Column key={stage.id} stage={stage} canMove={canMove} />
          ))}
        </div>
        <DragOverlay>{dragging ? <Card lead={dragging} overlay /> : null}</DragOverlay>
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

      <NewLeadDialog open={newLeadOpen} onClose={() => setNewLeadOpen(false)} />
    </div>
  );
}

function Column({ stage, canMove }: { stage: BoardStage; canMove: boolean }) {
  const { setNodeRef, isOver } = useDroppable({ id: stage.id, disabled: !canMove });
  return (
    <section
      ref={setNodeRef}
      aria-label={stage.name}
      className={`flex w-72 shrink-0 flex-col rounded-xl border bg-slate-100/70 transition ${isOver ? "border-emerald-500 bg-emerald-50/60" : "border-slate-200"}`}
    >
      <header className="flex items-center gap-2 px-3 pb-2 pt-3">
        <span className="size-2.5 rounded-full" style={{ background: stage.color ?? "#94a3b8" }} aria-hidden />
        <h2 className="flex-1 truncate text-sm font-medium">{stage.name}</h2>
        <span className="rounded-full bg-white px-2 py-0.5 text-xs text-slate-600 ring-1 ring-slate-200">{stage.total}</span>
      </header>
      <div className="flex flex-1 flex-col gap-2 overflow-y-auto px-2 pb-3">
        {stage.leads.map((lead) => (
          <DraggableCard key={lead.id} lead={lead} disabled={!canMove} />
        ))}
        {stage.total > stage.leads.length && (
          <Link href={`/leads?stageId=${stage.id}`} className="px-1 text-xs text-slate-500 hover:underline">
            + {stage.total - stage.leads.length} leads — ver lista
          </Link>
        )}
        {stage.total === 0 && <p className="px-1 py-4 text-center text-xs text-slate-400">Sem leads</p>}
      </div>
    </section>
  );
}

function DraggableCard({ lead, disabled }: { lead: LeadCard; disabled: boolean }) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({ id: lead.id, data: { lead }, disabled });
  return (
    <div ref={setNodeRef} {...listeners} {...attributes} className={isDragging ? "opacity-30" : ""}>
      <Card lead={lead} />
    </div>
  );
}

function Card({ lead, overlay }: { lead: LeadCard; overlay?: boolean }) {
  return (
    <article className={`rounded-lg border border-slate-200 bg-white p-3 text-sm shadow-xs ${overlay ? "rotate-2 shadow-lg" : "hover:border-slate-300"}`}>
      <Link href={`/leads/${lead.id}`} className="font-medium text-slate-900 hover:text-emerald-800" onPointerDown={(e) => e.stopPropagation()}>
        {lead.contactName}
      </Link>
      {lead.interest && <p className="mt-0.5 line-clamp-2 text-xs text-slate-600">{lead.interest}</p>}
      <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-[11px] text-slate-500">
        <span>{formatPhone(lead.phone)}</span>
        {lead.source && <span className="rounded bg-slate-100 px-1.5 py-0.5">{lead.source}</span>}
      </div>
      <div className="mt-2 flex items-center justify-between text-[11px] text-slate-500">
        <span className="truncate">{lead.ownerName}</span>
        <span title="Tempo na etapa">{formatRelative(lead.stageEnteredAt)}</span>
      </div>
    </article>
  );
}
