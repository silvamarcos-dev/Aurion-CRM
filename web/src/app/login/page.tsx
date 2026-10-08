"use client";

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
    <form onSubmit={submit} className="card w-full max-w-sm space-y-4 p-8">
      <div>
        <div className="mb-1 flex items-center gap-2">
          <span className="inline-block size-3 rounded-sm bg-emerald-700" aria-hidden />
          <h1 className="text-lg font-semibold">CRM Loteadora</h1>
        </div>
        <p className="text-sm text-slate-500">Acesse com sua conta corporativa.</p>
      </div>

      <div>
        <label className="label" htmlFor="email">E-mail</label>
        <input id="email" className="input" type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} />
      </div>
      <div>
        <label className="label" htmlFor="password">Senha</label>
        <input id="password" className="input" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
      </div>
      {needsOrganization && (
        <div>
          <label className="label" htmlFor="org">Organização (identificador)</label>
          <input id="org" className="input" value={organization} onChange={(e) => setOrganization(e.target.value)} />
        </div>
      )}

      {error && <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <button className="btn-primary w-full" disabled={loading}>
        {loading ? "Entrando…" : "Entrar"}
      </button>
    </form>
  );
}

export default function LoginPage() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-slate-100 p-4">
      <Suspense>
        <LoginForm />
      </Suspense>
    </main>
  );
}
