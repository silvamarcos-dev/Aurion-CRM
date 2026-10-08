"use client";

import { useQuery } from "@tanstack/react-query";
import { Activity, Check, ClipboardCopy, Database, Inbox, KeyRound, ListTree, Lock, Route, ScrollText, ServerCog, TriangleAlert, Unlock } from "lucide-react";
import { Fragment, useMemo, useState, type ReactNode } from "react";
import { useCan, useMe, useToast } from "@/components/providers";
import { EmptyState, PageHeader, Tabs } from "@/components/ui/page-header";
import { api } from "@/lib/api";
import { formatDateTime, formatRelative } from "@/lib/format";
import type { AuditEntry, EndpointInfo, OutboxStats, RoleMatrix, SystemInfo } from "@/lib/types";

type Tab = "sistema" | "fila" | "auditoria" | "api" | "permissoes";

export default function DeveloperPage() {
  const can = useCan();
  const [tab, setTab] = useState<Tab>("sistema");

  if (!can("dev.tools")) {
    return (
      <div className="p-8">
        <EmptyState icon={<Lock className="size-5" />} title="Acesso restrito" description="O painel técnico é exclusivo para administradores e desenvolvedores convidados." />
      </div>
    );
  }

  return (
    <div>
      <PageHeader title="Painel do desenvolvedor" description="Informações técnicas deste ambiente — somente leitura, sem dados pessoais completos.">
        <Tabs
          value={tab}
          onChange={setTab}
          items={[
            { key: "sistema", label: "Sistema" },
            { key: "fila", label: "Fila de eventos" },
            ...(can("audit.read") ? [{ key: "auditoria" as Tab, label: "Auditoria" }] : []),
            { key: "api", label: "Mapa da API" },
            { key: "permissoes", label: "Permissões" },
          ]}
        />
      </PageHeader>
      <div className="mx-auto max-w-6xl px-4 py-6 sm:px-6 lg:px-8">
        {tab === "sistema" && <SystemTab />}
        {tab === "fila" && <OutboxTab />}
        {tab === "auditoria" && <AuditTab />}
        {tab === "api" && <ApiTab />}
        {tab === "permissoes" && <PermissionsTab />}
      </div>
    </div>
  );
}

function Stat({ icon, label, value, tone = "slate" }: { icon: ReactNode; label: string; value: ReactNode; tone?: "slate" | "red" | "amber" | "brand" }) {
  const tones = { slate: "bg-slate-100 text-slate-600", red: "bg-red-50 text-red-600", amber: "bg-amber-50 text-amber-700", brand: "bg-brand-50 text-brand-700" };
  return (
    <div className="card flex items-center gap-3 p-4">
      <span className={`flex size-10 items-center justify-center rounded-xl ${tones[tone]}`}>{icon}</span>
      <span>
        <span className="block text-xs text-slate-500">{label}</span>
        <span className="block font-display text-xl font-semibold tabular-nums text-slate-900">{value}</span>
      </span>
    </div>
  );
}

