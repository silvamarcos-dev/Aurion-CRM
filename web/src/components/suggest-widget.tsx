"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Bug, Crosshair, Lightbulb, MessageSquarePlus, Sparkles, Type, X } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useCan, useToast } from "@/components/providers";
import { api, ApiError } from "@/lib/api";
import type { SuggestionKind } from "@/lib/types";

const kinds: { key: SuggestionKind; label: string; icon: typeof Bug; hint: string }[] = [
  { key: "Improvement", label: "Melhoria", icon: Sparkles, hint: "Algo que pode ficar melhor" },
  { key: "Bug", label: "Problema", icon: Bug, hint: "Algo não funciona como deveria" },
  { key: "Idea", label: "Ideia", icon: Lightbulb, hint: "Uma funcionalidade nova" },
  { key: "Text", label: "Texto", icon: Type, hint: "Erro de escrita ou rótulo confuso" },
];

type Picked = { descriptor: string; rect: DOMRect };

/**
 * Botão flutuante "Sugerir mudança" presente em todas as telas.
 * Captura automaticamente a página e, opcionalmente, o ELEMENTO apontado — nunca dados de formulários.
 */
export function SuggestWidget() {
  const can = useCan();
  const pathname = usePathname();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [picking, setPicking] = useState(false);
  const [hover, setHover] = useState<DOMRect | null>(null);
  const [picked, setPicked] = useState<Picked | null>(null);
  const [kind, setKind] = useState<SuggestionKind>("Improvement");
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");

  const reset = () => {
    setTitle("");
    setDescription("");
    setPicked(null);
    setKind("Improvement");
  };

  const send = useMutation({
    mutationFn: () =>
      api.post<{ id: string }>("suggestions", {
        kind,
        title,
        description,
        pagePath: pathname,
        pageTitle: document.querySelector("main h1")?.textContent?.trim().slice(0, 140) ?? null,
        elementDescriptor: picked?.descriptor ?? null,
        viewport: `${window.innerWidth}x${window.innerHeight}`,
      }),
    onSuccess: () => {
      toast("Sugestão enviada. Obrigado! Acompanhe em Sugestões.", "success");
      queryClient.invalidateQueries({ queryKey: ["suggestions"] });
      reset();
      setOpen(false);
    },
    onError: (err) => toast(err instanceof ApiError ? err.message : "Não foi possível enviar.", "error"),
  });

  // ───── Modo "apontar elemento" ─────
  const stopPicking = useCallback(() => {
    setPicking(false);
    setHover(null);
    setOpen(true);
  }, []);

  useEffect(() => {
    if (!picking) return;
    const isOwn = (el: Element | null) => Boolean(el?.closest("[data-suggest-widget]"));

    const onMove = (e: MouseEvent) => {
      const el = document.elementFromPoint(e.clientX, e.clientY);
      setHover(el && !isOwn(el) ? el.getBoundingClientRect() : null);
    };
    const onClick = (e: MouseEvent) => {
      const el = document.elementFromPoint(e.clientX, e.clientY);
      if (!el || isOwn(el)) return;
      e.preventDefault();
      e.stopPropagation();
      setPicked({ descriptor: describe(el), rect: el.getBoundingClientRect() });
      stopPicking();
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && stopPicking();

    document.addEventListener("mousemove", onMove, true);
    document.addEventListener("click", onClick, true);
    document.addEventListener("keydown", onKey, true);
    document.body.style.cursor = "crosshair";
    return () => {
      document.removeEventListener("mousemove", onMove, true);
      document.removeEventListener("click", onClick, true);
      document.removeEventListener("keydown", onKey, true);
      document.body.style.cursor = "";
    };
  }, [picking, stopPicking]);

  if (!can("suggestions.create")) return null;

  function submit(e: FormEvent) {
    e.preventDefault();
    send.mutate();
  }

  return (
    <div data-suggest-widget>
      {picking && (
        <>
          {hover && (
            <div className="pointer-events-none fixed z-[70] rounded-md border-2 border-violet-500 bg-violet-500/10 transition-all duration-75"
              style={{ top: hover.top - 3, left: hover.left - 3, width: hover.width + 6, height: hover.height + 6 }} />
          )}
          <div className="fixed left-1/2 top-4 z-[71] -translate-x-1/2 rounded-full bg-slate-900 px-4 py-2 text-sm text-white shadow-pop">
            Clique no elemento da tela · <kbd className="rounded bg-white/15 px-1.5 text-xs">Esc</kbd> para cancelar
          </div>
        </>
      )}

      {!open && !picking && (
        <button
          onClick={() => setOpen(true)}
          className="fixed bottom-5 right-5 z-40 flex items-center gap-2 rounded-full bg-slate-900 py-2.5 pl-3.5 pr-4 text-sm font-medium text-white shadow-pop transition hover:bg-slate-800 md:bottom-6 md:right-6"
          aria-label="Sugerir mudança"
        >
          <MessageSquarePlus className="size-4" /> Sugerir mudança
        </button>
      )}

      {open && !picking && (
        <form onSubmit={submit} className="fixed inset-x-3 bottom-3 z-50 rounded-2xl border border-slate-200 bg-white shadow-pop sm:inset-x-auto sm:bottom-6 sm:right-6 sm:w-[380px]">
          <div className="flex items-start justify-between border-b border-slate-100 px-5 py-4">
            <div>
              <h2 className="text-base font-semibold">Sugerir mudança</h2>
              <p className="text-xs text-slate-500">Página: <code className="text-slate-700">{pathname}</code></p>
            </div>
            <button type="button" onClick={() => setOpen(false)} className="-mr-2 rounded-lg p-1.5 text-slate-400 hover:bg-slate-100" aria-label="Fechar">
              <X className="size-4" />
            </button>
          </div>

          <div className="space-y-3 px-5 py-4">
            <div className="grid grid-cols-4 gap-1.5" role="radiogroup" aria-label="Tipo de sugestão">
              {kinds.map(({ key, label, icon: Icon, hint }) => (
                <button key={key} type="button" role="radio" aria-checked={kind === key} title={hint} onClick={() => setKind(key)}
                  className={`flex flex-col items-center gap-1 rounded-lg border px-1 py-2 text-[11px] font-medium transition ${
                    kind === key ? "border-brand-500 bg-brand-50 text-brand-800" : "border-slate-200 text-slate-600 hover:border-slate-300"
                  }`}>
                  <Icon className="size-4" /> {label}
                </button>
              ))}
            </div>

            <div>
              <label className="label" htmlFor="sg-title">Resumo</label>
              <input id="sg-title" className="input" required maxLength={140} value={title} onChange={(e) => setTitle(e.target.value)}
                placeholder={kind === "Bug" ? "Ex.: botão Salvar não responde" : "Ex.: filtrar leads por campanha"} />
            </div>
            <div>
              <label className="label" htmlFor="sg-desc">Detalhes</label>
              <textarea id="sg-desc" className="input min-h-24" required maxLength={4000} value={description} onChange={(e) => setDescription(e.target.value)}
                placeholder="O que você esperava? Como deveria ficar?" />
              <p className="mt-1 text-[11px] text-slate-400">Evite incluir dados de clientes (telefone, CPF, etc.).</p>
            </div>

            <div className="rounded-lg border border-dashed border-slate-300 p-2.5">
              {picked ? (
                <div className="flex items-start gap-2 text-xs">
                  <Crosshair className="mt-0.5 size-3.5 shrink-0 text-violet-600" />
                  <span className="min-w-0 flex-1 break-words text-slate-600">{picked.descriptor.split(" ⟶ ")[0]}</span>
                  <button type="button" className="text-slate-400 hover:text-red-600" onClick={() => setPicked(null)} aria-label="Remover elemento">
                    <X className="size-3.5" />
                  </button>
                </div>
              ) : (
                <button type="button" onClick={() => { setOpen(false); setPicking(true); }} className="flex w-full items-center justify-center gap-2 text-xs font-medium text-violet-700 hover:text-violet-900">
                  <Crosshair className="size-3.5" /> Apontar o elemento na tela (opcional)
                </button>
              )}
            </div>
          </div>

          <div className="flex items-center justify-between border-t border-slate-100 px-5 py-3">
            <Link href="/sugestoes" className="text-xs text-slate-500 hover:text-brand-700" onClick={() => setOpen(false)}>Ver sugestões</Link>
            <button className="btn-primary" disabled={send.isPending}>{send.isPending ? "Enviando…" : "Enviar sugestão"}</button>
          </div>
        </form>
      )}
    </div>
  );
}

const hidePii = (text: string) => text.replace(/\s+/g, " ").trim().replace(/\d{4,}/g, "••••").replace(/\S+@\S+/g, "•••@•••");

const tagNames: Record<string, string> = {
  a: "link", button: "botão", h1: "título", h2: "título", h3: "título", label: "rótulo", th: "coluna",
  td: "célula", input: "campo", select: "seleção", textarea: "campo de texto", img: "imagem", svg: "ícone",
  table: "tabela", tr: "linha", li: "item", ul: "lista", nav: "menu", aside: "barra lateral", form: "formulário",
  section: "seção", header: "cabeçalho", article: "card", p: "texto", span: "texto", div: "área",
};

/**
 * Descrição legível + caminho técnico do elemento. Texto só é capturado de elementos de INTERFACE
 * (botões, links, títulos, rótulos, cabeçalhos de coluna, placeholders); conteúdo de células, cards
 * e textos livres NÃO é registrado. Sequências numéricas e e-mails são ocultados.
 */
function describe(el: Element): string {
  const ui = el.closest("button, a, label, h1, h2, h3, th, [role=tab], [role=button], [role=radio]");
  const target = ui ?? el;
  const tag = target.tagName.toLowerCase();
  let human = target.getAttribute("role") === "tab" ? "aba" : (tagNames[tag] ?? tag);

  if (ui) {
    const name = hidePii(ui.getAttribute("aria-label") ?? ui.textContent ?? "").slice(0, 60);
    if (name) human += ` "${name}"`;
  } else if (tag === "input" || tag === "textarea" || tag === "select") {
    const field = target as HTMLInputElement;
    const label = (field.id && document.querySelector(`label[for="${field.id}"]`)?.textContent) || field.getAttribute("aria-label") || field.getAttribute("placeholder");
    if (label) human += ` "${hidePii(label).slice(0, 60)}"`;
  } else {
    // Célula de tabela: informa a COLUNA (cabeçalho), nunca o conteúdo.
    const cell = el.closest("td");
    const table = cell?.closest("table");
    if (cell && table) {
      const index = Array.from(cell.parentElement?.children ?? []).indexOf(cell);
      const header = table.querySelectorAll("thead th")[index]?.textContent;
      human = `célula da coluna "${hidePii(header ?? "?").slice(0, 40)}"`;
    }
    const region = el.closest("[aria-label]");
    if (region && region !== el) human += ` (${tagNames[region.tagName.toLowerCase()] ?? "área"} "${hidePii(region.getAttribute("aria-label") ?? "").slice(0, 40)}")`;
  }

  const section = target.closest("[role=dialog], section[aria-label], article")?.getAttribute("aria-label")
    ?? document.querySelector("main h1")?.textContent;
  if (section) human += ` · tela "${hidePii(section).slice(0, 40)}"`;

  return `${human} ⟶ ${cssPath(target)}`.slice(0, 600);
}

function cssPath(el: Element): string {
  const parts: string[] = [];
  let node: Element | null = el;
  while (node && node.tagName !== "BODY" && parts.length < 6) {
    let part = node.tagName.toLowerCase();
    const label = node.getAttribute("aria-label");
    if (node.id && !/\d{3,}/.test(node.id)) part += `#${node.id}`;
    else if (label) part += `[aria-label="${label.replace(/\d{4,}/g, "••••").slice(0, 30)}"]`;
    else if (node.parentElement) {
      const index = Array.from(node.parentElement.children).indexOf(node) + 1;
      part += `:nth-child(${index})`;
    }
    parts.unshift(part);
    node = node.parentElement;
  }
  return parts.join(" > ");
}
