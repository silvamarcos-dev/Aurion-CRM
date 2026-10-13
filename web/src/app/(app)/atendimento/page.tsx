"use client";

import { useQuery } from "@tanstack/react-query";
import { FlaskConical, MessagesSquare } from "lucide-react";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { ChatPanel, LeadSidePanel } from "@/components/inbox/chat-panel";
import { ConversationList, type InboxFilter } from "@/components/inbox/conversation-list";
import { SimulateDialog } from "@/components/inbox/inbox-dialogs";
import { useCan, useMe } from "@/components/providers";
import { EmptyState } from "@/components/ui/page-header";
import { api } from "@/lib/api";
import type { ConversationDetail } from "@/lib/types";

function Inbox() {
  const router = useRouter();
  const params = useSearchParams();
  const can = useCan();
  const { data: me } = useMe();
  const selected = params.get("c");
  const [filter, setFilter] = useState<InboxFilter>(() => (me?.capabilities["conversations.read"] === "own" ? "Mine" : "All"));
  const [simulateOpen, setSimulateOpen] = useState(false);

  const select = (id: string | null) => router.replace(id ? `/atendimento?c=${id}` : "/atendimento", { scroll: false });

  // Lead da conversa selecionada (para o painel lateral) — reaproveita o cache do chat.
  const { data: detail } = useQuery({
    queryKey: ["conversations", "detail", selected],
    queryFn: () => api.get<ConversationDetail>(`conversations/${selected}`),
    enabled: Boolean(selected),
    refetchInterval: 3_000,
  });

  return (
    <div className="flex h-[calc(100dvh-60px)] flex-col md:h-dvh">
      <header className="flex items-center justify-between gap-3 border-b border-slate-200/80 bg-white px-4 py-3 sm:px-6">
        <div>
          <h1 className="text-xl font-semibold text-slate-900">Atendimento</h1>
          <p className="text-xs text-slate-500">Conversas de WhatsApp ligadas aos leads</p>
        </div>
        {can("channels.simulate") && (
          <button className="btn-secondary text-violet-700" onClick={() => setSimulateOpen(true)}>
            <FlaskConical className="size-4" /> Simular cliente
          </button>
        )}
      </header>

      <div className="grid min-h-0 flex-1 lg:grid-cols-[340px_minmax(0,1fr)] xl:grid-cols-[340px_minmax(0,1fr)_300px]">
        <aside className={`min-h-0 border-r border-slate-200/80 bg-white ${selected ? "hidden lg:block" : "block"}`}>
          <ConversationList selectedId={selected} onSelect={select} filter={filter} onFilterChange={setFilter} />
        </aside>

        <section className={`min-h-0 ${selected ? "block" : "hidden lg:block"}`}>
          {selected ? (
            <ChatPanel key={selected} conversationId={selected} onBack={() => select(null)} />
          ) : (
            <div className="flex h-full items-center justify-center bg-[#f4f2ee] p-8">
              <EmptyState icon={<MessagesSquare className="size-5" />} title="Selecione uma conversa"
                description={can("channels.simulate") ? 'Sem WhatsApp conectado ainda? Use "Simular cliente" para testar o atendimento.' : "As mensagens dos seus leads aparecem aqui."} />
            </div>
          )}
        </section>

        <aside className="hidden min-h-0 overflow-y-auto border-l border-slate-200/80 bg-white xl:block">
          {detail ? <LeadSidePanel key={detail.conversation.leadId} leadId={detail.conversation.leadId} /> : <p className="p-6 text-sm text-slate-400">Dados do lead aparecem aqui.</p>}
        </aside>
      </div>

      <SimulateDialog open={simulateOpen} onClose={() => setSimulateOpen(false)} onSent={(id) => select(id)} />
    </div>
  );
}

export default function AtendimentoPage() {
  return (
    <Suspense>
      <Inbox />
    </Suspense>
  );
}
