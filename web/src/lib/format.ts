const dateTime = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short", timeZone: "America/Sao_Paulo" });
const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });

export const formatDateTime = (iso: string | null | undefined) => (iso ? dateTime.format(new Date(iso)) : "—");

export const formatCents = (cents: number | null | undefined) => (cents == null ? "—" : currency.format(cents / 100));

export function formatRelative(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const minutes = Math.round(diff / 60_000);
  if (minutes < 1) return "agora";
  if (minutes < 60) return `há ${minutes} min`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `há ${hours} h`;
  const days = Math.round(hours / 24);
  return `há ${days} d`;
}

export function formatPhone(e164: string | null): string {
  if (!e164) return "—";
  const m = e164.match(/^\+55(\d{2})(\d{4,5})(\d{4})$/);
  return m ? `(${m[1]}) ${m[2]}-${m[3]}` : e164;
}

export const timeframeLabels: Record<string, string> = {
  Unknown: "Não informado",
  Immediate: "Imediato",
  UpTo3Months: "Até 3 meses",
  UpTo6Months: "Até 6 meses",
  UpTo12Months: "Até 12 meses",
  Over12Months: "Mais de 12 meses",
};

export const statusLabels: Record<string, string> = {
  Open: "Em aberto",
  Won: "Venda",
  Lost: "Perdido",
  Archived: "Arquivado",
};

export const actionLabels: Record<string, string> = {
  "lead.created": "Lead criado",
  "lead.updated": "Dados alterados",
  "lead.assigned": "Responsável alterado",
  "lead.stage_changed": "Etapa alterada",
};
