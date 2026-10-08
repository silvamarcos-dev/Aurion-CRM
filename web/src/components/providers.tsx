"use client";

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
        <div className="pointer-events-none fixed bottom-4 right-4 z-50 flex flex-col gap-2" role="status" aria-live="polite">
          {toasts.map((t) => (
            <div
              key={t.id}
              className={`pointer-events-auto rounded-lg px-4 py-3 text-sm shadow-lg ring-1 ${
                t.tone === "error"
                  ? "bg-red-50 text-red-800 ring-red-200"
                  : t.tone === "success"
                    ? "bg-emerald-50 text-emerald-800 ring-emerald-200"
                    : "bg-white text-slate-800 ring-slate-200"
              }`}
            >
              {t.message}
            </div>
          ))}
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
