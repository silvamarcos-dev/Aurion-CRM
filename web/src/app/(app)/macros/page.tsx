"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState, type FormEvent } from "react";
import { Pencil, Play, Plus } from "lucide-react";
import { LeadPicker, type PickedLead } from "@/components/lead-picker";
import { PageHeader } from "@/components/ui/page-header";
import { Modal } from "@/components/modal";
import { useMe, useToast } from "@/components/providers";
import { api, ApiError } from "@/lib/api";
import type { Macro, MacroVariable, RenderResult } from "@/lib/types";

export default function MacrosPage() {
  const { data: me } = useMe();
  const [editing, setEditing] = useState<Macro | "new" | null>(null);
  const [testing, setTesting] = useState<Macro | null>(null);
  const { data: macros, isLoading } = useQuery({ queryKey: ["macros"], queryFn: () => api.get<Macro[]>("macros?includeInactive=true") });
  const manageScope = me?.capabilities["macros.manage"];

  const shared = macros?.filter((m) => m.isShared) ?? [];
  const personal = macros?.filter((m) => !m.isShared) ?? [];

  return (
    <div>
      <PageHeader
        title="Macros"
        description="Respostas rápidas com variáveis — no atendimento, basta digitar o atalho."
        actions={manageScope && <button className="btn-primary" onClick={() => setEditing("new")}><Plus className="size-4" /> Nova macro</button>}
      />
      <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
        {isLoading && <div className="grid gap-3 md:grid-cols-2">{[0, 1, 2, 3].map((i) => <div key={i} className="h-36 animate-pulse rounded-xl bg-white" />)}</div>}
        <Section title="Compartilhadas pela empresa" macros={shared} onEdit={setEditing} onTest={setTesting} />
        <Section title="Minhas macros" macros={personal} onEdit={setEditing} onTest={setTesting} empty="Você ainda não criou macros pessoais." />
      </div>

      {editing && <MacroEditor macro={editing === "new" ? null : editing} canShare={manageScope === "org"} onClose={() => setEditing(null)} />}
      {testing && <MacroTester macro={testing} onClose={() => setTesting(null)} />}
    </div>
  );
}

