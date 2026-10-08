"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bug, Crosshair, Lightbulb, MessageSquare, MessageSquarePlus, Monitor, Sparkles, Type } from "lucide-react";
import Link from "next/link";
import { useState, type FormEvent } from "react";
import { Modal } from "@/components/modal";
import { useMe, useToast } from "@/components/providers";
import { Avatar } from "@/components/ui/avatar";
import { EmptyState, PageHeader, Tabs } from "@/components/ui/page-header";
import { api, ApiError } from "@/lib/api";
import { formatDateTime, formatRelative } from "@/lib/format";
import type { SuggestionDetail, SuggestionKind, SuggestionPriority, SuggestionStats, SuggestionStatus, SuggestionSummary } from "@/lib/types";

const statusMeta: Record<SuggestionStatus, { label: string; className: string }> = {
  New: { label: "Nova", className: "bg-sky-50 text-sky-800 ring-sky-200" },
  UnderReview: { label: "Em análise", className: "bg-amber-50 text-amber-800 ring-amber-200" },
  Planned: { label: "Planejada", className: "bg-violet-50 text-violet-800 ring-violet-200" },
  Done: { label: "Concluída", className: "bg-brand-50 text-brand-800 ring-brand-200" },
  Declined: { label: "Recusada", className: "bg-slate-100 text-slate-600 ring-slate-200" },
};

const kindMeta: Record<SuggestionKind, { label: string; icon: typeof Bug; className: string }> = {
  Improvement: { label: "Melhoria", icon: Sparkles, className: "text-brand-700 bg-brand-50" },
  Bug: { label: "Problema", icon: Bug, className: "text-red-700 bg-red-50" },
  Idea: { label: "Ideia", icon: Lightbulb, className: "text-amber-700 bg-amber-50" },
  Text: { label: "Texto", icon: Type, className: "text-sky-700 bg-sky-50" },
};

const priorityLabels: Record<SuggestionPriority, string> = { Low: "Baixa", Medium: "Média", High: "Alta" };

type Filter = "all" | SuggestionStatus;

