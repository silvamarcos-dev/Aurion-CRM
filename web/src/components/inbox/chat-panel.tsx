"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertCircle, ArrowLeft, CalendarPlus, Check, CheckCheck, Clock, Clock4, EyeOff, FileText, FlaskConical, ListTodo, Lock, RotateCcw, Send, Zap } from "lucide-react";
import Link from "next/link";
import { Fragment, useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { AppointmentDialog } from "@/components/appointment-dialog";
import { SimulateDialog, TemplateDialog } from "@/components/inbox/inbox-dialogs";
import { useCan, useMe, useToast } from "@/components/providers";
import { TaskDialog } from "@/components/task-dialog";
import { Avatar } from "@/components/ui/avatar";
import { api, ApiError } from "@/lib/api";
import { formatDateTime, formatPhone } from "@/lib/format";
import { formatDayLabel, todayInZone, zonedDate, zonedTime } from "@/lib/time";
import type { ChatMessage, ConversationDetail, LeadDetail, Macro, RenderResult, UserSummary } from "@/lib/types";

const newClientId = () => (crypto.randomUUID?.() ?? `${Date.now()}-${Math.random()}`).replaceAll("-", "");

export function ChatPanel({ conversationId, onBack }: { conversationId: string; onBack: () => void }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const can = useCan();
  const { data: me } = useMe();
  const [text, setText] = useState("");
  const [templatesOpen, setTemplatesOpen] = useState(false);
  const [simulateOpen, setSimulateOpen] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);
  const opened = useRef<string | null>(null);
  const key = ["conversations", "detail", conversationId];

  const { data, error } = useQuery({
    queryKey: key,
    queryFn: () => api.get<ConversationDetail>(`conversations/${conversationId}`),
    refetchInterval: 3_000,
  });

  // Abertura: registra o acesso e marca como lida — uma vez por conversa aberta.
  useEffect(() => {
    if (opened.current === conversationId) return;
    opened.current = conversationId;
    api.post(`conversations/${conversationId}/open`).then(() => queryClient.invalidateQueries({ queryKey: ["conversations", "list"] })).catch(() => undefined);
  }, [conversationId, queryClient]);

  const last = data?.messages.at(-1);
  const lastId = last?.id;

  // Mensagem nova do cliente com a conversa aberta: já está sendo lida.
  const canReply = data?.canReply;
  const unread = data?.conversation.unreadCount ?? 0;
  useEffect(() => {
    if (!canReply || unread === 0 || last?.direction !== "Inbound") return;
    api.post(`conversations/${conversationId}/read`).then(() => queryClient.invalidateQueries({ queryKey: ["conversations", "list"] })).catch(() => undefined);
    queryClient.invalidateQueries({ queryKey: ["conversations", "counters"] });
  }, [lastId, canReply, unread, last?.direction, conversationId, queryClient]);
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [lastId, conversationId]);

  const send = useMutation({
    mutationFn: (body: { text?: string; templateId?: string; templateParameters?: string[] }) =>
      api.post<ChatMessage>(`conversations/${conversationId}/messages`, { clientMessageId: newClientId(), ...body }),
    onSuccess: (message) => {
      queryClient.setQueryData<ConversationDetail>(key, (d) => d && { ...d, messages: d.messages.some((m) => m.id === message.id) ? d.messages : [...d.messages, message] });
      queryClient.invalidateQueries({ queryKey: ["conversations"] });
      if (message.status === "Failed") toast(message.failureReason ?? "A mensagem não foi entregue ao WhatsApp.", "error");
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Não foi possível enviar.", "error"),
  });

  const retry = useMutation({
    mutationFn: (messageId: string) => api.post<ChatMessage>(`conversations/${conversationId}/messages/${messageId}/retry`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key }),
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao reenviar.", "error"),
  });

  const lifecycle = useMutation({
    mutationFn: (action: "close" | "reopen") => api.post(`conversations/${conversationId}/${action}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["conversations"] }),
    onError: (err) => toast(err instanceof ApiError ? err.message : "Operação não concluída.", "error"),
  });

  if (error) return <div className="flex h-full items-center justify-center p-8 text-sm text-slate-500">Conversa não encontrada ou sem permissão.</div>;
  if (!data) return <div className="h-full animate-pulse bg-slate-50" />;

  const c = data.conversation;
  const expired = !data.withinWindow;

  function submitText() {
    const value = text.trim();
    if (!value || send.isPending) return;
    send.mutate({ text: value }, { onSuccess: () => setText("") });
  }

  return (
    <div className="flex h-full min-w-0 flex-col">
      {/* Cabeçalho */}
      <header className="flex items-center gap-3 border-b border-slate-200/80 bg-white px-4 py-3">
        <button className="rounded-md p-1 text-slate-500 hover:bg-slate-100 lg:hidden" onClick={onBack} aria-label="Voltar para a lista"><ArrowLeft className="size-5" /></button>
        <Avatar name={c.contactName} size="md" />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold text-slate-900">{c.contactName}</p>
          <p className="truncate text-xs text-slate-500">
            {formatPhone(c.contactAddress)} · {c.channelName}
            {c.provider === "Simulator" && <span className="ml-1.5 inline-flex items-center gap-0.5 text-violet-600"><FlaskConical className="size-3" />simulado</span>}
          </p>
        </div>
        <OwnerControl conversationId={conversationId} ownerUserId={c.ownerUserId} ownerName={c.ownerName} />
        {c.provider === "Simulator" && can("channels.simulate") && (
          <button className="btn-secondary px-2.5 py-1.5 text-xs text-violet-700" onClick={() => setSimulateOpen(true)} title="Simular uma resposta do cliente">
            <FlaskConical className="size-3.5" /> <span className="hidden sm:inline">Cliente responde</span>
          </button>
        )}
        {data.canReply && (
          c.state === "Open"
            ? <button className="btn-ghost px-2.5 py-1.5 text-xs" onClick={() => lifecycle.mutate("close")}>Encerrar</button>
            : <button className="btn-ghost px-2.5 py-1.5 text-xs" onClick={() => lifecycle.mutate("reopen")}>Reabrir</button>
        )}
      </header>

      {data.contentHidden && (
        <div className="flex items-center gap-2 border-b border-sky-200 bg-sky-50 px-4 py-2 text-xs text-sky-900">
          <EyeOff className="size-3.5" /> O conteúdo das mensagens fica oculto no modo visitante (dados pessoais do cliente).
        </div>
      )}

      {/* Mensagens */}
      <div className="flex-1 overflow-y-auto bg-[#f4f2ee] bg-[radial-gradient(#e7e3dc_1px,transparent_1px)] [background-size:18px_18px] px-3 py-4 sm:px-6">
        <MessageList messages={data.messages} meId={me?.userId} onRetry={(id) => retry.mutate(id)} canReply={data.canReply} />
        <div ref={bottomRef} />
      </div>

      {/* Composer */}
      {data.canReply ? (
        <div className="border-t border-slate-200/80 bg-white p-3">
          {expired ? (
            <div className="flex flex-wrap items-center gap-3 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2.5 text-sm text-amber-900">
              <Clock4 className="size-4 shrink-0" />
              <span className="flex-1">Janela de 24h encerrada: o cliente não escreveu nas últimas 24 horas. Para retomar, envie um <strong>template aprovado</strong>.</span>
              <button className="btn-primary py-1.5" onClick={() => setTemplatesOpen(true)}><FileText className="size-4" /> Usar template</button>
            </div>
          ) : (
            <Composer
              leadId={c.leadId}
              value={text}
              onChange={setText}
              onSubmit={submitText}
              sending={send.isPending}
              onOpenTemplates={() => setTemplatesOpen(true)}
              windowEndsAt={c.windowEndsAt}
            />
          )}
        </div>
      ) : (
        <div className="flex items-center justify-center gap-2 border-t border-slate-200/80 bg-white p-3 text-xs text-slate-500">
          <Lock className="size-3.5" /> Somente leitura — você não pode responder esta conversa.
        </div>
      )}

      <TemplateDialog open={templatesOpen} onClose={() => setTemplatesOpen(false)} templates={data.templates} sending={send.isPending}
        onSend={(templateId, templateParameters) => send.mutate({ templateId, templateParameters }, { onSuccess: () => setTemplatesOpen(false) })} />
      <SimulateDialog open={simulateOpen} onClose={() => setSimulateOpen(false)} defaults={{ channelId: c.channelId, phone: c.contactAddress, name: c.contactName }} />
    </div>
  );
}

function MessageList({ messages, meId, onRetry, canReply }: { messages: ChatMessage[]; meId?: string; onRetry: (id: string) => void; canReply: boolean }) {
  const today = todayInZone();
  let lastDay = "";
  return (
    <ol className="mx-auto flex max-w-3xl flex-col gap-1.5">
      {messages.map((m) => {
        const day = zonedDate(m.occurredAt);
        const showDay = day !== lastDay;
        lastDay = day;
        const out = m.direction === "Outbound";
        return (
          <Fragment key={m.id}>
            {showDay && (
              <li className="my-3 self-center rounded-full bg-white/90 px-3 py-1 text-[11px] font-medium text-slate-500 shadow-card">
                {day === today ? "Hoje" : formatDayLabel(day)}
              </li>
            )}
            <li className={`flex ${out ? "justify-end" : "justify-start"}`}>
              <div className={`max-w-[85%] rounded-2xl px-3 py-2 text-sm shadow-card sm:max-w-[70%] ${
                out ? "rounded-br-md bg-[#d9fdd3] text-slate-900" : "rounded-bl-md bg-white text-slate-900"
              } ${m.status === "Failed" ? "ring-1 ring-red-300" : ""}`}>
                {out && m.authorKind !== "User" && <p className="mb-0.5 text-[11px] font-semibold text-violet-700">{m.authorKind === "Copilot" ? "Copilot" : "Sistema"}</p>}
                {out && m.authorKind === "User" && m.authorUserId !== meId && m.authorName && <p className="mb-0.5 text-[11px] font-semibold text-brand-700">{m.authorName}</p>}
                {m.type === "Template" && <p className="mb-1 flex items-center gap-1 text-[10px] font-medium uppercase tracking-wide text-slate-500"><FileText className="size-3" />template {m.templateName}</p>}
                {m.type !== "Text" && m.type !== "Template" && !m.body && <p className="italic text-slate-500">[{m.type}]</p>}
                <p className="whitespace-pre-wrap break-words leading-relaxed">{m.body}</p>
                <p className="mt-0.5 flex items-center justify-end gap-1 text-[10px] text-slate-500">
                  {zonedTime(m.occurredAt)}
                  {out && <StatusIcon message={m} />}
                </p>
                {m.status === "Failed" && (
                  <p className="mt-1 flex items-center gap-1.5 border-t border-red-100 pt-1 text-[11px] text-red-700">
                    <AlertCircle className="size-3.5" /> {m.failureReason ?? "Não entregue."}
                    {canReply && <button className="ml-auto flex items-center gap-1 font-medium underline-offset-2 hover:underline" onClick={() => onRetry(m.id)}><RotateCcw className="size-3" />Reenviar</button>}
                  </p>
                )}
              </div>
            </li>
          </Fragment>
        );
      })}
      {messages.length === 0 && <li className="self-center py-10 text-sm text-slate-500">Nenhuma mensagem ainda.</li>}
    </ol>
  );
}

function StatusIcon({ message }: { message: ChatMessage }) {
  switch (message.status) {
    case "Queued": return <Clock className="size-3" aria-label="Enviando" />;
    case "Sent": return <Check className="size-3.5" aria-label="Enviada" />;
    case "Delivered": return <CheckCheck className="size-3.5" aria-label="Entregue" />;
    case "Read": return <CheckCheck className="size-3.5 text-sky-500" aria-label={`Lida ${message.readAt ? formatDateTime(message.readAt) : ""}`} />;
    case "Failed": return <AlertCircle className="size-3.5 text-red-600" aria-label="Falhou" />;
    default: return null;
  }
}

/** Campo de mensagem com macros: digite "/" e escolha; o texto vem renderizado para o lead (variáveis preenchidas). */
function Composer({ leadId, value, onChange, onSubmit, sending, onOpenTemplates, windowEndsAt }: {
  leadId: string;
  value: string;
  onChange: (v: string) => void;
  onSubmit: () => void;
  sending: boolean;
  onOpenTemplates: () => void;
  windowEndsAt: string | null;
}) {
  const toast = useToast();
  const can = useCan();
  const [highlight, setHighlight] = useState(0);
  const ref = useRef<HTMLTextAreaElement>(null);
  const { data: macros } = useQuery({ queryKey: ["macros"], queryFn: () => api.get<Macro[]>("macros"), enabled: can("macros.use"), staleTime: 60_000 });

  // Ajusta a altura também quando o texto muda por código (macro aplicada, envio que limpa o campo).
  useEffect(() => {
    const t = ref.current;
    if (!t) return;
    t.style.height = "auto";
    t.style.height = `${Math.min(t.scrollHeight, 160)}px`;
  }, [value]);

  const query = value.startsWith("/") && !value.includes(" ") && !value.includes("\n") ? value.toLowerCase() : null;
  const matches = useMemo(() => (query ? (macros ?? []).filter((m) => m.isActive && m.shortcut.startsWith(query)).slice(0, 6) : []), [macros, query]);

  const apply = useMutation({
    mutationFn: (macro: Macro) => api.post<RenderResult>(`macros/${macro.id}/render`, { leadId }),
    onSuccess: (r) => {
      onChange(r.text);
      if (r.missingVariables.length) toast(`Sem valor para: ${r.missingVariables.join(", ")}. Revise antes de enviar.`, "info");
      requestAnimationFrame(() => ref.current?.focus());
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao usar a macro.", "error"),
  });

  function onKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (matches.length) {
      if (e.key === "ArrowDown") { e.preventDefault(); setHighlight((h) => (h + 1) % matches.length); return; }
      if (e.key === "ArrowUp") { e.preventDefault(); setHighlight((h) => (h - 1 + matches.length) % matches.length); return; }
      if (e.key === "Enter" || e.key === "Tab") { e.preventDefault(); apply.mutate(matches[Math.min(highlight, matches.length - 1)]); return; }
    }
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      onSubmit();
    }
  }

  const hoursLeft = windowEndsAt ? Math.max(0, Math.round((new Date(windowEndsAt).getTime() - Date.now()) / 3_600_000)) : 0;

  return (
    <div className="relative">
      {matches.length > 0 && (
        <ul className="absolute bottom-full left-0 mb-2 w-full max-w-md overflow-hidden rounded-xl border border-slate-200 bg-white shadow-pop" role="listbox">
          {matches.map((m, i) => (
            <li key={m.id}>
              <button type="button" role="option" aria-selected={i === highlight} onMouseDown={(e) => { e.preventDefault(); apply.mutate(m); }}
                className={`flex w-full items-start gap-2 px-3 py-2 text-left text-sm ${i === highlight ? "bg-brand-50" : "hover:bg-slate-50"}`}>
                <code className="shrink-0 rounded bg-brand-50 px-1.5 text-xs font-medium text-brand-800">{m.shortcut}</code>
                <span className="min-w-0"><span className="block font-medium">{m.title}</span><span className="block truncate text-xs text-slate-500">{m.body}</span></span>
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="flex items-end gap-2">
        <button type="button" className="btn-ghost shrink-0 px-2" onClick={onOpenTemplates} title="Enviar template aprovado"><FileText className="size-5" /></button>
        <textarea
          ref={ref}
          rows={1}
          className="input max-h-40 min-h-[42px] resize-none py-2.5"
          placeholder='Escreva uma mensagem… (digite "/" para macros)'
          value={value}
          maxLength={4096}
          onChange={(e) => { onChange(e.target.value); setHighlight(0); }}
          onKeyDown={onKeyDown}
          aria-label="Mensagem"
        />
        <button className="btn-primary shrink-0 px-3 py-2.5" onClick={onSubmit} disabled={sending || !value.trim()} aria-label="Enviar"><Send className="size-4" /></button>
      </div>
      <p className="mt-1.5 flex items-center gap-3 pl-11 text-[11px] text-slate-400">
        <span className="flex items-center gap-1"><Zap className="size-3" />Enter envia · Shift+Enter quebra linha</span>
        <span className="flex items-center gap-1"><Clock4 className="size-3" />janela de 24h: ~{hoursLeft}h restantes</span>
      </p>
    </div>
  );
}

function OwnerControl({ conversationId, ownerUserId, ownerName }: { conversationId: string; ownerUserId: string | null; ownerName: string | null }) {
  const can = useCan();
  const toast = useToast();
  const queryClient = useQueryClient();
  const canAssign = can("conversations.assign");
  const { data: users } = useQuery({
    queryKey: ["users-in-scope", "conversations.assign"],
    queryFn: () => api.get<UserSummary[]>("users/in-scope?capability=conversations.assign"),
    enabled: canAssign,
  });
  const assign = useMutation({
    mutationFn: (userId: string) => api.post(`conversations/${conversationId}/assign`, { userId }),
    onSuccess: () => { toast("Conversa atribuída.", "success"); queryClient.invalidateQueries({ queryKey: ["conversations"] }); },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao atribuir.", "error"),
  });

  if (!canAssign) return ownerName ? <span className="hidden text-xs text-slate-500 md:inline">{ownerName}</span> : null;
  return (
    <select className="input hidden w-auto py-1.5 text-xs md:block" value={ownerUserId ?? ""} onChange={(e) => e.target.value && assign.mutate(e.target.value)} aria-label="Responsável pela conversa">
      {!ownerUserId && <option value="">Sem dono</option>}
      {ownerUserId && !users?.some((u) => u.userId === ownerUserId) && <option value={ownerUserId}>{ownerName}</option>}
      {users?.map((u) => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}
    </select>
  );
}

export function LeadSidePanel({ leadId }: { leadId: string }) {
  const can = useCan();
  const [visitOpen, setVisitOpen] = useState(false);
  const [taskOpen, setTaskOpen] = useState(false);
  const { data: lead } = useQuery({ queryKey: ["lead", leadId], queryFn: () => api.get<LeadDetail>(`leads/${leadId}`), staleTime: 60_000 });

  if (!lead) return <div className="m-4 h-40 animate-pulse rounded-xl bg-slate-100" />;
  return (
    <div className="space-y-4 p-4 text-sm">
      <div className="flex items-center gap-3">
        <Avatar name={lead.fullName} size="lg" />
        <div className="min-w-0">
          <p className="truncate font-semibold text-slate-900">{lead.fullName}</p>
          <p className="truncate text-xs text-slate-500">{lead.stageName} · {lead.ownerName}</p>
        </div>
      </div>
      <dl className="grid grid-cols-[96px_1fr] gap-y-1.5 text-xs">
        <dt className="text-slate-500">Interesse</dt><dd className="text-slate-800">{lead.interest ?? "—"}</dd>
        <dt className="text-slate-500">Origem</dt><dd className="text-slate-800">{lead.source ?? "—"}</dd>
        <dt className="text-slate-500">Follow-up</dt>
        <dd className={lead.nextFollowUpAt && new Date(lead.nextFollowUpAt) < new Date() ? "font-medium text-red-600" : "text-slate-800"}>{formatDateTime(lead.nextFollowUpAt)}</dd>
        <dt className="text-slate-500">Último contato</dt><dd className="text-slate-800">{formatDateTime(lead.lastContactAt)}</dd>
      </dl>
      <div className="grid gap-2">
        <Link href={`/leads/${lead.id}`} className="btn-secondary text-xs">Abrir ficha do lead</Link>
        {can("calendar.manage") && <button className="btn-secondary text-xs" onClick={() => setVisitOpen(true)}><CalendarPlus className="size-3.5" /> Agendar visita</button>}
        {can("tasks.manage") && <button className="btn-secondary text-xs" onClick={() => setTaskOpen(true)}><ListTodo className="size-3.5" /> Criar follow-up</button>}
      </div>
      <AppointmentDialog open={visitOpen} onClose={() => setVisitOpen(false)} lead={{ id: lead.id, name: lead.fullName }} />
      <TaskDialog open={taskOpen} onClose={() => setTaskOpen(false)} lead={{ id: lead.id, name: lead.fullName }} />
    </div>
  );
}
