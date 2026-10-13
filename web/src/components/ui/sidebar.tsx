"use client";

import { useQuery } from "@tanstack/react-query";
import {
  Bot,
  CalendarDays,
  ChevronsLeft,
  ChevronsRight,
  CircleCheckBig,
  Columns3,
  LandPlot,
  Link2,
  LogOut,
  Menu,
  MessageCircle,
  MessageSquarePlus,
  PhoneCall,
  Settings,
  TerminalSquare,
  Users,
  Workflow,
  X,
  Zap,
  type LucideIcon,
} from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { useCan, useMe } from "@/components/providers";
import { Avatar } from "@/components/ui/avatar";
import { api, logout } from "@/lib/api";
import type { InboxCounters, SuggestionStats, TaskItem } from "@/lib/types";

type NavItem = { href: string; label: string; icon: LucideIcon; capability?: string; badgeKey?: "overdueTasks" | "newSuggestions" | "unreadConversations" };
type NavGroup = { label: string; items: NavItem[] };

const groups: NavGroup[] = [
  {
    label: "Comercial",
    items: [
      { href: "/kanban", label: "Funil", icon: Columns3, capability: "leads.read" },
      { href: "/leads", label: "Leads", icon: Users, capability: "leads.read" },
      { href: "/atendimento", label: "Atendimento", icon: MessageCircle, capability: "conversations.read", badgeKey: "unreadConversations" },
    ],
  },
  {
    label: "Rotina",
    items: [
      { href: "/agenda", label: "Agenda", icon: CalendarDays, capability: "calendar.read" },
      { href: "/tarefas", label: "Tarefas", icon: CircleCheckBig, capability: "tasks.read", badgeKey: "overdueTasks" },
      { href: "/macros", label: "Macros", icon: Zap, capability: "macros.use" },
    ],
  },
  {
    label: "Sistema",
    items: [
      { href: "/sugestoes", label: "Sugestões", icon: MessageSquarePlus, capability: "suggestions.create", badgeKey: "newSuggestions" },
      { href: "/desenvolvedor", label: "Desenvolvedor", icon: TerminalSquare, capability: "dev.tools" },
      { href: "/configuracoes#acessos", label: "Acessos externos", icon: Link2, capability: "users.manage" },
    ],
  },
];

const upcoming: { label: string; icon: LucideIcon }[] = [
  { label: "Call Manager", icon: PhoneCall },
  { label: "Automações", icon: Workflow },
  { label: "Copilot", icon: Bot },
];

const COLLAPSED_KEY = "crm.sidebar.collapsed";

function readCollapsed() {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === "1";
  } catch {
    return false;
  }
}

