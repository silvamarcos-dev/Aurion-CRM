import type { Stage } from "@/lib/types";

/** Badge da etapa do funil usando a cor configurada na etapa. */
export function StageBadge({ stage, name }: { stage?: Pick<Stage, "name" | "color"> | null; name?: string }) {
  const color = stage?.color ?? "#94a3b8";
  return (
    <span
      className="chip whitespace-nowrap ring-1 ring-inset"
      style={{ background: `${color}14`, color: shade(color), ["--tw-ring-color" as string]: `${color}40` }}
    >
      <span className="size-1.5 rounded-full" style={{ background: color }} aria-hidden />
      {stage?.name ?? name ?? "—"}
    </span>
  );
}

/** Escurece a cor para garantir contraste do texto sobre o fundo claro. */
function shade(hex: string) {
  const n = parseInt(hex.replace("#", ""), 16);
  if (Number.isNaN(n)) return "#334155";
  const f = 0.55;
  const r = Math.round(((n >> 16) & 255) * f);
  const g = Math.round(((n >> 8) & 255) * f);
  const b = Math.round((n & 255) * f);
  return `rgb(${r} ${g} ${b})`;
}
