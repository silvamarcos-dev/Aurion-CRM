"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { CircleCheckBig, Plus } from "lucide-react";
import { useCan } from "@/components/providers";
import { EmptyState, PageHeader, Tabs } from "@/components/ui/page-header";
import { TaskDialog } from "@/components/task-dialog";
import { TaskRow } from "@/components/task-row";
import { api } from "@/lib/api";
import type { TaskItem } from "@/lib/types";

const tabs = [
  { key: "Overdue", label: "Atrasadas", query: "status=Open&due=Overdue" },
  { key: "Today", label: "Hoje", query: "status=Open&due=Today" },
  { key: "Upcoming", label: "Próximas", query: "status=Open&due=Upcoming" },
  { key: "Done", label: "Concluídas", query: "status=Done&limit=50" },
] as const;

export default function TasksPage() {
  const can = useCan();
  const [tab, setTab] = useState<(typeof tabs)[number]["key"]>("Today");
  const [open, setOpen] = useState(false);
  const current = tabs.find((t) => t.key === tab)!;

  const counts = useQuery({
    queryKey: ["tasks", "counts"],
    queryFn: async () => {
      const [overdue, today] = await Promise.all([
        api.get<TaskItem[]>("tasks?status=Open&due=Overdue"),
        api.get<TaskItem[]>("tasks?status=Open&due=Today"),
      ]);
      return { Overdue: overdue.length, Today: today.length } as Record<string, number>;
    },
  });
  const { data: tasks, isLoading } = useQuery({ queryKey: ["tasks", tab], queryFn: () => api.get<TaskItem[]>(`tasks?${current.query}`) });

  return (
    <div>
      <PageHeader
        title="Tarefas"
        description="Follow-ups, ligações e pendências do dia a dia."
        actions={can("tasks.manage") && <button className="btn-primary" onClick={() => setOpen(true)}><Plus className="size-4" /> Nova tarefa</button>}
      >
        <Tabs
          value={tab}
          onChange={setTab}
          items={tabs.map((t) => ({
            key: t.key,
            label: t.label,
            badge: counts.data?.[t.key] ? (
              <span className={`rounded-full px-1.5 text-[11px] font-semibold tabular-nums ${t.key === "Overdue" ? "bg-red-100 text-red-700" : "bg-slate-100 text-slate-600"}`}>{counts.data[t.key]}</span>
            ) : undefined,
          }))}
        />
      </PageHeader>

      <div className="mx-auto max-w-4xl px-4 py-5 sm:px-6 lg:px-8">
        {isLoading && <div className="space-y-2">{[0, 1, 2].map((i) => <div key={i} className="h-16 animate-pulse rounded-xl bg-white" />)}</div>}
        <ul className="space-y-2">
          {tasks?.map((t) => <TaskRow key={t.id} task={t} />)}
        </ul>
        {tasks?.length === 0 && (
          <EmptyState
            icon={<CircleCheckBig className="size-5" />}
            title={tab === "Overdue" ? "Nenhuma tarefa atrasada" : tab === "Today" ? "Nada para hoje" : tab === "Done" ? "Nenhuma tarefa concluída" : "Nenhuma tarefa futura"}
            description={tab === "Overdue" ? "Tudo em dia. Bom trabalho!" : "Crie follow-ups a partir do lead para não perder o timing."}
          />
        )}
      </div>

      <TaskDialog open={open} onClose={() => setOpen(false)} />
    </div>
  );
}
