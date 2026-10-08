"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Code2, Copy, Eye, Link2, ShieldCheck, TriangleAlert } from "lucide-react";
import { useState, type FormEvent } from "react";
import { Modal } from "@/components/modal";
import { useToast } from "@/components/providers";
import { EmptyState } from "@/components/ui/page-header";
import { api, ApiError } from "@/lib/api";
import { formatDateTime, formatRelative } from "@/lib/format";
import type { AccessLink, CreatedAccessLink, GuestRole } from "@/lib/types";

const roleInfo: Record<GuestRole, { icon: typeof Eye; title: string; description: string }> = {
  Visitante: {
    icon: Eye,
    title: "Visitante",
    description: "Navega por todo o sistema em modo somente leitura. Ideal para apresentar o CRM.",
  },
  Desenvolvedor: {
    icon: Code2,
    title: "Desenvolvedor",
    description: "Somente leitura + painel técnico (ambiente, fila de eventos, auditoria, mapa da API) e gestão de sugestões.",
  },
};

const statusChip = {
  Active: "bg-brand-50 text-brand-800 ring-brand-200",
  Expired: "bg-slate-100 text-slate-600 ring-slate-200",
  Revoked: "bg-red-50 text-red-700 ring-red-200",
} as const;
const statusLabel = { Active: "Ativo", Expired: "Expirado", Revoked: "Revogado" } as const;

