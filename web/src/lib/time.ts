/**
 * Utilitários de fuso horário. O backend trabalha em UTC e informa o fuso da organização;
 * a interface sempre exibe e interpreta horários NO FUSO DA ORGANIZAÇÃO (não no do navegador).
 */
export const ORG_TZ = "America/Sao_Paulo";

type Parts = { year: number; month: number; day: number; hour: number; minute: number; weekday: number };

const formatters = new Map<string, Intl.DateTimeFormat>();
function formatter(tz: string) {
  let f = formatters.get(tz);
  if (!f) {
    f = new Intl.DateTimeFormat("en-US", {
      timeZone: tz,
      hourCycle: "h23",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      weekday: "short",
    });
    formatters.set(tz, f);
  }
  return f;
}

const WEEKDAYS: Record<string, number> = { Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6, Sun: 7 };

export function zonedParts(iso: string | Date, tz = ORG_TZ): Parts {
  const parts = Object.fromEntries(formatter(tz).formatToParts(new Date(iso)).map((p) => [p.type, p.value]));
  return {
    year: Number(parts.year),
    month: Number(parts.month),
    day: Number(parts.day),
    hour: Number(parts.hour),
    minute: Number(parts.minute),
    weekday: WEEKDAYS[parts.weekday as string],
  };
}

/** "2026-10-09" do instante, no fuso informado. */
export function zonedDate(iso: string | Date, tz = ORG_TZ): string {
  const p = zonedParts(iso, tz);
  return `${p.year}-${pad(p.month)}-${pad(p.day)}`;
}

export function zonedTime(iso: string | Date, tz = ORG_TZ): string {
  const p = zonedParts(iso, tz);
  return `${pad(p.hour)}:${pad(p.minute)}`;
}

/** Converte data + hora locais do fuso informado em ISO UTC. */
export function zonedToIso(date: string, time: string, tz = ORG_TZ): string {
  const [y, m, d] = date.split("-").map(Number);
  const [hh, mm] = time.split(":").map(Number);
  const guess = Date.UTC(y, m - 1, d, hh, mm);
  const p = zonedParts(new Date(guess), tz);
  const asIfUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute);
  const offset = asIfUtc - guess;
  return new Date(guess - offset).toISOString();
}

export function todayInZone(tz = ORG_TZ): string {
  return zonedDate(new Date(), tz);
}

export function addDays(date: string, days: number): string {
  const [y, m, d] = date.split("-").map(Number);
  const dt = new Date(Date.UTC(y, m - 1, d + days));
  return dt.toISOString().slice(0, 10);
}

/** Segunda-feira da semana da data (ISO). */
export function startOfWeek(date: string): string {
  const [y, m, d] = date.split("-").map(Number);
  const weekday = new Date(Date.UTC(y, m - 1, d)).getUTCDay() || 7;
  return addDays(date, 1 - weekday);
}

export function minutesOfDay(time: string): number {
  const [h, m] = time.split(":").map(Number);
  return h * 60 + m;
}

export const pad = (n: number) => String(n).padStart(2, "0");

export const weekdayNames = ["", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo"];
export const weekdayShort = ["", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];

export function formatDayLabel(date: string): string {
  const [y, m, d] = date.split("-").map(Number);
  const weekday = new Date(Date.UTC(y, m - 1, d)).getUTCDay() || 7;
  return `${weekdayShort[weekday]} ${pad(d)}/${pad(m)}`;
}
