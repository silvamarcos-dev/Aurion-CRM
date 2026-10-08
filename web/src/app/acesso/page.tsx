"use client";

import { LandPlot, Loader2, ShieldAlert } from "lucide-react";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";

/**
 * Entrada por link de acesso: /acesso#<token>.
 * O token vai no FRAGMENTO (#), que o navegador nunca envia ao servidor — não aparece em logs nem em Referer.
 */
export default function AccessLinkPage() {
  const [error, setError] = useState<string | null>(null);
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;

    const token = window.location.hash.slice(1);
    // Remove o token da barra de endereço/histórico imediatamente.
    window.history.replaceState(null, "", "/acesso");

    if (!token) {
      setError("Link incompleto. Peça um novo link a quem compartilhou o acesso.");
      return;
    }

    fetch("/api/auth/access-link", {
      method: "POST",
      headers: { "Content-Type": "application/json", "x-crm-request": "1" },
      body: JSON.stringify({ token }),
    })
      .then(async (response) => {
        if (response.ok) {
          window.location.replace("/kanban");
          return;
        }
        const problem = await response.json().catch(() => ({}));
        setError(problem.title ?? "Link inválido ou expirado.");
      })
      .catch(() => setError("Não foi possível validar o link. Tente novamente."));
  }, []);

  return (
    <main className="flex min-h-dvh items-center justify-center bg-canvas p-6">
      <div className="card w-full max-w-sm p-8 text-center">
        <span className="mx-auto mb-5 flex size-11 items-center justify-center rounded-xl bg-gradient-to-br from-brand-400 to-brand-700 text-white">
          <LandPlot className="size-5" />
        </span>
        {error ? (
          <>
            <ShieldAlert className="mx-auto mb-3 size-6 text-red-500" />
            <h1 className="text-lg font-semibold">Não foi possível entrar</h1>
            <p className="mt-2 text-sm text-slate-500">{error}</p>
            <Link href="/login" className="btn-secondary mt-6">Ir para o login</Link>
          </>
        ) : (
          <>
            <Loader2 className="mx-auto mb-3 size-6 animate-spin text-brand-600" />
            <h1 className="text-lg font-semibold">Abrindo o CRM…</h1>
            <p className="mt-2 text-sm text-slate-500">Validando seu link de acesso.</p>
          </>
        )}
      </div>
    </main>
  );
}