/** Gestão de links de acesso externo (visitante / desenvolvedor). */
export function AccessLinksPanel() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { data: links } = useQuery({ queryKey: ["access-links"], queryFn: () => api.get<AccessLink[]>("access-links") });
  const [form, setForm] = useState({ label: "", role: "Visitante" as GuestRole, expiresInDays: 7, note: "" });
  const [created, setCreated] = useState<{ url: string; expiresAt: string; label: string } | null>(null);

  const create = useMutation({
    mutationFn: () => api.post<CreatedAccessLink>("access-links", { ...form, note: form.note || null }),
    onSuccess: (result) => {
      setCreated({ url: `${window.location.origin}/acesso#${result.token}`, expiresAt: result.expiresAt, label: form.label });
      setForm({ ...form, label: "", note: "" });
      queryClient.invalidateQueries({ queryKey: ["access-links"] });
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Não foi possível criar o link.", "error"),
  });
  const revoke = useMutation({
    mutationFn: (id: string) => api.post(`access-links/${id}/revoke`),
    onSuccess: () => { toast("Acesso revogado. A pessoa foi desconectada.", "success"); queryClient.invalidateQueries({ queryKey: ["access-links"] }); },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao revogar.", "error"),
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    create.mutate();
  }

  return (
    <div className="space-y-6">
      <div className="flex gap-3 rounded-xl border border-sky-200 bg-sky-50 p-4 text-sm text-sky-900">
        <ShieldCheck className="mt-0.5 size-5 shrink-0" />
        <div>
          <p className="font-medium">Como funciona</p>
          <p className="mt-1 text-sky-900/80">
            Cada link dá acesso sem senha, por tempo limitado, em <strong>modo somente leitura</strong>. Telefones, e-mails, sobrenomes e observações dos clientes
            aparecem <strong>mascarados</strong> — você pode mostrar o sistema sem expor dados pessoais (LGPD). Revogar desconecta a pessoa na hora.
          </p>
        </div>
      </div>

      <form onSubmit={submit} className="space-y-4">
        <h2 className="section-title">Novo link de acesso</h2>
        <div className="grid gap-3 sm:grid-cols-2" role="radiogroup" aria-label="Tipo de acesso">
          {(Object.keys(roleInfo) as GuestRole[]).map((role) => {
            const { icon: Icon, title, description } = roleInfo[role];
            const active = form.role === role;
            return (
              <button key={role} type="button" role="radio" aria-checked={active} onClick={() => setForm({ ...form, role })}
                className={`flex gap-3 rounded-xl border p-4 text-left transition ${active ? "border-brand-500 bg-brand-50/60 ring-4 ring-brand-500/10" : "border-slate-200 hover:border-slate-300"}`}>
                <span className={`flex size-9 shrink-0 items-center justify-center rounded-lg ${role === "Desenvolvedor" ? "bg-violet-100 text-violet-700" : "bg-sky-100 text-sky-700"}`}><Icon className="size-4" /></span>
                <span>
                  <span className="block text-sm font-semibold text-slate-900">{title}</span>
                  <span className="mt-0.5 block text-xs text-slate-500">{description}</span>
                </span>
              </button>
            );
          })}
        </div>
        <div className="grid gap-3 sm:grid-cols-[1fr_160px]">
          <div>
            <label className="label" htmlFor="al-label">Para quem é?</label>
            <input id="al-label" className="input" required maxLength={120} placeholder="Ex.: João (investidor), Ana — dev freelancer" value={form.label} onChange={(e) => setForm({ ...form, label: e.target.value })} />
          </div>
          <div>
            <label className="label" htmlFor="al-days">Validade</label>
            <select id="al-days" className="input" value={form.expiresInDays} onChange={(e) => setForm({ ...form, expiresInDays: Number(e.target.value) })}>
              {[1, 3, 7, 14, 30].map((d) => <option key={d} value={d}>{d} {d === 1 ? "dia" : "dias"}</option>)}
            </select>
          </div>
        </div>
        <div>
          <label className="label" htmlFor="al-note">Observação interna (opcional)</label>
          <input id="al-note" className="input" maxLength={300} placeholder="Ex.: demonstração para parceiros" value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} />
        </div>
        <button className="btn-primary" disabled={create.isPending}><Link2 className="size-4" /> Gerar link</button>
      </form>

      <div>
        <h2 className="section-title mb-3">Links criados</h2>
        {links?.length === 0 && <EmptyState icon={<Link2 className="size-5" />} title="Nenhum link criado" description="Gere um link para mostrar o sistema ou receber ajuda de um desenvolvedor." />}
        <ul className="divide-y divide-slate-100 overflow-hidden rounded-xl border border-slate-200">
          {links?.map((l) => {
            const Icon = roleInfo[l.role].icon;
            return (
              <li key={l.id} className="flex flex-wrap items-center gap-3 bg-white px-4 py-3 text-sm">
                <Icon className={`size-4 ${l.role === "Desenvolvedor" ? "text-violet-600" : "text-sky-600"}`} />
                <span className="min-w-0 flex-1">
                  <span className="block font-medium text-slate-900">{l.label} <span className="font-normal text-slate-500">· {l.role}</span></span>
                  <span className="block text-xs text-slate-500">
                    Criado por {l.createdByName} {formatRelative(l.createdAt)} · {l.status === "Active" ? `expira em ${formatDateTime(l.expiresAt)}` : l.revokedAt ? `revogado em ${formatDateTime(l.revokedAt)}` : `expirou em ${formatDateTime(l.expiresAt)}`}
                    {" · "}{l.useCount > 0 ? `usado ${l.useCount}× (último ${formatRelative(l.lastUsedAt!)})` : "ainda não usado"}
                  </span>
                </span>
                <span className={`chip ring-1 ring-inset ${statusChip[l.status]}`}>{statusLabel[l.status]}</span>
                {l.status === "Active" && (
                  <button className="btn-ghost px-2 py-1 text-xs text-red-600 hover:bg-red-50 hover:text-red-700" onClick={() => revoke.mutate(l.id)} disabled={revoke.isPending}>Revogar</button>
                )}
              </li>
            );
          })}
        </ul>
      </div>

      {created && (
        <Modal title="Link de acesso criado" description={`Para ${created.label} · válido até ${formatDateTime(created.expiresAt)}`} open onClose={() => setCreated(null)}>
          <div className="space-y-4">
            <div className="flex gap-2">
              <input className="input font-mono text-xs" readOnly value={created.url} onFocus={(e) => e.currentTarget.select()} aria-label="Link de acesso" />
              <button className="btn-primary shrink-0" onClick={() => navigator.clipboard.writeText(created.url).then(() => toast("Link copiado.", "success"))}>
                <Copy className="size-4" /> Copiar
              </button>
            </div>
            <p className="flex gap-2 rounded-lg bg-amber-50 p-3 text-xs text-amber-900">
              <TriangleAlert className="size-4 shrink-0" />
              Este link só é exibido agora. Quem tiver o link entra no sistema até a data de validade — envie por um canal privado. Se vazar, revogue.
            </p>
          </div>
        </Modal>
      )}
    </div>
  );
}
