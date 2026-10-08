import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { isSameOriginRequest } from "@/server/csrf";
import { API_BASE_URL, storeTokens, type TokenPair } from "@/server/session";

const LoginSchema = z.object({
  email: z.string().email().max(254),
  password: z.string().min(1).max(256),
  organization: z.string().max(80).optional(),
});

export async function POST(request: NextRequest) {
  if (!isSameOriginRequest(request)) {
    return NextResponse.json({ title: "Requisição recusada." }, { status: 403 });
  }

  const parsed = LoginSchema.safeParse(await request.json().catch(() => null));
  if (!parsed.success) {
    return NextResponse.json({ title: "Informe e-mail e senha válidos." }, { status: 400 });
  }

  const response = await fetch(`${API_BASE_URL}/api/v1/auth/login`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      // Propaga o IP do cliente para o rate limiting da API (configurar proxy confiável em produção).
      "X-Forwarded-For": request.headers.get("x-forwarded-for") ?? "",
    },
    body: JSON.stringify(parsed.data),
    cache: "no-store",
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => ({ title: "Falha no login." }));
    return NextResponse.json({ title: problem.title ?? "Falha no login." }, { status: response.status });
  }

  await storeTokens((await response.json()) as TokenPair);
  return NextResponse.json({ ok: true });
}