/** Navegação principal: escura, agrupada, recolhível (preferência salva no navegador) e com gaveta no mobile. */
export function AppSidebar() {
  const [collapsed, setCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const pathname = usePathname();

  useEffect(() => {
    setCollapsed(readCollapsed());
  }, []);
  useEffect(() => {
    setMobileOpen(false);
  }, [pathname]);

  function toggle() {
    setCollapsed((c) => {
      try {
        localStorage.setItem(COLLAPSED_KEY, c ? "0" : "1");
      } catch {
        /* armazenamento indisponível: apenas não persiste */
      }
      return !c;
    });
  }

  return (
    <>
      {/* Barra superior mobile */}
      <div className="sticky top-0 z-30 flex items-center justify-between bg-sidebar px-4 py-3 text-white md:hidden">
        <Brand collapsed={false} />
        <button className="rounded-md p-1.5 text-sidebar-text hover:bg-white/10" onClick={() => setMobileOpen(true)} aria-label="Abrir menu">
          <Menu className="size-5" />
        </button>
      </div>

      {mobileOpen && (
        <div className="fixed inset-0 z-40 bg-black/40 backdrop-blur-[1px] md:hidden" onClick={() => setMobileOpen(false)} aria-hidden />
      )}

      <aside
        className={`fixed inset-y-0 left-0 z-50 flex flex-col border-r border-sidebar-border bg-gradient-to-b from-sidebar to-sidebar-2 text-sidebar-text transition-[width,transform] duration-200 md:sticky md:top-0 md:h-dvh md:translate-x-0 ${
          mobileOpen ? "translate-x-0" : "-translate-x-full"
        } ${collapsed ? "w-[68px]" : "w-64"}`}
        aria-label="Navegação principal"
      >
        <div className={`flex items-center ${collapsed ? "justify-center px-2" : "justify-between px-4"} h-16 shrink-0`}>
          <Brand collapsed={collapsed} />
          <button className="rounded-md p-1.5 text-sidebar-muted hover:bg-white/10 hover:text-white md:hidden" onClick={() => setMobileOpen(false)} aria-label="Fechar menu">
            <X className="size-5" />
          </button>
        </div>

        <nav className="flex-1 overflow-y-auto overflow-x-hidden px-3 pb-4">
          {groups.map((group) => (
            <NavSection key={group.label} group={group} collapsed={collapsed} pathname={pathname} />
          ))}

          <div className="mt-6">
            {collapsed ? <div className="mx-auto mb-2 h-px w-6 bg-sidebar-border" /> : <SectionLabel>Em breve</SectionLabel>}
            <ul className="space-y-0.5">
              {upcoming.map(({ label, icon: Icon }) => (
                <li key={label}>
                  <span
                    title={collapsed ? `${label} (em breve)` : undefined}
                    className={`flex cursor-default items-center gap-3 rounded-lg px-2.5 py-2 text-sm text-sidebar-muted/60 ${collapsed ? "justify-center" : ""}`}
                  >
                    <Icon className="size-[18px] shrink-0" strokeWidth={1.75} />
                    {!collapsed && (
                      <>
                        <span className="flex-1 truncate">{label}</span>
                        <span className="rounded border border-sidebar-border px-1.5 text-[10px] uppercase tracking-wide">breve</span>
                      </>
                    )}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        </nav>

        <div className="shrink-0 border-t border-sidebar-border p-3">
          <NavLink item={{ href: "/configuracoes", label: "Configurações", icon: Settings }} collapsed={collapsed} active={pathname.startsWith("/configuracoes")} />
          <UserCard collapsed={collapsed} />
          <button
            onClick={toggle}
            className="mt-2 hidden w-full items-center justify-center gap-2 rounded-lg py-1.5 text-xs text-sidebar-muted hover:bg-white/5 hover:text-white md:flex"
            aria-label={collapsed ? "Expandir menu" : "Recolher menu"}
            title={collapsed ? "Expandir menu" : "Recolher menu"}
          >
            {collapsed ? <ChevronsRight className="size-4" /> : <><ChevronsLeft className="size-4" /> Recolher</>}
          </button>
        </div>
      </aside>
    </>
  );
}

function Brand({ collapsed }: { collapsed: boolean }) {
  const { data: me } = useMe();
  return (
    <Link href="/kanban" className="flex min-w-0 items-center gap-2.5">
      <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-brand-400 to-brand-700 text-white shadow-[0_4px_12px_-2px_rgb(18_168_106/0.5)]">
        <LandPlot className="size-5" strokeWidth={2} />
      </span>
      {!collapsed && (
        <span className="min-w-0 leading-tight">
          <span className="block font-display text-[15px] font-semibold text-white">CRM Loteadora</span>
          <span className="block truncate text-[11px] text-sidebar-muted">{me?.organizationName ?? " "}</span>
        </span>
      )}
    </Link>
  );
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return <p className="mb-1.5 px-2.5 text-[10px] font-semibold uppercase tracking-[0.08em] text-sidebar-muted/80">{children}</p>;
}

function NavSection({ group, collapsed, pathname }: { group: NavGroup; collapsed: boolean; pathname: string }) {
  const can = useCan();
  const items = group.items.filter((i) => !i.capability || can(i.capability));
  if (items.length === 0) return null;

  return (
    <div className="mt-4 first:mt-1">
      {collapsed ? <div className="mx-auto mb-2 h-px w-6 bg-sidebar-border" /> : <SectionLabel>{group.label}</SectionLabel>}
      <ul className="space-y-0.5">
        {items.map((item) => (
          <li key={item.href}>
            <NavLink item={item} collapsed={collapsed} active={pathname.startsWith(item.href.split("#")[0]) && !item.href.includes("#")} />
          </li>
        ))}
      </ul>
    </div>
  );
}

function NavLink({ item, collapsed, active }: { item: NavItem; collapsed: boolean; active: boolean }) {
  const badge = useBadge(item.badgeKey);
  const Icon = item.icon;
  return (
    <Link
      href={item.href}
      title={collapsed ? item.label : undefined}
      aria-current={active ? "page" : undefined}
      className={`group relative flex items-center gap-3 rounded-lg px-2.5 py-2 text-sm transition ${collapsed ? "justify-center" : ""} ${
        active ? "bg-white/10 font-medium text-white" : "text-sidebar-text/85 hover:bg-white/5 hover:text-white"
      }`}
    >
      {active && <span className="absolute inset-y-1.5 -left-3 w-1 rounded-r-full bg-brand-400" aria-hidden />}
      <Icon className={`size-[18px] shrink-0 ${active ? "text-brand-300" : "text-sidebar-muted group-hover:text-sidebar-text"}`} strokeWidth={1.9} />
      {!collapsed && <span className="flex-1 truncate">{item.label}</span>}
      {badge > 0 && (
        <span
          className={`rounded-full bg-red-500 text-[10px] font-semibold text-white ${collapsed ? "absolute right-1.5 top-1 size-2 p-0" : "min-w-5 px-1.5 py-0.5 text-center"}`}
          aria-label={item.badgeKey === "newSuggestions" ? `${badge} novas` : item.badgeKey === "unreadConversations" ? `${badge} não lidas` : `${badge} atrasadas`}
        >
          {collapsed ? "" : badge > 99 ? "99+" : badge}
        </span>
      )}
    </Link>
  );
}

function useBadge(key?: NavItem["badgeKey"]) {
  const can = useCan();
  const { data: me } = useMe();
  const tasksEnabled = key === "overdueTasks" && can("tasks.read");
  const suggestionsEnabled = key === "newSuggestions" && can("suggestions.manage");
  const inboxEnabled = key === "unreadConversations" && can("conversations.reply");
  const inbox = useQuery({
    queryKey: ["conversations", "counters"],
    queryFn: () => api.get<InboxCounters>("conversations/counters"),
    enabled: inboxEnabled,
    refetchInterval: 15_000,
  });
  const tasks = useQuery({
    queryKey: ["tasks", "overdue-badge"],
    queryFn: () => api.get<TaskItem[]>("tasks?status=Open&due=Overdue&limit=100"),
    enabled: tasksEnabled && !me?.isGuest,
    refetchInterval: 120_000,
  });
  const suggestions = useQuery({
    queryKey: ["suggestions", "stats"],
    queryFn: () => api.get<SuggestionStats>("suggestions/stats"),
    enabled: suggestionsEnabled,
    refetchInterval: 120_000,
  });
  if (tasksEnabled) return tasks.data?.length ?? 0;
  if (suggestionsEnabled) return suggestions.data?.new ?? 0;
  if (inboxEnabled) return inbox.data?.mineUnread ?? 0;
  return 0;
}

function UserCard({ collapsed }: { collapsed: boolean }) {
  const { data: me } = useMe();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setOpen(false);
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  if (!me) return <div className="mt-2 h-12" />;

  return (
    <div ref={ref} className="relative mt-2">
      <button
        onClick={() => setOpen((o) => !o)}
        className={`flex w-full items-center gap-2.5 rounded-lg p-2 text-left transition hover:bg-white/5 ${collapsed ? "justify-center" : ""}`}
        aria-haspopup="menu"
        aria-expanded={open}
        title={collapsed ? me.displayName : undefined}
      >
        <Avatar name={me.displayName} size="md" className="ring-sidebar" />
        {!collapsed && (
          <span className="min-w-0 flex-1 leading-tight">
            <span className="block truncate text-sm font-medium text-white">{me.displayName}</span>
            <span className="block truncate text-xs text-sidebar-muted">{me.role}</span>
          </span>
        )}
      </button>

      {open && (
        <div role="menu" className={`absolute bottom-full z-50 mb-2 w-60 rounded-xl border border-slate-200 bg-white p-1.5 text-slate-700 shadow-pop ${collapsed ? "left-0" : "left-0 right-0 w-auto"}`}>
          <div className="px-2.5 py-2">
            <p className="truncate text-sm font-medium text-slate-900">{me.displayName}</p>
            <p className="truncate text-xs text-slate-500">{me.email}</p>
            <p className="mt-1 truncate text-xs text-slate-500">{me.organizationName}</p>
          </div>
          <div className="my-1 h-px bg-slate-100" />
          <Link role="menuitem" href="/configuracoes" className="flex items-center gap-2 rounded-lg px-2.5 py-2 text-sm hover:bg-slate-50" onClick={() => setOpen(false)}>
            <Settings className="size-4 text-slate-400" /> Minha jornada e preferências
          </Link>
          <button role="menuitem" onClick={logout} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-sm text-red-600 hover:bg-red-50">
            <LogOut className="size-4" /> Sair
          </button>
        </div>
      )}
    </div>
  );
}
