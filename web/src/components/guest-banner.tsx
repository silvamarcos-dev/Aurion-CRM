"use client";

import { Code2, Eye } from "lucide-react";
import Link from "next/link";
import { useMe } from "@/components/providers";
import { formatDateTime } from "@/lib/format";

/** Faixa fixa que deixa claro quando a sessão é de um convidado (somente leitura, dados mascarados). */
export function GuestBanner() {
  const { data: me } = useMe();
  if (!me?.isGuest) return null;

  const developer = me.role === "Desenvolvedor";
  const Icon = developer ? Code2 : Eye;

  return (
    <div className={`flex flex-wrap items-center gap-x-3 gap-y-1 border-b px-4 py-2 text-sm sm:px-6 lg:px-8 ${developer ? "border-violet-200 bg-violet-50 text-violet-900" : "border-sky-200 bg-sky-50 text-sky-900"}`}>
      <span className="flex items-center gap-2 font-medium">
        <Icon className="size-4" /> {developer ? "Modo desenvolvedor" : "Modo visitante"}
      </span>
      <span className="text-xs opacity-80">
        Somente leitura · dados pessoais de clientes mascarados
        {me.accessExpiresAt ? ` · acesso até ${formatDateTime(me.accessExpiresAt)}` : ""}
      </span>
      <span className="ml-auto flex gap-3 text-xs font-medium">
        {developer && <Link href="/desenvolvedor" className="underline-offset-2 hover:underline">Painel técnico</Link>}
        <Link href="/sugestoes" className="underline-offset-2 hover:underline">Minhas sugestões</Link>
      </span>
    </div>
  );
}
