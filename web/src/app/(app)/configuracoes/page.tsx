"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, type FormEvent } from "react";
import { AccessLinksPanel } from "@/components/access-links-panel";
import { useCan, useToast } from "@/components/providers";
import { PageHeader, Tabs } from "@/components/ui/page-header";
import { toApiTime, WeeklyHoursEditor } from "@/components/weekly-hours-editor";
import { api, ApiError } from "@/lib/api";
import { formatDateTime } from "@/lib/format";
import { addDays, formatDayLabel, todayInZone, zonedToIso } from "@/lib/time";
import type { Absence, AbsenceKind, AgentSchedule, Agenda, CalendarExceptionItem, CalendarSettings, CalendarStatus, Holiday } from "@/lib/types";

type Tab = "jornada" | "empresa" | "feriados" | "excecoes" | "acessos";

export default function SettingsPage() {
  const can = useCan();
  const admin = can("settings.manage");
  const [tab, setTab] = useState<Tab>("jornada");

  // Permite abrir uma aba direto pelo link (ex.: /configuracoes#acessos, usado na sidebar).
  useEffect(() => {
    const sync = () => {
      const hash = window.location.hash.slice(1) as Tab;
      if (["jornada", "empresa", "feriados", "excecoes", "acessos"].includes(hash)) setTab(hash);
    };
    sync();
    window.addEventListener("hashchange", sync);
    return () => window.removeEventListener("hashchange", sync);
  }, []);
  const tabs: { key: Tab; label: string; show: boolean }[] = [
    { key: "jornada", label: "Minha jornada", show: can("calendar.manage") },
    { key: "empresa", label: "Expediente da empresa", show: can("calendar.read") },
    { key: "feriados", label: "Feriados", show: can("calendar.read") },
    { key: "excecoes", label: "Exceções", show: can("calendar.read") },
    { key: "acessos", label: "Acessos externos", show: can("users.manage") },
  ];

  return (
    <div>
      <PageHeader title="Configurações" description="Sua jornada, o expediente da empresa e o calendário de feriados.">
        <Tabs value={tab} onChange={setTab} items={tabs.filter((t) => t.show).map((t) => ({ key: t.key, label: t.label }))} />
      </PageHeader>
      <div className="mx-auto max-w-4xl px-4 py-6 sm:px-6 lg:px-8">
        <div className="card p-5 sm:p-6">
          {tab === "jornada" && <MySchedule />}
          {tab === "empresa" && <CompanyHours editable={admin} />}
          {tab === "feriados" && <Holidays editable={admin} />}
          {tab === "excecoes" && <Exceptions editable={admin} />}
          {tab === "acessos" && <AccessLinksPanel />}
        </div>
      </div>
    </div>
  );
}

// ───────────────────────── Minha jornada ─────────────────────────