function Section({ title, macros, onEdit, onTest, empty }: { title: string; macros: Macro[]; onEdit: (m: Macro) => void; onTest: (m: Macro) => void; empty?: string }) {
  return (
    <section className="mb-8">
      <h2 className="section-title mb-3">{title} <span className="ml-1 text-xs font-normal text-slate-400">{macros.length}</span></h2>
      {macros.length === 0 && empty && <p className="text-sm text-slate-500">{empty}</p>}
      <div className="grid gap-3 md:grid-cols-2">
        {macros.map((m) => (
          <article key={m.id} className={`card flex flex-col p-4 transition hover:shadow-md ${m.isActive ? "" : "opacity-60"}`}>
            <div className="mb-2 flex items-center justify-between gap-2">
              <div className="min-w-0">
                <code className="rounded-md bg-brand-50 px-1.5 py-0.5 font-mono text-xs font-medium text-brand-800 ring-1 ring-inset ring-brand-200">{m.shortcut}</code>
                <span className="ml-2 text-sm font-medium">{m.title}</span>
                {!m.isActive && <span className="ml-2 text-xs text-slate-500">(inativa)</span>}
              </div>
              <span className="shrink-0 text-xs text-slate-400">{m.usageCount} usos</span>
            </div>
            <p className="line-clamp-4 flex-1 whitespace-pre-wrap rounded-lg bg-slate-50 p-3 text-sm leading-relaxed text-slate-600">{m.body}</p>
            <div className="mt-3 flex gap-1">
              <button className="btn-ghost px-2 py-1 text-xs text-brand-700" onClick={() => onTest(m)}><Play className="size-3.5" /> Testar</button>
              {m.canEdit && <button className="btn-ghost px-2 py-1 text-xs" onClick={() => onEdit(m)}><Pencil className="size-3.5" /> Editar</button>}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

function MacroEditor({ macro, canShare, onClose }: { macro: Macro | null; canShare: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const bodyRef = useRef<HTMLTextAreaElement>(null);
  const { data: variables } = useQuery({ queryKey: ["macro-variables"], queryFn: () => api.get<MacroVariable[]>("macros/variables"), staleTime: Infinity });
  const [form, setForm] = useState({
    shortcut: macro?.shortcut ?? "/",
    title: macro?.title ?? "",
    body: macro?.body ?? "",
    shared: macro?.isShared ?? false,
    isActive: macro?.isActive ?? true,
  });

  const save = useMutation({
    mutationFn: () => {
      const body = { ...form, developmentId: macro?.developmentId ?? null };
      return macro ? api.put(`macros/${macro.id}`, body) : api.post("macros", body);
    },
    onSuccess: () => { toast("Macro salva.", "success"); queryClient.invalidateQueries({ queryKey: ["macros"] }); onClose(); },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao salvar.", "error"),
  });
  const remove = useMutation({
    mutationFn: () => api.del(`macros/${macro!.id}`),
    onSuccess: () => { toast("Macro excluída.", "success"); queryClient.invalidateQueries({ queryKey: ["macros"] }); onClose(); },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao excluir.", "error"),
  });

  function insertVariable(name: string) {
    const el = bodyRef.current;
    const token = `{{${name}}}`;
    if (!el) return setForm((f) => ({ ...f, body: f.body + token }));
    const start = el.selectionStart;
    const next = form.body.slice(0, start) + token + form.body.slice(el.selectionEnd);
    setForm((f) => ({ ...f, body: next }));
    requestAnimationFrame(() => { el.focus(); el.selectionStart = el.selectionEnd = start + token.length; });
  }

  function submit(e: FormEvent) {
    e.preventDefault();
    save.mutate();
  }

  return (
    <Modal title={macro ? "Editar macro" : "Nova macro"} open onClose={onClose}>
      <form onSubmit={submit} className="space-y-3">
        <div className="grid grid-cols-[140px_1fr] gap-3">
          <div>
            <label className="label" htmlFor="mc-short">Atalho</label>
            <input id="mc-short" className="input font-mono" required maxLength={33} value={form.shortcut} onChange={(e) => setForm((f) => ({ ...f, shortcut: e.target.value }))} />
          </div>
          <div>
            <label className="label" htmlFor="mc-title">Título</label>
            <input id="mc-title" className="input" required maxLength={120} value={form.title} onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))} />
          </div>
        </div>
        <div>
          <label className="label" htmlFor="mc-body">Texto</label>
          <textarea id="mc-body" ref={bodyRef} className="input min-h-32" required maxLength={4000} value={form.body} onChange={(e) => setForm((f) => ({ ...f, body: e.target.value }))} />
          <div className="mt-2 flex flex-wrap gap-1">
            {variables?.map((v) => (
              <button key={v.name} type="button" title={v.description} onClick={() => insertVariable(v.name)}
                className="rounded bg-slate-100 px-1.5 py-0.5 font-mono text-[11px] text-slate-700 hover:bg-brand-50 hover:text-brand-800">
                {`{{${v.name}}}`}
              </button>
            ))}
          </div>
        </div>
        <div className="flex flex-wrap gap-4 text-sm">
          {!macro && canShare && (
            <label className="flex items-center gap-2">
              <input type="checkbox" className="accent-brand-700" checked={form.shared} onChange={(e) => setForm((f) => ({ ...f, shared: e.target.checked }))} />
              Compartilhar com toda a empresa
            </label>
          )}
          {macro && (
            <label className="flex items-center gap-2">
              <input type="checkbox" className="accent-brand-700" checked={form.isActive} onChange={(e) => setForm((f) => ({ ...f, isActive: e.target.checked }))} />
              Ativa
            </label>
          )}
        </div>
        <div className="flex items-center justify-between gap-2">
          {macro ? (
            <button type="button" className="text-sm text-red-600 hover:underline" onClick={() => remove.mutate()}>Excluir</button>
          ) : <span />}
          <div className="flex gap-2">
            <button type="button" className="btn-secondary" onClick={onClose}>Cancelar</button>
            <button className="btn-primary" disabled={save.isPending}>Salvar</button>
          </div>
        </div>
      </form>
    </Modal>
  );
}

function MacroTester({ macro, onClose }: { macro: Macro; onClose: () => void }) {
  const [lead, setLead] = useState<PickedLead | null>(null);
  const toast = useToast();
  const render = useMutation({
    mutationFn: () => api.post<RenderResult>(`macros/${macro.id}/render`, { leadId: lead?.id ?? null }),
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao renderizar.", "error"),
  });

  return (
    <Modal title={`Testar ${macro.shortcut}`} open onClose={onClose}>
      <div className="space-y-3">
        <div>
          <label className="label" htmlFor="mt-lead">Lead (opcional)</label>
          <LeadPicker id="mt-lead" value={lead} onChange={setLead} />
        </div>
        <button className="btn-primary" onClick={() => render.mutate()} disabled={render.isPending}>Gerar texto</button>
        {render.data && (
          <div className="rounded-md bg-slate-50 p-3">
            <p className="whitespace-pre-wrap text-sm">{render.data.text}</p>
            {render.data.missingVariables.length > 0 && (
              <p className="mt-2 text-xs text-amber-700">Sem valor para: {render.data.missingVariables.join(", ")}</p>
            )}
          </div>
        )}
        <p className="text-xs text-slate-500">Nada é enviado ao lead — esta é apenas uma prévia.</p>
      </div>
    </Modal>
  );
}