function SystemTab() {
  const { data: me } = useMe();
  const toast = useToast();
  const { data } = useQuery({ queryKey: ["dev", "system"], queryFn: () => api.get<SystemInfo>("dev/system"), refetchInterval: 30_000 });
  if (!data || !me) return <div className="h-40 animate-pulse rounded-xl bg-white" />;

  const debugInfo = [
    `Ambiente: ${data.environment}`,
    `Versão: ${data.appVersion}`,
    `Runtime: ${data.runtime}`,
    `Banco: ${data.databaseVersion}`,
    `Migrations aplicadas: ${data.appliedMigrations.length} (última: ${data.appliedMigrations.at(-1) ?? "—"})`,
    `Migrations pendentes: ${data.pendingMigrations.length}`,
    `Fuso da organização: ${data.organizationTimeZone}`,
    `Hora do servidor (UTC): ${data.serverTimeUtc}`,
    `Navegador: ${navigator.userAgent}`,
    `Tela: ${window.innerWidth}x${window.innerHeight}`,
    `Papel: ${me.role}`,
  ].join("\n");

  return (
    <div className="space-y-6">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Stat icon={<ServerCog className="size-5" />} label="Ambiente" value={data.environment} tone="brand" />
        <Stat icon={<Database className="size-5" />} label="Migrations aplicadas" value={data.appliedMigrations.length} />
        <Stat icon={<TriangleAlert className="size-5" />} label="Migrations pendentes" value={data.pendingMigrations.length} tone={data.pendingMigrations.length ? "amber" : "slate"} />
        <Stat icon={<Activity className="size-5" />} label="Fuso da organização" value={<span className="text-base">{data.organizationTimeZone}</span>} />
      </div>

      <div className="card divide-y divide-slate-100">
        {[
          ["Versão da aplicação", data.appVersion],
          ["Runtime", data.runtime],
          ["Banco de dados", data.databaseVersion],
          ["Hora do servidor (fuso da organização)", formatDateTime(data.serverTimeUtc)],
        ].map(([k, v]) => (
          <div key={k} className="flex flex-wrap justify-between gap-2 px-4 py-3 text-sm">
            <span className="text-slate-500">{k}</span>
            <span className="font-mono text-slate-800">{v}</span>
          </div>
        ))}
        <div className="px-4 py-3 text-sm">
          <p className="mb-2 text-slate-500">Histórico de migrations</p>
          <ol className="space-y-1 font-mono text-xs text-slate-700">
            {data.appliedMigrations.map((m) => <li key={m} className="flex items-center gap-2"><Check className="size-3.5 text-brand-600" />{m}</li>)}
            {data.pendingMigrations.map((m) => <li key={m} className="flex items-center gap-2 text-amber-700"><TriangleAlert className="size-3.5" />{m} (pendente)</li>)}
          </ol>
        </div>
      </div>

      <button
        className="btn-secondary"
        onClick={() => navigator.clipboard.writeText(debugInfo).then(() => toast("Informações copiadas — cole no chat/issue.", "success"))}
      >
        <ClipboardCopy className="size-4" /> Copiar informações de diagnóstico
      </button>
    </div>
  );
}

