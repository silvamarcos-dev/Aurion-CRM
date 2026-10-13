"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Copy, FlaskConical, KeyRound, Plus, Smartphone, Trash2, TriangleAlert } from "lucide-react";
import { useState, type FormEvent } from "react";
import { Modal } from "@/components/modal";
import { useToast } from "@/components/providers";
import { EmptyState } from "@/components/ui/page-header";
import { api, ApiError } from "@/lib/api";
import { formatPhone } from "@/lib/format";
import type { Channel, ChannelProvider, MessageTemplate, UserSummary, WebhookSetup } from "@/lib/types";

/** Canais de WhatsApp (simulado ou oficial da Meta), templates e instruções do webhook. */
export function ChannelsPanel() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { data: channels } = useQuery({ queryKey: ["channels"], queryFn: () => api.get<Channel[]>("channels") });
  const { data: setup } = useQuery({ queryKey: ["channels", "webhook-setup"], queryFn: () => api.get<WebhookSetup>("channels/webhook-setup") });
  const [editing, setEditing] = useState<Channel | ChannelProvider | null>(null);
  const [templatesOf, setTemplatesOf] = useState<Channel | null>(null);

  const toggle = useMutation({
    mutationFn: (c: Channel) => api.post(`channels/${c.id}/${c.status === "Active" ? "disable" : "enable"}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["channels"] }),
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha.", "error"),
  });

  const callbackUrl = typeof window !== "undefined" && setup ? `${window.location.origin}${setup.callbackPath}` : "";

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="section-title">Canais de WhatsApp</h2>
        <div className="flex gap-2">
          <button className="btn-secondary text-violet-700" onClick={() => setEditing("Simulator")}><FlaskConical className="size-4" /> Canal simulado</button>
          <button className="btn-primary" onClick={() => setEditing("WhatsAppCloud")}><Smartphone className="size-4" /> WhatsApp oficial (Meta)</button>
        </div>
      </div>

      {channels?.length === 0 && (
        <EmptyState icon={<Smartphone className="size-5" />} title="Nenhum canal configurado"
          description="Comece com um canal simulado para treinar a equipe; conecte o número oficial quando a conta da Meta estiver aprovada." />
      )}

      <ul className="space-y-2">
        {channels?.map((c) => (
          <li key={c.id} className="flex flex-wrap items-center gap-3 rounded-xl border border-slate-200 bg-white p-4">
            <span className={`flex size-10 items-center justify-center rounded-xl ${c.provider === "Simulator" ? "bg-violet-50 text-violet-700" : "bg-brand-50 text-brand-700"}`}>
              {c.provider === "Simulator" ? <FlaskConical className="size-5" /> : <Smartphone className="size-5" />}
            </span>
            <span className="min-w-0 flex-1">
              <span className="block font-medium text-slate-900">{c.name} <span className="font-normal text-slate-500">· {formatPhone(c.phoneNumber)}</span></span>
              <span className="block text-xs text-slate-500">
                {c.provider === "Simulator" ? "Simulação (nada sai do CRM)" : `Cloud API · Phone Number ID ${c.phoneNumberId}`}
                {" · "}leads novos → {c.defaultOwnerName ?? "—"} · {c.templateCount} template(s)
                {c.provider === "WhatsAppCloud" && (c.hasCredentials ? " · token configurado" : " · SEM token")}
              </span>
            </span>
            <span className={`chip ring-1 ring-inset ${c.status === "Active" ? "bg-brand-50 text-brand-800 ring-brand-200" : "bg-slate-100 text-slate-600 ring-slate-200"}`}>{c.status === "Active" ? "Ativo" : "Desativado"}</span>
            <button className="btn-ghost px-2 py-1 text-xs" onClick={() => setTemplatesOf(c)}>Templates</button>
            <button className="btn-ghost px-2 py-1 text-xs" onClick={() => setEditing(c)}>Editar</button>
            <button className="btn-ghost px-2 py-1 text-xs" onClick={() => toggle.mutate(c)}>{c.status === "Active" ? "Desativar" : "Ativar"}</button>
          </li>
        ))}
      </ul>

      {setup && (
        <div className="rounded-xl border border-slate-200 bg-slate-50/60 p-4 text-sm">
          <h3 className="section-title mb-2 flex items-center gap-2"><KeyRound className="size-4 text-slate-400" /> Webhook da Meta (para o WhatsApp oficial)</h3>
          <ol className="list-decimal space-y-1.5 pl-5 text-slate-600">
            <li>
              No painel do app na Meta (WhatsApp › Configuração), use a URL de callback:
              <span className="mt-1 flex items-center gap-2">
                <code className="rounded bg-white px-2 py-1 text-xs ring-1 ring-slate-200">{callbackUrl}</code>
                <button className="btn-ghost px-1.5 py-1" onClick={() => navigator.clipboard.writeText(callbackUrl).then(() => toast("URL copiada.", "success"))} aria-label="Copiar URL"><Copy className="size-3.5" /></button>
              </span>
            </li>
            <li>Token de verificação e App Secret ficam <strong>no servidor</strong> (variáveis <code>WhatsApp__VerifyToken</code> e <code>WhatsApp__AppSecret</code>) — nunca na tela.</li>
            <li>Assine o campo <code>messages</code>.</li>
          </ol>
          <p className="mt-3 flex flex-wrap gap-4 text-xs">
            <Status ok={setup.verifyTokenConfigured} label="Token de verificação" />
            <Status ok={setup.appSecretConfigured} label="App Secret (assinatura)" />
          </p>
        </div>
      )}

      {editing && <ChannelEditor value={editing} onClose={() => setEditing(null)} />}
      {templatesOf && <TemplatesEditor channel={templatesOf} onClose={() => setTemplatesOf(null)} />}
    </div>
  );
}

function Status({ ok, label }: { ok: boolean; label: string }) {
  return ok
    ? <span className="flex items-center gap-1 text-brand-700"><CheckCircle2 className="size-3.5" /> {label} configurado</span>
    : <span className="flex items-center gap-1 text-amber-700"><TriangleAlert className="size-3.5" /> {label} não configurado</span>;
}

function ChannelEditor({ value, onClose }: { value: Channel | ChannelProvider; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const existing = typeof value === "string" ? null : value;
  const provider: ChannelProvider = existing?.provider ?? (value as ChannelProvider);
  const { data: users } = useQuery({ queryKey: ["users-in-scope", "channels.manage"], queryFn: () => api.get<UserSummary[]>("users/in-scope?capability=channels.manage") });
  const [form, setForm] = useState({
    name: existing?.name ?? (provider === "Simulator" ? "WhatsApp (simulado)" : "WhatsApp oficial"),
    phoneNumber: existing?.phoneNumber ?? "",
    phoneNumberId: existing?.phoneNumberId ?? "",
    defaultOwnerUserId: existing?.defaultOwnerUserId ?? "",
    accessToken: "",
  });

  const save = useMutation({
    mutationFn: () => {
      const body = { provider, ...form, phoneNumberId: form.phoneNumberId || null, accessToken: form.accessToken || null };
      return existing ? api.put(`channels/${existing.id}`, body) : api.post("channels", body);
    },
    onSuccess: () => { toast("Canal salvo.", "success"); queryClient.invalidateQueries({ queryKey: ["channels"] }); onClose(); },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao salvar.", "error"),
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    save.mutate();
  }

  return (
    <Modal title={existing ? "Editar canal" : provider === "Simulator" ? "Novo canal simulado" : "Conectar WhatsApp oficial"} open onClose={onClose}>
      <form onSubmit={submit} className="space-y-3">
        <div>
          <label className="label" htmlFor="ch-name">Nome</label>
          <input id="ch-name" className="input" required maxLength={80} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        </div>
        <div>
          <label className="label" htmlFor="ch-phone">Número do WhatsApp</label>
          <input id="ch-phone" className="input" required inputMode="tel" placeholder="(11) 90000-0000" value={form.phoneNumber} onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} />
        </div>
        {provider === "WhatsAppCloud" && (
          <>
            <div>
              <label className="label" htmlFor="ch-pnid">Phone Number ID (Meta)</label>
              <input id="ch-pnid" className="input font-mono" required maxLength={64} value={form.phoneNumberId} onChange={(e) => setForm({ ...form, phoneNumberId: e.target.value })} />
            </div>
            <div>
              <label className="label" htmlFor="ch-token">Token de acesso permanente {existing?.hasCredentials && "(deixe vazio para manter o atual)"}</label>
              <input id="ch-token" className="input font-mono" type="password" autoComplete="off" required={!existing?.hasCredentials} value={form.accessToken}
                onChange={(e) => setForm({ ...form, accessToken: e.target.value })} />
              <p className="mt-1 text-[11px] text-slate-500">Guardado cifrado no servidor e nunca exibido de novo.</p>
            </div>
          </>
        )}
        <div>
          <label className="label" htmlFor="ch-owner">Leads novos que chegarem por este canal vão para</label>
          <select id="ch-owner" className="input" required value={form.defaultOwnerUserId} onChange={(e) => setForm({ ...form, defaultOwnerUserId: e.target.value })}>
            <option value="">Selecione…</option>
            {users?.map((u) => <option key={u.userId} value={u.userId}>{u.displayName} — {u.role}</option>)}
          </select>
          <p className="mt-1 text-[11px] text-slate-500">A distribuição automática (rodízio, por empreendimento) vem com as Automações.</p>
        </div>
        <div className="flex justify-end gap-2">
          <button type="button" className="btn-secondary" onClick={onClose}>Cancelar</button>
          <button className="btn-primary" disabled={save.isPending}>Salvar</button>
        </div>
      </form>
    </Modal>
  );
}

function TemplatesEditor({ channel, onClose }: { channel: Channel; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const key = ["channels", channel.id, "templates"];
  const { data: templates } = useQuery({ queryKey: key, queryFn: () => api.get<MessageTemplate[]>(`channels/${channel.id}/templates`) });
  const [form, setForm] = useState({ name: "", language: "pt_BR", body: "" });
  const refresh = () => { queryClient.invalidateQueries({ queryKey: key }); queryClient.invalidateQueries({ queryKey: ["channels"] }); };

  const create = useMutation({
    mutationFn: () => api.post(`channels/${channel.id}/templates`, form),
    onSuccess: () => { setForm({ name: "", language: "pt_BR", body: "" }); refresh(); },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao salvar.", "error"),
  });
  const remove = useMutation({ mutationFn: (id: string) => api.del(`channels/${channel.id}/templates/${id}`), onSuccess: refresh });

  return (
    <Modal title={`Templates — ${channel.name}`} description="Cadastre aqui os templates JÁ APROVADOS na Meta, com o mesmo nome e texto ({{1}}, {{2}}…)." open onClose={onClose} size="lg">
      <ul className="mb-5 space-y-2">
        {templates?.map((t) => (
          <li key={t.id} className="flex items-start gap-3 rounded-lg border border-slate-200 p-3 text-sm">
            <span className="min-w-0 flex-1">
              <span className="block font-mono text-xs font-medium text-slate-800">{t.name} <span className="text-slate-400">({t.language} · {t.parameterCount} parâmetro(s))</span></span>
              <span className="mt-1 block whitespace-pre-wrap text-slate-600">{t.body}</span>
            </span>
            <button className="rounded p-1 text-slate-400 hover:bg-red-50 hover:text-red-600" onClick={() => remove.mutate(t.id)} aria-label={`Remover ${t.name}`}><Trash2 className="size-4" /></button>
          </li>
        ))}
        {templates?.length === 0 && <li className="text-sm text-slate-500">Nenhum template ainda.</li>}
      </ul>
      <form onSubmit={(e) => { e.preventDefault(); create.mutate(); }} className="space-y-3 rounded-xl bg-slate-50 p-4">
        <div className="grid grid-cols-[1fr_110px] gap-3">
          <div>
            <label className="label" htmlFor="tp-name">Nome (como na Meta)</label>
            <input id="tp-name" className="input font-mono" required pattern="[a-z0-9_]+" placeholder="retomar_contato" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </div>
          <div>
            <label className="label" htmlFor="tp-lang">Idioma</label>
            <input id="tp-lang" className="input" required maxLength={10} value={form.language} onChange={(e) => setForm({ ...form, language: e.target.value })} />
          </div>
        </div>
        <div>
          <label className="label" htmlFor="tp-body">Texto</label>
          <textarea id="tp-body" className="input min-h-20" required maxLength={1024} placeholder="Olá, {{1}}! Aqui é {{2}}…" value={form.body} onChange={(e) => setForm({ ...form, body: e.target.value })} />
        </div>
        <button className="btn-primary" disabled={create.isPending}><Plus className="size-4" /> Adicionar template</button>
      </form>
    </Modal>
  );
}
