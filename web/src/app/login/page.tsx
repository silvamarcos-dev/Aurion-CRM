"use client";

import { CalendarCheck2, Columns3, LandPlot, Loader2, ShieldCheck } from "lucide-react";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState, type FormEvent } from "react";

function LoginForm() {
  const router = useRouter();
  const params = useSearchParams();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [organization, setOrganization] = useState("");
  const [needsOrganization, setNeedsOrganization] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError(null);
    try {
      const response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json", "x-crm-request": "1" },
        body: JSON.stringify({ email, password, organization: organization || undefined }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => ({}));
        if (problem.title === "Informe a organização.") setNeedsOrganization(true);
        setError(response.status === 429 ? "Muitas tentativas. Aguarde um minuto." : (problem.title ?? "Falha no login."));
        return;
      }
      const next = params.get("next");
      router.replace(next && next.startsWith("/") && !next.startsWith("//") ? next : "/kanban");
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={submit} className="w-full max-w-sm space-y-5">
      <div className="mb-8 flex items-center gap-2.5 lg:hidden">
        <span className="flex size-9 items-center justify-center rounded-xl bg-gradient-to-br from-brand-400 to-brand-700 text-white">
          <LandPlot className="size-5" />
        </span>
        <span className="font-display text-lg font-semibold">CRM Loteadora</span>
      </div>

      <div>
        <h1 className="text-2xl font-semibold text-slate-900">Bem-vindo de volta</h1>
        <p className="mt-1 text-sm text-slate-500">Entre com sua conta corporativa para continuar.</p>
      </div>

      <div>
        <label className="label" htmlFor="email">E-mail</label>
        <input id="email" className="input py-2.5" type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} placeholder="voce@empresa.com.br" />
      </div>
      <div>
        <label className="label" htmlFor="password">Senha</label>
        <input id="password" className="input py-2.5" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
      </div>
      {needsOrganization && (
        <div>
          <label className="label" htmlFor="org">Organização (identificador)</label>
          <input id="org" className="input py-2.5" value={organization} onChange={(e) => setOrganization(e.target.value)} />
        </div>
      )}

      {error && <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{error}</p>}

      <button className="btn-primary w-full py-2.5" disabled={loading}>
        {loading && <Loader2 className="size-4 animate-spin" />}
        {loading ? "Entrando…" : "Entrar"}
      </button>

      <p className="flex items-center gap-1.5 text-xs text-slate-400">
        <ShieldCheck className="size-3.5" /> Sessão protegida e acessos registrados em auditoria.
      </p>
    </form>
  );
}

const highlights = [
  { icon: Columns3, title: "Funil visual", text: "Acompanhe cada lead do primeiro contato à venda." },
  { icon: CalendarCheck2, title: "Agenda real", text: "Visitas só em horários realmente livres do corretor." },
  { icon: ShieldCheck, title: "Seguro por padrão", text: "Permissões no servidor, auditoria e LGPD desde o início." },
];

export default function LoginPage() {
  return (
    <main className="grid min-h-dvh lg:grid-cols-[1.05fr_1fr]">
      <section className="relative hidden overflow-hidden bg-sidebar p-12 text-white lg:flex lg:flex-col">
        {/* Padrão de lotes (grade) */}
        <svg className="absolute inset-0 size-full opacity-[0.07]" aria-hidden>
          <defs>
            <pattern id="lots" width="56" height="56" patternUnits="userSpaceOnUse" patternTransform="rotate(-12)">
              <path d="M0 0H56V56" fill="none" stroke="white" strokeWidth="1.2" />
            </pattern>
          </defs>
          <rect width="100%" height="100%" fill="url(#lots)" />
        </svg>
        <div className="absolute -right-24 -top-24 size-96 rounded-full bg-brand-500/25 blur-3xl" aria-hidden />
        <div className="absolute -bottom-32 -left-16 size-96 rounded-full bg-brand-700/30 blur-3xl" aria-hidden />

        <div className="relative flex items-center gap-2.5">
          <span className="flex size-10 items-center justify-center rounded-xl bg-gradient-to-br from-brand-400 to-brand-700 shadow-[0_8px_24px_-6px_rgb(18_168_106/0.6)]">
            <LandPlot className="size-5" />
          </span>
          <span className="font-display text-lg font-semibold">CRM Loteadora</span>
        </div>

        <div className="relative mt-auto max-w-md">
          <h2 className="text-3xl font-semibold leading-tight">Gestão comercial para quem vende terra.</h2>
          <p className="mt-3 text-sidebar-text/80">Leads, funil, agenda de visitas e rotina do corretor em um só lugar — com um Copilot que sempre pede autorização.</p>
          <ul className="mt-10 space-y-5">
            {highlights.map(({ icon: Icon, title, text }) => (
              <li key={title} className="flex gap-3">
                <span className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg bg-white/10 ring-1 ring-white/10">
                  <Icon className="size-4 text-brand-300" />
                </span>
                <span>
                  <span className="block text-sm font-medium">{title}</span>
                  <span className="block text-sm text-sidebar-muted">{text}</span>
                </span>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section className="flex items-center justify-center bg-white p-6 sm:p-12">
        <Suspense>
          <LoginForm />
        </Suspense>
      </section>
    </main>
  );
}