export default function SuggestionsPage() {
  const { data: me } = useMe();
  const [filter, setFilter] = useState<Filter>("all");
  const [selected, setSelected] = useState<string | null>(null);
  const managesAll = me?.capabilities["suggestions.read"] === "org";

  const { data: stats } = useQuery({ queryKey: ["suggestions", "stats"], queryFn: () => api.get<SuggestionStats>("suggestions/stats") });
  const { data: items, isLoading } = useQuery({
    queryKey: ["suggestions", "list", filter],
    queryFn: () => api.get<SuggestionSummary[]>(`suggestions${filter === "all" ? "" : `?status=${filter}`}`),
  });

  const count = (s: SuggestionStatus) => stats?.[({ New: "new", UnderReview: "underReview", Planned: "planned", Done: "done", Declined: "declined" } as const)[s]] ?? 0;
  const badge = (n: number) => (n > 0 ? <span className="rounded-full bg-slate-100 px-1.5 text-[11px] font-semibold tabular-nums text-slate-600">{n}</span> : undefined);

  return (
    <div>
      <PageHeader
        title="Sugestões"
        description={managesAll ? "Tudo o que usuários, visitantes e desenvolvedores sugeriram para o sistema." : "As sugestões que você enviou e o andamento de cada uma."}
      >
        <Tabs
          value={filter}
          onChange={setFilter}
          items={[
            { key: "all", label: "Todas" },
            ...(["New", "UnderReview", "Planned", "Done", "Declined"] as SuggestionStatus[]).map((s) => ({ key: s, label: statusMeta[s].label, badge: badge(count(s)) })),
          ]}
        />
      </PageHeader>

      <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
        {isLoading && <div className="space-y-2">{[0, 1, 2].map((i) => <div key={i} className="h-20 animate-pulse rounded-xl bg-white" />)}</div>}

        {items && items.length === 0 && (
          <EmptyState
            icon={<MessageSquarePlus className="size-5" />}
            title="Nenhuma sugestão por aqui"
            description='Use o botão "Sugerir mudança", no canto inferior direito de qualquer tela, para enviar uma ideia, melhoria ou problema.'
          />
        )}

        <ul className="space-y-2">
          {items?.map((s) => {
            const kind = kindMeta[s.kind];
            const KindIcon = kind.icon;
            return (
              <li key={s.id}>
                <button onClick={() => setSelected(s.id)} className="card flex w-full items-start gap-4 p-4 text-left transition hover:shadow-md">
                  <span className={`flex size-9 shrink-0 items-center justify-center rounded-lg ${kind.className}`}><KindIcon className="size-4" /></span>
                  <span className="min-w-0 flex-1">
                    <span className="flex flex-wrap items-center gap-2">
                      <span className="font-medium text-slate-900">{s.title}</span>
                      <span className={`chip ring-1 ring-inset ${statusMeta[s.status].className}`}>{statusMeta[s.status].label}</span>
                      {s.priority && <span className="chip bg-slate-100 text-slate-600">Prioridade {priorityLabels[s.priority].toLowerCase()}</span>}
                    </span>
                    <span className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-500">
                      <span className="flex items-center gap-1.5"><Avatar name={s.authorName} size="xs" />{s.authorName} · {s.authorRole}</span>
                      <code className="rounded bg-slate-100 px-1 text-[11px] text-slate-600">{s.pagePath}</code>
                      <span>{formatRelative(s.createdAt)}</span>
                      {s.commentCount > 0 && <span className="flex items-center gap-1"><MessageSquare className="size-3.5" />{s.commentCount}</span>}
                    </span>
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      </div>

      {selected && <SuggestionDetailModal id={selected} onClose={() => setSelected(null)} />}
    </div>
  );
}

function SuggestionDetailModal({ id, onClose }: { id: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [comment, setComment] = useState("");
  const { data: s } = useQuery({ queryKey: ["suggestions", "detail", id], queryFn: () => api.get<SuggestionDetail>(`suggestions/${id}`) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["suggestions"] });
  const onError = (err: unknown) => toast(err instanceof ApiError ? err.message : "Operação não concluída.", "error");

  const triage = useMutation({
    mutationFn: (body: { status: SuggestionStatus; priority: SuggestionPriority | null }) => api.post(`suggestions/${id}/triage`, body),
    onSuccess: () => { toast("Sugestão atualizada.", "success"); refresh(); },
    onError,
  });
  const addComment = useMutation({
    mutationFn: () => api.post(`suggestions/${id}/comments`, { body: comment }),
    onSuccess: () => { setComment(""); refresh(); },
    onError,
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    if (comment.trim()) addComment.mutate();
  }

  const [human, technical] = (s?.elementDescriptor ?? "").split(" ⟶ ");

  return (
    <Modal title={s?.title ?? "Sugestão"} description={s ? `${kindMeta[s.kind].label} · enviada por ${s.authorName} (${s.authorRole}) em ${formatDateTime(s.createdAt)}` : undefined} open onClose={onClose} size="lg">
      {!s ? (
        <div className="h-40 animate-pulse rounded-xl bg-slate-100" />
      ) : (
        <div className="space-y-5">
          <p className="whitespace-pre-wrap text-sm leading-relaxed text-slate-700">{s.description}</p>

          <div className="grid gap-2 rounded-xl bg-slate-50 p-3 text-xs text-slate-600 sm:grid-cols-2">
            <span className="flex items-center gap-1.5">
              <Monitor className="size-3.5 text-slate-400" /> Página:{" "}
              <Link href={s.pagePath} className="font-mono text-brand-700 hover:underline">{s.pagePath}</Link>
              {s.viewport && <span className="text-slate-400">({s.viewport})</span>}
            </span>
            {s.pageTitle && <span>Título da tela: <strong className="font-medium">{s.pageTitle}</strong></span>}
            {human && (
              <span className="flex items-start gap-1.5 sm:col-span-2">
                <Crosshair className="mt-0.5 size-3.5 shrink-0 text-violet-500" />
                <span>Elemento: <strong className="font-medium">{human}</strong>
                  {technical && <code className="mt-1 block break-all text-[11px] text-slate-500">{technical}</code>}
                </span>
              </span>
            )}
          </div>

          {s.canTriage && (
            <div className="flex flex-wrap items-end gap-3 rounded-xl border border-slate-200 p-3">
              <div>
                <label className="label" htmlFor="sg-status">Status</label>
                <select id="sg-status" className="input w-auto" value={s.status} onChange={(e) => triage.mutate({ status: e.target.value as SuggestionStatus, priority: s.priority })}>
                  {Object.entries(statusMeta).map(([v, m]) => <option key={v} value={v}>{m.label}</option>)}
                </select>
              </div>
              <div>
                <label className="label" htmlFor="sg-priority">Prioridade</label>
                <select id="sg-priority" className="input w-auto" value={s.priority ?? ""} onChange={(e) => triage.mutate({ status: s.status, priority: (e.target.value || null) as SuggestionPriority | null })}>
                  <option value="">Sem prioridade</option>
                  {Object.entries(priorityLabels).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                </select>
              </div>
            </div>
          )}
          {!s.canTriage && (
            <p className="text-sm">Status: <span className={`chip ring-1 ring-inset ${statusMeta[s.status].className}`}>{statusMeta[s.status].label}</span></p>
          )}

          <div>
            <h3 className="section-title mb-3">Conversa</h3>
            <ul className="space-y-3">
              {s.comments.map((c) => (
                <li key={c.id} className="flex gap-3">
                  <Avatar name={c.authorName} size="sm" />
                  <div className="min-w-0 flex-1 rounded-xl rounded-tl-sm bg-slate-50 px-3 py-2">
                    <p className="text-xs text-slate-500"><strong className="font-medium text-slate-700">{c.authorName}</strong> · {formatDateTime(c.createdAt)}</p>
                    <p className="mt-0.5 whitespace-pre-wrap text-sm text-slate-700">{c.body}</p>
                  </div>
                </li>
              ))}
              {s.comments.length === 0 && <li className="text-sm text-slate-500">Nenhum comentário ainda.</li>}
            </ul>
            {s.canComment && (
              <form onSubmit={submit} className="mt-3 flex gap-2">
                <input className="input" placeholder="Escreva um comentário…" maxLength={2000} value={comment} onChange={(e) => setComment(e.target.value)} />
                <button className="btn-primary shrink-0" disabled={addComment.isPending || !comment.trim()}>Enviar</button>
              </form>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}