function MySchedule() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { data } = useQuery({ queryKey: ["schedule", "me"], queryFn: () => api.get<AgentSchedule>("agenda/schedule") });
  const [form, setForm] = useState<AgentSchedule | null>(null);
  useEffect(() => { if (data) setForm(data); }, [data]);

  const save = useMutation({
    mutationFn: (f: AgentSchedule) => api.put("agenda/schedule", f),
    onSuccess: () => {
      toast("Jornada salva.", "success");
      for (const key of ["schedule", "agenda", "availability"]) queryClient.invalidateQueries({ queryKey: [key] });
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao salvar.", "error"),
  });

  if (!form) return <p className="text-sm text-slate-500">Carregando…</p>;
  const set = (patch: Partial<AgentSchedule>) => setForm({ ...form, ...patch });

  return (
    <div className="space-y-8">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate(form); }} className="space-y-4">
        <div>
          <h2 className="text-sm font-semibold">Dias e horários de trabalho</h2>
          <p className="mb-3 text-xs text-slate-500">
            Base para a disponibilidade de visitas (inclusive a que o Copilot consultará). Intervalos entre blocos — como o almoço — ficam indisponíveis.
          </p>
          <WeeklyHoursEditor value={form.workBlocks} onChange={(workBlocks) => set({ workBlocks })} />
        </div>
        <div className="grid gap-3 sm:grid-cols-4">
          <NumberField id="sc-dur" label="Duração da visita (min)" value={form.visitDurationMinutes} onChange={(v) => set({ visitDurationMinutes: v ?? 60 })} />
          <NumberField id="sc-buf" label="Intervalo entre compromissos (min)" value={form.bufferMinutes} onChange={(v) => set({ bufferMinutes: v ?? 0 })} />
          <NumberField id="sc-notice" label="Antecedência mínima (min)" value={form.minNoticeMinutes} onChange={(v) => set({ minNoticeMinutes: v ?? 0 })} />
          <NumberField id="sc-max" label="Máx. visitas por dia" value={form.maxVisitsPerDay} optional onChange={(v) => set({ maxVisitsPerDay: v })} />
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="accent-brand-700" checked={form.worksOnHolidays} onChange={(e) => set({ worksOnHolidays: e.target.checked })} />
          Trabalho em feriados
        </label>
        <button className="btn-primary" disabled={save.isPending}>Salvar jornada</button>
      </form>
      <Absences />
    </div>
  );
}

function NumberField({ id, label, value, onChange, optional }: { id: string; label: string; value: number | null; onChange: (v: number | null) => void; optional?: boolean }) {
  return (
    <div>
      <label className="label" htmlFor={id}>{label}</label>
      <input id={id} type="number" min={0} className="input" value={value ?? ""} placeholder={optional ? "Sem limite" : undefined}
        onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))} required={!optional} />
    </div>
  );
}

const absenceLabels: Record<AbsenceKind, string> = { Vacation: "Férias", DayOff: "Folga", Blocked: "Horário bloqueado", Sick: "Atestado", Other: "Outro" };

function Absences() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const today = todayInZone();
  const { data: agenda } = useQuery({
    queryKey: ["agenda", "me", "absences", today],
    queryFn: () => api.get<Agenda>(`agenda?from=${today}&to=${addDays(today, 60)}`),
  });
  const [form, setForm] = useState({ kind: "DayOff" as AbsenceKind, startDate: addDays(today, 1), startTime: "08:00", endDate: addDays(today, 1), endTime: "18:00", note: "" });

  const create = useMutation({
    mutationFn: () => api.post("agenda/absences", {
      kind: form.kind,
      startsAt: zonedToIso(form.startDate, form.startTime),
      endsAt: zonedToIso(form.endDate, form.endTime),
      note: form.note || null,
    }),
    onSuccess: () => { toast("Ausência registrada.", "success"); queryClient.invalidateQueries({ queryKey: ["agenda"] }); queryClient.invalidateQueries({ queryKey: ["availability"] }); },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao registrar.", "error"),
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.del(`agenda/absences/${id}`),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["agenda"] }); queryClient.invalidateQueries({ queryKey: ["availability"] }); },
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    create.mutate();
  }

  return (
    <section>
      <h2 className="text-sm font-semibold">Ausências e bloqueios</h2>
      <p className="mb-3 text-xs text-slate-500">Férias, folgas e horários bloqueados removem a disponibilidade do período.</p>
      <form onSubmit={submit} className="card mb-3 grid gap-3 p-4 sm:grid-cols-6">
        <div className="sm:col-span-2">
          <label className="label" htmlFor="ab-kind">Tipo</label>
          <select id="ab-kind" className="input" value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value as AbsenceKind })}>
            {Object.entries(absenceLabels).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
        </div>
        <div><label className="label" htmlFor="ab-sd">Início</label><input id="ab-sd" type="date" className="input" value={form.startDate} onChange={(e) => setForm({ ...form, startDate: e.target.value })} /></div>
        <div><label className="label" htmlFor="ab-st">&nbsp;</label><input id="ab-st" aria-label="Hora de início" type="time" className="input" value={form.startTime} onChange={(e) => setForm({ ...form, startTime: e.target.value })} /></div>
        <div><label className="label" htmlFor="ab-ed">Fim</label><input id="ab-ed" type="date" className="input" value={form.endDate} onChange={(e) => setForm({ ...form, endDate: e.target.value })} /></div>
        <div><label className="label" htmlFor="ab-et">&nbsp;</label><input id="ab-et" aria-label="Hora de fim" type="time" className="input" value={form.endTime} onChange={(e) => setForm({ ...form, endTime: e.target.value })} /></div>
        <div className="sm:col-span-5"><input className="input" aria-label="Observação" placeholder="Observação (opcional)" maxLength={200} value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} /></div>
        <button className="btn-secondary" disabled={create.isPending}>Adicionar</button>
      </form>
      <ul className="space-y-1">
        {agenda?.absences.map((a: Absence) => (
          <li key={a.id} className="flex items-center justify-between rounded-md bg-white px-3 py-2 text-sm ring-1 ring-slate-200">
            <span><strong className="font-medium">{absenceLabels[a.kind]}</strong> · {formatDateTime(a.startsAt)} → {formatDateTime(a.endsAt)}{a.note ? ` · ${a.note}` : ""}</span>
            <button className="text-xs text-slate-400 hover:text-red-600" onClick={() => remove.mutate(a.id)}>Remover</button>
          </li>
        ))}
        {agenda?.absences.length === 0 && <li className="text-sm text-slate-500">Nenhuma ausência nos próximos 60 dias.</li>}
      </ul>
    </section>
  );
}

