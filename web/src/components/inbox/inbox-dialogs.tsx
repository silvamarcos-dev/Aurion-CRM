"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FlaskConical } from "lucide-react";
import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Modal } from "@/components/modal";
import { useToast } from "@/components/providers";
import { api, ApiError } from "@/lib/api";
import type { Channel, MessageTemplate } from "@/lib/types";

/** Escolha de template aprovado + parâmetros, com pré-visualização do texto final. */
export function TemplateDialog({ open, onClose, templates, onSend, sending }: {
  open: boolean;
  onClose: () => void;
  templates: MessageTemplate[];
  onSend: (templateId: string, parameters: string[]) => void;
  sending: boolean;
}) {
  const [templateId, setTemplateId] = useState("");
  const [params, setParams] = useState<string[]>([]);
  const template = templates.find((t) => t.id === templateId);

  useEffect(() => {
    if (!open) return;
    setTemplateId(templates[0]?.id ?? "");
  }, [open, templates]);
  useEffect(() => {
    setParams(Array.from({ length: template?.parameterCount ?? 0 }, () => ""));
  }, [template?.id, template?.parameterCount]);

  const preview = template ? params.reduce((text, p, i) => text.replaceAll(`{{${i + 1}}}`, p || `{{${i + 1}}}`), template.body) : "";

  function submit(e: FormEvent) {
    e.preventDefault();
    if (template) onSend(template.id, params);
  }

  return (
    <Modal title="Enviar template" description="Templates aprovados pela Meta são a única forma de falar com o cliente fora da janela de 24h." open={open} onClose={onClose}>
      {templates.length === 0 ? (
        <p className="text-sm text-slate-500">Nenhum template cadastrado neste canal. Um administrador pode cadastrar em Configurações › Canais.</p>
      ) : (
        <form onSubmit={submit} className="space-y-4">
          <div>
            <label className="label" htmlFor="tpl">Template</label>
            <select id="tpl" className="input" value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
              {templates.map((t) => <option key={t.id} value={t.id}>{t.name} ({t.language})</option>)}
            </select>
          </div>
          {params.map((value, i) => (
            <div key={i}>
              <label className="label" htmlFor={`tpl-p${i}`}>Parâmetro {`{{${i + 1}}}`}</label>
              <input id={`tpl-p${i}`} className="input" required maxLength={200} value={value} onChange={(e) => setParams(params.map((p, j) => (j === i ? e.target.value : p)))} />
            </div>
          ))}
          <div className="rounded-xl bg-[#e7f6ee] p-3 text-sm text-slate-800 whitespace-pre-wrap">{preview}</div>
          <div className="flex justify-end gap-2">
            <button type="button" className="btn-secondary" onClick={onClose}>Cancelar</button>
            <button className="btn-primary" disabled={sending || params.some((p) => !p.trim())}>Enviar template</button>
          </div>
        </form>
      )}
    </Modal>
  );
}

/** "Simular cliente": gera uma mensagem RECEBIDA num canal de simulação (mesmo caminho do WhatsApp real). */
export function SimulateDialog({ open, onClose, defaults, onSent }: {
  open: boolean;
  onClose: () => void;
  defaults?: { channelId: string; phone: string; name?: string };
  onSent?: (conversationId: string) => void;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { data: channels } = useQuery({ queryKey: ["channels"], queryFn: () => api.get<Channel[]>("channels"), enabled: open });
  const simulators = useMemo(() => channels?.filter((c) => c.provider === "Simulator" && c.status === "Active") ?? [], [channels]);
  const [form, setForm] = useState({ channelId: "", phone: "", name: "", text: "" });

  useEffect(() => {
    if (!open) return;
    setForm({ channelId: defaults?.channelId ?? "", phone: defaults?.phone ?? "", name: defaults?.name ?? "", text: "" });
  }, [open, defaults?.channelId, defaults?.phone, defaults?.name]);
  useEffect(() => {
    if (open && !form.channelId && simulators[0]) setForm((f) => ({ ...f, channelId: simulators[0].id }));
  }, [open, form.channelId, simulators]);

  const send = useMutation({
    mutationFn: () => api.post<{ conversationId: string; leadCreated: boolean }>("simulator/inbound", {
      channelId: form.channelId, fromPhone: form.phone, profileName: form.name || null, text: form.text,
    }),
    onSuccess: (r) => {
      toast(r.leadCreated ? "Mensagem recebida — lead novo criado no funil." : "Mensagem do cliente recebida.", "success");
      queryClient.invalidateQueries({ queryKey: ["conversations"] });
      queryClient.invalidateQueries({ queryKey: ["board"] });
      onSent?.(r.conversationId);
      onClose();
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao simular.", "error"),
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    send.mutate();
  }

  const fixed = Boolean(defaults);
  return (
    <Modal title="Simular cliente" description="Gera uma mensagem como se o cliente tivesse escrito no WhatsApp. Só funciona em canais de simulação." open={open} onClose={onClose}>
      {channels && simulators.length === 0 ? (
        <p className="text-sm text-slate-500">Nenhum canal de simulação ativo. Crie um em Configurações › Canais.</p>
      ) : (
        <form onSubmit={submit} className="space-y-3">
          {!fixed && (
            <>
              <div>
                <label className="label" htmlFor="sim-ch">Canal</label>
                <select id="sim-ch" className="input" value={form.channelId} onChange={(e) => setForm({ ...form, channelId: e.target.value })}>
                  {simulators.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="label" htmlFor="sim-phone">Telefone do cliente</label>
                  <input id="sim-phone" className="input" required inputMode="tel" placeholder="(11) 91234-5678" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
                </div>
                <div>
                  <label className="label" htmlFor="sim-name">Nome no WhatsApp</label>
                  <input id="sim-name" className="input" maxLength={80} placeholder="Opcional" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
                </div>
              </div>
            </>
          )}
          <div>
            <label className="label" htmlFor="sim-text">Mensagem do cliente</label>
            <textarea id="sim-text" className="input min-h-20" required maxLength={4000} autoFocus value={form.text} onChange={(e) => setForm({ ...form, text: e.target.value })}
              placeholder="Ex.: Oi! Ainda tem lote disponível perto da entrada?" />
          </div>
          <div className="flex items-center justify-between gap-2">
            <span className="flex items-center gap-1.5 text-xs text-violet-700"><FlaskConical className="size-3.5" /> Modo simulação</span>
            <div className="flex gap-2">
              <button type="button" className="btn-secondary" onClick={onClose}>Cancelar</button>
              <button className="btn-primary" disabled={send.isPending || !form.channelId}>Receber mensagem</button>
            </div>
          </div>
        </form>
      )}
    </Modal>
  );
}