function OutboxTab() {
  const { data } = useQuery({ queryKey: ["dev", "outbox"], queryFn: () => api.get<OutboxStats>("dev/outbox"), refetchInterval: 10_000 });
  if (!data) return <div className="h-40 animate-pulse rounded-xl bg-white" />;

  return (
    <div className="space-y-6">
      <p className="text-sm text-slate-500">
        Eventos de domínio (lead criado, etapa alterada, visita agendada…) são gravados na mesma transação e processados em segundo plano.
        É aqui que automações e o Copilot vão se conectar. Atualiza a cada 10 segundos.
      </p>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Stat icon={<Inbox className="size-5" />} label="Aguardando" value={data.pending} />
        <Stat icon={<Activity className="size-5" />} label="Em nova tentativa" value={data.retrying} tone={data.retrying ? "amber" : "slate"} />
        <Stat icon={<TriangleAlert className="size-5" />} label="Falha definitiva" value={data.deadLettered} tone={data.deadLettered ? "red" : "slate"} />
        <Stat icon={<Check className="size-5" />} label="Processados (24h)" value={data.processedLast24h} tone="brand" />
      </div>
      {data.oldestPendingSeconds != null && (
        <p className="text-sm text-slate-600">Evento mais antigo aguardando: há {Math.round(data.oldestPendingSeconds)} s</p>
      )}
      <div>
        <h2 className="section-title mb-3">Falhas recentes</h2>
        {data.recentFailures.length === 0 ? (
          <EmptyState icon={<Check className="size-5" />} title="Nenhuma falha" description="Todos os eventos foram processados com sucesso." />
        ) : (
          <div className="card overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-[11px] uppercase tracking-wide text-slate-500">
                <tr><th className="px-4 py-2.5">Evento</th><th className="px-4 py-2.5">Tentativas</th><th className="px-4 py-2.5">Erro</th><th className="px-4 py-2.5">Quando</th></tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.recentFailures.map((f) => (
                  <tr key={f.id}>
                    <td className="px-4 py-2.5 font-mono text-xs">{f.type}</td>
                    <td className={`px-4 py-2.5 ${f.deadLettered ? "font-medium text-red-600" : ""}`}>{f.attempts}{f.deadLettered ? " (desistiu)" : ""}</td>
                    <td className="max-w-md truncate px-4 py-2.5 font-mono text-xs text-slate-600" title={f.lastError ?? ""}>{f.lastError}</td>
                    <td className="px-4 py-2.5 text-slate-500">{formatRelative(f.occurredAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

function AuditTab() {
  const [action, setAction] = useState("");
  const [entity, setEntity] = useState("");
  const [expanded, setExpanded] = useState<number | null>(null);
  const params = new URLSearchParams({ limit: "100" });
  if (action) params.set("action", action);
  if (entity) params.set("entityType", entity);
  const { data } = useQuery({ queryKey: ["dev", "audit", params.toString()], queryFn: () => api.get<AuditEntry[]>(`dev/audit?${params}`) });

  return (
    <div className="space-y-4">
      <p className="text-sm text-slate-500">Trilha de auditoria imutável (encadeada por hash). Textos livres como motivos e observações são ocultados neste modo.</p>
      <div className="flex flex-wrap gap-2">
        <input className="input max-w-xs" placeholder="Ação começa com… (ex.: lead.)" value={action} onChange={(e) => setAction(e.target.value)} aria-label="Filtrar ação" />
        <select className="input w-auto" value={entity} onChange={(e) => setEntity(e.target.value)} aria-label="Entidade">
          <option value="">Todas as entidades</option>
          {["lead", "user", "pipeline", "appointment", "task", "macro", "holiday", "calendar_settings", "access_link", "suggestion"].map((e) => <option key={e} value={e}>{e}</option>)}
        </select>
      </div>
      <div className="card overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-[11px] uppercase tracking-wide text-slate-500">
            <tr><th className="px-4 py-2.5">#</th><th className="px-4 py-2.5">Quando</th><th className="px-4 py-2.5">Ação</th><th className="px-4 py-2.5">Entidade</th><th className="px-4 py-2.5">Ator</th></tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {data?.map((a) => (
              <Fragment key={a.sequence}>
                <tr className="cursor-pointer hover:bg-slate-50" onClick={() => setExpanded(expanded === a.sequence ? null : a.sequence)}>
                  <td className="px-4 py-2 font-mono text-xs text-slate-400">{a.sequence}</td>
                  <td className="whitespace-nowrap px-4 py-2 text-slate-600">{formatDateTime(a.occurredAt)}</td>
                  <td className="px-4 py-2 font-mono text-xs text-slate-800">{a.action}</td>
                  <td className="px-4 py-2 text-xs text-slate-600">{a.entityType}</td>
                  <td className="px-4 py-2 text-xs text-slate-600">{a.actorKind === "User" ? a.actorName ?? "Usuário" : a.actorKind}</td>
                </tr>
                {expanded === a.sequence && (
                  <tr className="bg-slate-50/60">
                    <td colSpan={5} className="px-4 py-3">
                      <pre className="overflow-x-auto rounded-lg bg-slate-900 p-3 text-xs text-slate-100">{JSON.stringify({ ...JSON.parse(a.data || "{}"), entityId: a.entityId, correlationId: a.correlationId }, null, 2)}</pre>
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
        {data?.length === 0 && <p className="p-6 text-center text-sm text-slate-500">Nenhum evento com esses filtros.</p>}
      </div>
    </div>
  );
}

const methodColors: Record<string, string> = {
  GET: "bg-sky-50 text-sky-700 ring-sky-200",
  POST: "bg-brand-50 text-brand-700 ring-brand-200",
  PUT: "bg-amber-50 text-amber-700 ring-amber-200",
  DELETE: "bg-red-50 text-red-700 ring-red-200",
};

function ApiTab() {
  const [term, setTerm] = useState("");
  const { data } = useQuery({ queryKey: ["dev", "endpoints"], queryFn: () => api.get<EndpointInfo[]>("dev/endpoints"), staleTime: Infinity });
  const groups = useMemo(() => {
    const filtered = (data ?? []).filter((e) => !term || e.route.includes(term) || e.group.toLowerCase().includes(term.toLowerCase()));
    return Object.entries(Object.groupBy(filtered, (e) => e.group)) as [string, EndpointInfo[]][];
  }, [data, term]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-slate-500">{data?.length ?? 0} endpoints gerados a partir do código — sempre em sincronia. Todos exigem autenticação, salvo os marcados como públicos.</p>
        <input className="input max-w-xs" placeholder="Filtrar rota ou grupo…" value={term} onChange={(e) => setTerm(e.target.value)} aria-label="Filtrar endpoints" />
      </div>
      {groups.map(([group, endpoints]) => (
        <section key={group} className="card overflow-hidden">
          <h2 className="flex items-center gap-2 border-b border-slate-100 bg-slate-50/70 px-4 py-2.5 text-sm font-semibold"><Route className="size-4 text-slate-400" />{group}</h2>
          <ul className="divide-y divide-slate-100">
            {endpoints.map((e) => (
              <li key={e.method + e.route} className="flex items-center gap-3 px-4 py-2 text-sm">
                <span className={`chip w-16 justify-center font-mono ring-1 ring-inset ${methodColors[e.method] ?? "bg-slate-100 text-slate-600 ring-slate-200"}`}>{e.method}</span>
                <code className="flex-1 font-mono text-xs text-slate-800">{e.route}</code>
                {e.anonymous ? (
                  <span className="flex items-center gap-1 text-xs text-amber-700"><Unlock className="size-3.5" /> público</span>
                ) : (
                  <span className="flex items-center gap-1 text-xs text-slate-400"><KeyRound className="size-3.5" /> autenticado</span>
                )}
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

const scopeStyles: Record<string, string> = { Org: "bg-brand-600 text-white", Team: "bg-brand-200 text-brand-900", Own: "bg-brand-50 text-brand-800 ring-1 ring-inset ring-brand-200" };

function PermissionsTab() {
  const { data: me } = useMe();
  const { data } = useQuery({ queryKey: ["dev", "roles"], queryFn: () => api.get<RoleMatrix>("dev/roles"), staleTime: 60_000 });
  if (!data || !me) return <div className="h-40 animate-pulse rounded-xl bg-white" />;

  return (
    <div className="space-y-6">
      <div className="card p-4">
        <h2 className="section-title mb-2 flex items-center gap-2"><ListTree className="size-4 text-slate-400" /> Minha sessão</h2>
        <p className="text-sm text-slate-600">Papel <strong>{me.role}</strong> em <strong>{me.organizationName}</strong>{me.isGuest ? " (convidado)" : ""}.</p>
        <div className="mt-3 flex flex-wrap gap-1.5">
          {Object.entries(me.capabilities).sort().map(([c, s]) => (
            <span key={c} className="chip bg-slate-100 font-mono text-slate-700">{c}<span className="text-slate-400">:{s}</span></span>
          ))}
        </div>
      </div>

      <div className="card overflow-x-auto">
        <table className="w-full text-left text-xs">
          <thead className="bg-slate-50 text-[11px] uppercase tracking-wide text-slate-500">
            <tr>
              <th className="sticky left-0 bg-slate-50 px-4 py-2.5"><ScrollText className="inline size-3.5" /> Capability</th>
              {data.roles.map((r) => <th key={r.role} className="whitespace-nowrap px-3 py-2.5 text-center">{r.role}</th>)}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {data.capabilities.map((c) => (
              <tr key={c}>
                <td className="sticky left-0 bg-white px-4 py-2 font-mono text-slate-700">{c}</td>
                {data.roles.map((r) => {
                  const scope = r.grants[c];
                  return (
                    <td key={r.role} className="px-3 py-2 text-center">
                      {scope ? <span className={`rounded px-1.5 py-0.5 text-[10px] font-semibold ${scopeStyles[scope]}`}>{scope}</span> : <span className="text-slate-300">—</span>}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="text-xs text-slate-500">Org = toda a organização · Team = time do usuário · Own = apenas registros próprios. A autorização é sempre verificada no servidor.</p>
    </div>
  );
}
