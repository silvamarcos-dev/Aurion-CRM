"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Check, Clock3, UserRound, X } from "lucide-react";
import Link from "next/link";
import { useCan, useToast } from "@/components/providers";
import { taskKindLabels } from "@/components/task-dialog";
import { Avatar } from "@/components/ui/avatar";
import { api, ApiError } from "@/lib/api";
import { formatDateTime } from "@/lib/format";
import type { TaskItem } from "@/lib/types";

export function TaskRow({ task, hideLead }: { task: TaskItem; hideLead?: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const can = useCan();
  const overdue = task.status === "Open" && new Date(task.dueAt) < new Date();

  const action = useMutation({
    mutationFn: (path: "complete" | "reopen" | "cancel") => api.post(`tasks/${task.id}/${path}`),
    onSuccess: () => {
      for (const key of ["tasks", "lead-tasks", "lead", "board", "lead-history"]) queryClient.invalidateQueries({ queryKey: [key] });
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao atualizar a tarefa.", "error"),
  });

  const done = task.status === "Done";
  const disabled = !can("tasks.manage") || task.status === "Cancelled" || action.isPending;

  return (
    <li className={`card group flex items-start gap-3 p-3.5 transition hover:shadow-md ${task.status === "Cancelled" ? "opacity-60" : ""}`}>
      <button
        type="button"
        role="checkbox"
        aria-checked={done}
        aria-label={done ? "Reabrir tarefa" : "Concluir tarefa"}
        disabled={disabled}
        onClick={() => action.mutate(done ? "reopen" : "complete")}
        className={`mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full border-2 transition disabled:cursor-not-allowed ${
          done ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 text-transparent hover:border-brand-500 hover:text-brand-500"
        }`}
      >
        <Check className="size-3" strokeWidth={3} />
      </button>
      <div className="min-w-0 flex-1">
        <p className={`text-sm font-medium ${task.status !== "Open" ? "text-slate-400 line-through" : "text-slate-900"}`}>{task.title}</p>
        <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-500">
          <span className={`flex items-center gap-1 ${overdue ? "font-medium text-red-600" : ""}`}>
            <Clock3 className="size-3.5" /> {overdue ? "Atrasada · " : ""}{formatDateTime(task.dueAt)}
          </span>
          <span className="chip bg-slate-100 text-slate-600">{taskKindLabels[task.kind]}</span>
          {!hideLead && task.leadId && (
            <Link href={`/leads/${task.leadId}`} className="flex items-center gap-1 font-medium text-brand-700 hover:underline">
              <UserRound className="size-3.5" /> {task.leadName}
            </Link>
          )}
          <span className="flex items-center gap-1"><Avatar name={task.assigneeName} size="xs" /> {task.assigneeName}</span>
        </div>
        {task.notes && <p className="mt-2 whitespace-pre-wrap rounded-lg bg-slate-50 px-2.5 py-2 text-xs text-slate-600">{task.notes}</p>}
      </div>
      {task.status === "Open" && can("tasks.manage") && (
        <button className="rounded-md p-1 text-slate-300 opacity-0 transition hover:bg-red-50 hover:text-red-600 group-hover:opacity-100 focus:opacity-100" onClick={() => action.mutate("cancel")} aria-label="Cancelar tarefa" title="Cancelar tarefa">
          <X className="size-4" />
        </button>
      )}
    </li>
  );
}