// ───────────────────────── Empresa ─────────────────────────

const coverageLabels: Record<keyof CalendarSettings["coverage"], string> = {
  offHours: "Fora do horário comercial (dias úteis)",
  saturdays: "Sábados",
  sundays: "Domingos",
  holidays: "Feriados",
};

function CompanyHours({ editable }: { editable: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { data } = useQuery({ queryKey: ["calendar-settings"], queryFn: () => api.get<CalendarSettings>("calendar/settings") });
  const { data: status } = useQuery({ queryKey: ["calendar-status"], queryFn: () => api.get<CalendarStatus>("calendar/status"), refetchInterval: 60_000 });
  const [form, setForm] = useState<CalendarSettings | null>(null);
  useEffect(() => { if (data) setForm(data); }, [data]);

  const save = useMutation({
    mutationFn: (f: CalendarSettings) => api.put("calendar/settings", { ...f, stateCode: f.stateCode || null, cityIbgeCode: f.cityIbgeCode || null }),
    onSuccess: () => {
      toast("Expediente salvo.", "success");
      for (const key of ["calendar-settings", "calendar-status", "agenda", "availability", "holidays"]) queryClient.invalidateQueries({ queryKey: [key] });
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Falha ao salvar.", "error"),
  });

  if (!form) return <p className="text-sm text-slate-500">Carregando…</p>;

  return (
    <form onSubmit={(e) => { e.preventDefault(); save.mutate(form); }} className="space-y-6">
      {status && (
        <div className={`rounded-lg p-3 text-sm ring-1 ${status.isBusinessTime ? "bg-brand-50 text-brand-900 ring-brand-200" : "bg-slate-50 text-slate-700 ring-slate-200"}`}>
          <strong>Agora:</strong> {status.reason}{" "}
          {status.copilotMayCover ? "O Copilot poderá atender nesta janela (conforme autonomia configurada)." : "O Copilot não assume atendimentos nesta janela."}
        </div>
      )}
      <fieldset disabled={!editable} className="space-y-6">
        <div>
          <h2 className="text-sm font-semibold">Horário comercial</h2>
          <p className="mb-3 text-xs text-slate-500">Intervalos semiabertos: um expediente até 18:00 termina às 17:59:59.</p>
          <WeeklyHoursEditor value={form.businessHours} onChange={(businessHours) => setForm({ ...form, businessHours })} disabled={!editable} />
        </div>
        <div className="grid gap-3 sm:grid-cols-3">
          <div>
            <label className="label" htmlFor="cs-tz">Fuso horário</label>
            <select id="cs-tz" className="input" value={form.timeZone} onChange={(e) => setForm({ ...form, timeZone: e.target.value })}>
              {["America/Sao_Paulo", "America/Manaus", "America/Cuiaba", "America/Belem", "America/Fortaleza", "America/Recife", "America/Bahia", "America/Porto_Velho", "America/Rio_Branco", "America/Noronha"].map((tz) => (
                <option key={tz} value={tz}>{tz}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="label" htmlFor="cs-uf">UF (feriados estaduais)</label>
            <input id="cs-uf" className="input uppercase" maxLength={2} value={form.stateCode ?? ""} onChange={(e) => setForm({ ...form, stateCode: e.target.value.toUpperCase() })} />
          </div>
          <div>
            <label className="label" htmlFor="cs-ibge">Município — código IBGE</label>
            <input id="cs-ibge" className="input" inputMode="numeric" maxLength={7} placeholder="3550308" value={form.cityIbgeCode ?? ""} onChange={(e) => setForm({ ...form, cityIbgeCode: e.target.value.replace(/\D/g, "") })} />
          </div>
        </div>
        <div>
          <h2 className="text-sm font-semibold">Quando o Copilot pode atender sozinho</h2>
          <p className="mb-3 text-xs text-slate-500">
            Define apenas a JANELA de cobertura. O nível de autonomia do Copilot (observação, sugestão, supervisionado) é configurado nas fases do Copilot e começa no mínimo.
          </p>
          <div className="grid gap-2 sm:grid-cols-2">
            {(Object.keys(coverageLabels) as (keyof CalendarSettings["coverage"])[]).map((key) => (
              <label key={key} className="flex items-center gap-2 text-sm">
                <input type="checkbox" className="accent-brand-700" checked={form.coverage[key]} onChange={(e) => setForm({ ...form, coverage: { ...form.coverage, [key]: e.target.checked } })} />
                {coverageLabels[key]}
              </label>
            ))}
          </div>
        </div>
      </fieldset>
      {editable ? <button className="btn-primary" disabled={save.isPending}>Salvar expediente</button> : <p className="text-xs text-slate-500">Somente administradores alteram o expediente.</p>}
    </form>
  );
}

// ───────────────────────── Feriados ─────────────────────────

const scopeLabels = { National: "Nacional", State: "Estadual", City: "Municipal" } as const;

function Holidays({ editable }: { editable: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [year, setYear] = useState(() => Number(todayInZone().slice(0, 4)));
  const { data: holidays } = useQuery({ queryKey: ["holidays", year], queryFn: () => api.get<Holiday[]>(`calendar/holidays?year=${year}`) });
  const [form, setForm] = useState({ date: `${year}-01-01`, name: "", scope: "City" as Holiday["scope"], stateCode: "", cityIbgeCode: "" });
  const invalidate = () => { for (const key of ["holidays", "agenda", "availability", "calendar-status"]) queryClient.invalidateQueries({ queryKey: [key] }); };
  const onError = (err: unknown) => toast(err instanceof ApiError ? err.message : "Operação não concluída.", "error");

  const importer = useMutation({
    mutationFn: () => api.post<{ imported: number; alreadyPresent: number; provider: string }>(`calendar/holidays/import?year=${year}`),
    onSuccess: (r) => { toast(`${r.imported} feriados importados de ${r.provider}. Revise e confirme.`, "success"); invalidate(); },
    onError,
  });
  const confirm = useMutation({ mutationFn: (id: string) => api.post(`calendar/holidays/${id}/confirm`), onSuccess: invalidate, onError });
  const remove = useMutation({ mutationFn: (id: string) => api.del(`calendar/holidays/${id}`), onSuccess: invalidate, onError });
  const create = useMutation({
    mutationFn: () => api.post("calendar/holidays", { ...form, stateCode: form.stateCode || null, cityIbgeCode: form.cityIbgeCode || null }),
    onSuccess: () => { toast("Feriado cadastrado.", "success"); setForm({ ...form, name: "" }); invalidate(); },
    onError,
  });

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <button className="btn-secondary" onClick={() => setYear(year - 1)} aria-label="Ano anterior">‹</button>
        <span className="w-14 text-center font-medium">{year}</span>
        <button className="btn-secondary" onClick={() => setYear(year + 1)} aria-label="Próximo ano">›</button>
        {editable && (
          <button className="btn-secondary ml-auto" onClick={() => importer.mutate()} disabled={importer.isPending}>
            {importer.isPending ? "Importando…" : "Importar nacionais (BrasilAPI)"}
          </button>
        )}
      </div>
      <p className="text-xs text-slate-500">
        Feriados importados ficam <strong>pendentes</strong> até um administrador confirmar — a API pode incluir pontos facultativos (como Carnaval) que não são feriados para todas as empresas.
      </p>

      <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200 bg-white">
        {holidays?.map((h) => (
          <li key={h.id} className="flex flex-wrap items-center gap-2 px-3 py-2 text-sm">
            <span className="w-24 text-slate-600">{formatDayLabel(h.date)}</span>
            <span className="flex-1 font-medium">{h.name}</span>
            <span className="text-xs text-slate-500">{scopeLabels[h.scope]}{h.stateCode ? ` · ${h.stateCode}` : ""}{h.cityIbgeCode ? ` · ${h.cityIbgeCode}` : ""}</span>
            {!h.appliesToOrganization && <span className="rounded bg-slate-100 px-1.5 text-xs text-slate-500">não se aplica à sua localização</span>}
            {h.isConfirmed
              ? <span className="rounded bg-brand-50 px-1.5 text-xs text-brand-800">confirmado</span>
              : <span className="rounded bg-amber-50 px-1.5 text-xs text-amber-800">pendente</span>}
            {editable && !h.isConfirmed && <button className="text-xs text-brand-700 hover:underline" onClick={() => confirm.mutate(h.id)}>Confirmar</button>}
            {editable && <button className="text-xs text-slate-400 hover:text-red-600" onClick={() => remove.mutate(h.id)}>Remover</button>}
          </li>
        ))}
        {holidays?.length === 0 && <li className="px-3 py-4 text-center text-sm text-slate-500">Nenhum feriado cadastrado para {year}.</li>}
      </ul>

      {editable && (
        <form onSubmit={(e) => { e.preventDefault(); create.mutate(); }} className="card grid gap-3 p-4 sm:grid-cols-6">
          <div className="sm:col-span-6 text-sm font-semibold">Cadastrar feriado estadual/municipal</div>
          <div><label className="label" htmlFor="hd-date">Data</label><input id="hd-date" type="date" className="input" required value={form.date} onChange={(e) => setForm({ ...form, date: e.target.value })} /></div>
          <div className="sm:col-span-2"><label className="label" htmlFor="hd-name">Nome</label><input id="hd-name" className="input" required maxLength={120} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></div>
          <div>
            <label className="label" htmlFor="hd-scope">Abrangência</label>
            <select id="hd-scope" className="input" value={form.scope} onChange={(e) => setForm({ ...form, scope: e.target.value as Holiday["scope"] })}>
              {Object.entries(scopeLabels).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </select>
          </div>
          {form.scope === "State" && <div><label className="label" htmlFor="hd-uf">UF</label><input id="hd-uf" className="input uppercase" maxLength={2} required value={form.stateCode} onChange={(e) => setForm({ ...form, stateCode: e.target.value.toUpperCase() })} /></div>}
          {form.scope === "City" && <div><label className="label" htmlFor="hd-ibge">Cód. IBGE</label><input id="hd-ibge" className="input" maxLength={7} required value={form.cityIbgeCode} onChange={(e) => setForm({ ...form, cityIbgeCode: e.target.value.replace(/\D/g, "") })} /></div>}
          <div className="flex items-end"><button className="btn-primary w-full" disabled={create.isPending}>Adicionar</button></div>
        </form>
      )}
    </div>
  );
}

// ───────────────────────── Exceções ─────────────────────────

function Exceptions({ editable }: { editable: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const year = Number(todayInZone().slice(0, 4));
  const { data } = useQuery({ queryKey: ["calendar-exceptions", year], queryFn: () => api.get<CalendarExceptionItem[]>(`calendar/exceptions?year=${year}`) });
  const [form, setForm] = useState({ date: addDays(todayInZone(), 7), kind: "Closed" as CalendarExceptionItem["kind"], start: "08:00", end: "12:00", note: "" });
  const invalidate = () => { for (const key of ["calendar-exceptions", "agenda", "availability", "calendar-status"]) queryClient.invalidateQueries({ queryKey: [key] }); };
  const onError = (err: unknown) => toast(err instanceof ApiError ? err.message : "Operação não concluída.", "error");

  const create = useMutation({
    mutationFn: () => api.post("calendar/exceptions", {
      date: form.date,
      kind: form.kind,
      start: form.kind === "SpecialHours" ? toApiTime(form.start) : null,
      end: form.kind === "SpecialHours" ? toApiTime(form.end) : null,
      note: form.note || null,
    }),
    onSuccess: () => { toast("Exceção cadastrada.", "success"); invalidate(); },
    onError,
  });
  const remove = useMutation({ mutationFn: (id: string) => api.del(`calendar/exceptions/${id}`), onSuccess: invalidate, onError });

  return (
    <div className="space-y-4">
      <p className="text-xs text-slate-500">Dias em que a empresa fecha ou tem expediente especial (ex.: véspera de Natal, inventário).</p>
      <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200 bg-white">
        {data?.map((x) => (
          <li key={x.id} className="flex items-center gap-2 px-3 py-2 text-sm">
            <span className="w-24 text-slate-600">{formatDayLabel(x.date)}</span>
            <span className="flex-1">{x.kind === "Closed" ? "Fechado" : `Expediente especial ${x.start?.slice(0, 5)}–${x.end?.slice(0, 5)}`}{x.note ? ` · ${x.note}` : ""}</span>
            {editable && <button className="text-xs text-slate-400 hover:text-red-600" onClick={() => remove.mutate(x.id)}>Remover</button>}
          </li>
        ))}
        {data?.length === 0 && <li className="px-3 py-4 text-center text-sm text-slate-500">Nenhuma exceção em {year}.</li>}
      </ul>
      {editable && (
        <form onSubmit={(e) => { e.preventDefault(); create.mutate(); }} className="card grid gap-3 p-4 sm:grid-cols-6">
          <div><label className="label" htmlFor="ex-date">Data</label><input id="ex-date" type="date" className="input" required value={form.date} onChange={(e) => setForm({ ...form, date: e.target.value })} /></div>
          <div className="sm:col-span-2">
            <label className="label" htmlFor="ex-kind">Tipo</label>
            <select id="ex-kind" className="input" value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value as CalendarExceptionItem["kind"] })}>
              <option value="Closed">Fechado o dia todo</option>
              <option value="SpecialHours">Expediente especial</option>
            </select>
          </div>
          {form.kind === "SpecialHours" && (
            <>
              <div><label className="label" htmlFor="ex-start">Início</label><input id="ex-start" type="time" className="input" value={form.start} onChange={(e) => setForm({ ...form, start: e.target.value })} /></div>
              <div><label className="label" htmlFor="ex-end">Fim</label><input id="ex-end" type="time" className="input" value={form.end} onChange={(e) => setForm({ ...form, end: e.target.value })} /></div>
            </>
          )}
          <div className="sm:col-span-5"><input className="input" aria-label="Observação" placeholder="Observação" maxLength={200} value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} /></div>
          <button className="btn-primary" disabled={create.isPending}>Adicionar</button>
        </form>
      )}
    </div>
  );
}
