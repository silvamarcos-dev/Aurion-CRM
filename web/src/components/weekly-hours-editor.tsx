"use client";

import { weekdayNames } from "@/lib/time";
import type { WeeklyInterval } from "@/lib/types";

const short = (t: string) => t.slice(0, 5);
export const toApiTime = (t: string) => (t.length === 5 ? `${t}:00` : t);

/** Editor de intervalos semanais (vários intervalos por dia; lacunas = pausas). */
export function WeeklyHoursEditor({ value, onChange, disabled }: { value: WeeklyInterval[]; onChange: (v: WeeklyInterval[]) => void; disabled?: boolean }) {
  const update = (index: number, patch: Partial<WeeklyInterval>) =>
    onChange(value.map((iv, i) => (i === index ? { ...iv, ...patch } : iv)));
  const remove = (index: number) => onChange(value.filter((_, i) => i !== index));
  const add = (day: number) => {
    const last = value.filter((v) => v.isoWeekday === day).at(-1);
    onChange([...value, { isoWeekday: day, start: last ? last.end : "08:00:00", end: last ? "18:00:00" : "12:00:00" }]);
  };

  return (
    <div className="divide-y divide-slate-100 rounded-lg border border-slate-200">
      {[1, 2, 3, 4, 5, 6, 7].map((day) => {
        const items = value.map((iv, index) => ({ iv, index })).filter((x) => x.iv.isoWeekday === day);
        return (
          <div key={day} className="flex flex-wrap items-center gap-2 px-3 py-2">
            <span className="w-20 text-sm font-medium text-slate-700">{weekdayNames[day]}</span>
            {items.length === 0 && <span className="text-xs text-slate-400">Sem expediente</span>}
            {items.map(({ iv, index }) => (
              <span key={index} className="flex items-center gap-1 rounded-md bg-slate-50 px-1.5 py-1 ring-1 ring-slate-200">
                <input aria-label={`${weekdayNames[day]} início`} type="time" className="rounded border-0 bg-transparent text-sm" value={short(iv.start)} disabled={disabled}
                  onChange={(e) => update(index, { start: toApiTime(e.target.value) })} />
                <span className="text-xs text-slate-400">–</span>
                <input aria-label={`${weekdayNames[day]} fim`} type="time" className="rounded border-0 bg-transparent text-sm" value={short(iv.end)} disabled={disabled}
                  onChange={(e) => update(index, { end: toApiTime(e.target.value) })} />
                {!disabled && (
                  <button type="button" aria-label="Remover intervalo" className="px-1 text-slate-400 hover:text-red-600" onClick={() => remove(index)}>×</button>
                )}
              </span>
            ))}
            {!disabled && (
              <button type="button" className="text-xs text-brand-700 hover:underline" onClick={() => add(day)}>+ intervalo</button>
            )}
          </div>
        );
      })}
    </div>
  );
}
