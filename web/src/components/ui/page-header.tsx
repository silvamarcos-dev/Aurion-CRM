import type { ReactNode } from "react";

/** Cabeçalho padrão das páginas: título, descrição curta e ações à direita. */
export function PageHeader({
  title,
  description,
  actions,
  children,
  sticky = false,
}: {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  children?: ReactNode;
  sticky?: boolean;
}) {
  return (
    <header className={`border-b border-slate-200/80 bg-white/85 backdrop-blur ${sticky ? "sticky top-0 z-20" : ""}`}>
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-4 sm:px-6 lg:px-8">
        <div className="min-w-0">
          <h1 className="truncate text-xl font-semibold text-slate-900">{title}</h1>
          {description && <p className="mt-0.5 text-sm text-slate-500">{description}</p>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      {children}
    </header>
  );
}

export function EmptyState({ icon, title, description, action }: { icon?: ReactNode; title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-slate-300 bg-white/60 px-6 py-10 text-center">
      {icon && <div className="mb-3 flex size-10 items-center justify-center rounded-full bg-brand-50 text-brand-700">{icon}</div>}
      <p className="text-sm font-medium text-slate-900">{title}</p>
      {description && <p className="mt-1 max-w-sm text-sm text-slate-500">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

/** Abas sublinhadas reutilizáveis. */
export function Tabs<T extends string>({ value, onChange, items }: { value: T; onChange: (v: T) => void; items: { key: T; label: ReactNode; badge?: ReactNode }[] }) {
  return (
    <div className="flex gap-1 overflow-x-auto px-4 sm:px-6 lg:px-8" role="tablist">
      {items.map((t) => (
        <button
          key={t.key}
          role="tab"
          aria-selected={value === t.key}
          onClick={() => onChange(t.key)}
          className={`-mb-px flex items-center gap-1.5 border-b-2 px-3 py-2.5 text-sm whitespace-nowrap transition ${
            value === t.key ? "border-brand-600 font-medium text-brand-800" : "border-transparent text-slate-500 hover:text-slate-900"
          }`}
        >
          {t.label}
          {t.badge}
        </button>
      ))}
    </div>
  );
}
