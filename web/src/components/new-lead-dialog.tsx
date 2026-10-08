"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { Modal } from "@/components/modal";
import { useCan, useMe, useToast } from "@/components/providers";
import { api, ApiError } from "@/lib/api";
import type { UserSummary } from "@/lib/types";

export function NewLeadDialog({ open, onClose, stageId }: { open: boolean; onClose: () => void; stageId?: string }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const can = useCan();
  const { data: me } = useMe();
  const canAssign = can("leads.assign");
  const { data: assignable } = useQuery({
    queryKey: ["assignable"],
    queryFn: () => api.get<UserSummary[]>("users/assignable"),
    enabled: open && canAssign,
  });

  const [form, setForm] = useState({ fullName: "", phone: "", email: "", source: "", campaign: "", interest: "", ownerUserId: "" });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [key]: e.target.value }));

  const create = useMutation({
    mutationFn: () =>
      api.post<{ id: string }>("leads", {
        ...form,
        phone: form.phone || null,
        email: form.email || null,
        ownerUserId: form.ownerUserId || null,
        stageId: stageId ?? null,
      }),
    onSuccess: () => {
      toast("Lead criado.", "success");
      queryClient.invalidateQueries({ queryKey: ["board"] });
      queryClient.invalidateQueries({ queryKey: ["leads"] });
      setForm({ fullName: "", phone: "", email: "", source: "", campaign: "", interest: "", ownerUserId: "" });
      onClose();
    },
    onError: (error) => toast(error instanceof ApiError ? error.message : "Falha ao criar lead.", "error"),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate();
  }

  return (
    <Modal title="Novo lead" open={open} onClose={onClose}>
      <form onSubmit={submit} className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <label className="label" htmlFor="nl-name">Nome *</label>
          <input id="nl-name" className="input" required maxLength={200} value={form.fullName} onChange={set("fullName")} />
        </div>
        <div>
          <label className="label" htmlFor="nl-phone">Telefone / WhatsApp</label>
          <input id="nl-phone" className="input" inputMode="tel" placeholder="(11) 98765-4321" value={form.phone} onChange={set("phone")} />
        </div>
        <div>
          <label className="label" htmlFor="nl-email">E-mail</label>
          <input id="nl-email" className="input" type="email" value={form.email} onChange={set("email")} />
        </div>
        <div>
          <label className="label" htmlFor="nl-source">Origem</label>
          <input id="nl-source" className="input" maxLength={80} placeholder="Meta Ads, Site, Indicação…" value={form.source} onChange={set("source")} />
        </div>
        <div>
          <label className="label" htmlFor="nl-campaign">Campanha</label>
          <input id="nl-campaign" className="input" maxLength={120} value={form.campaign} onChange={set("campaign")} />
        </div>
        <div className="sm:col-span-2">
          <label className="label" htmlFor="nl-interest">Interesse</label>
          <input id="nl-interest" className="input" maxLength={500} value={form.interest} onChange={set("interest")} />
        </div>
        {canAssign && (
          <div className="sm:col-span-2">
            <label className="label" htmlFor="nl-owner">Corretor responsável</label>
            <select id="nl-owner" className="input" value={form.ownerUserId} onChange={set("ownerUserId")}>
              <option value="">{me ? `Eu (${me.displayName})` : "Eu"}</option>
              {assignable
                ?.filter((u) => u.userId !== me?.userId)
                .map((u) => (
                  <option key={u.userId} value={u.userId}>
                    {u.displayName} — {u.role}
                  </option>
                ))}
            </select>
          </div>
        )}
        <div className="mt-2 flex justify-end gap-2 sm:col-span-2">
          <button type="button" className="btn-secondary" onClick={onClose}>Cancelar</button>
          <button className="btn-primary" disabled={create.isPending}>{create.isPending ? "Salvando…" : "Criar lead"}</button>
        </div>
      </form>
    </Modal>
  );
}
