"use client";

import { CircleAlert, CircleCheck, Info } from "lucide-react";
import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import { createContext, useCallback, useContext, useState, type ReactNode } from "react";
import { api } from "@/lib/api";
import type { Me } from "@/lib/types";

type Toast = { id: number; message: string; tone: "info" | "error" | "success" };
const ToastContext = createContext<(message: string, tone?: Toast["tone"]) => void>(() => {});

export function useToast() {
  return useContext(ToastContext);
}

export function Providers({ children }: { children: ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: { queries: { staleTime: 15_000, retry: 1, refetchOnWindowFocus: true } },
      }),
  );
  const [toasts, setToasts] = useState<Toast[]>([]);

  const push = useCallback((message: string, tone: Toast["tone"] = "info") => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t, { id, message, tone }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4500);
  }, []);

  return (
    <QueryClientProvider client={client}>
      <ToastContext.Provider value={push}>
        {children}
        <div className="pointer-events-none fixed right-4 top-4 z-[60] flex w-[calc(100%-2rem)] max-w-sm flex-col gap-2" role="status" aria-live="polite">
          {toasts.map((t) => {
            const Icon = t.tone === "error" ? CircleAlert : t.tone === "success" ? CircleCheck : Info;
            return (
              <div key={t.id} className="pointer-events-auto flex items-start gap-3 rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-800 shadow-pop">
                <Icon className={`mt-0.5 size-4 shrink-0 ${t.tone === "error" ? "text-red-600" : t.tone === "success" ? "text-brand-600" : "text-sky-600"}`} />
                <span>{t.message}</span>
              </div>
            );
          })}
        </div>
      </ToastContext.Provider>
    </QueryClientProvider>
  );
}

export function useMe() {
  return useQuery({ queryKey: ["me"], queryFn: () => api.get<Me>("me"), staleTime: 60_000 });
}

/** Apenas para exibir/ocultar controles. A API sempre revalida a permissão. */
export function useCan() {
  const { data } = useMe();
  return (capability: string) => Boolean(data?.capabilities[capability]);
}
