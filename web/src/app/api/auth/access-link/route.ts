import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { isSameOriginRequest } from "@/server/csrf";
import { API_BASE_URL, storeTokens, type TokenPair } from "@/server/session";

const Schema = z.object({ token: z.string().min(16).max(128) });

/** Troca o token do link de acesso (visitante/desenvolvedor) por uma sessão em cookies httpOnly. */
export async function POST(request: NextRequest) {
  if (!isSameOriginRequest(request)) {
    return NextResponse.json({ title: "Requisição recusada." }, { status: 403 });
  }

  const parsed = Schema.safeParse(await request.json().catch(() => null));
  if (!parsed.success) {
    return NextResponse.json({ title: "Link inválido ou expirado." }, { status: 401 });
  }

  const response = await fetch(`${API_BASE_URL}/api/v1/auth/access-link`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Forwarded-For": request.headers.get("x-forwarded-for") ?? "" },
    body: JSON.stringify(parsed.data),
    cache: "no-store",
  });

  if (!response.ok) {
    return NextResponse.json({ title: response.status === 429 ? "Muitas tentativas. Aguarde um minuto." : "Link inválido ou expirado." }, { status: response.status });
  }

  await storeTokens((await response.json()) as TokenPair);
  return NextResponse.json({ ok: true });
}
