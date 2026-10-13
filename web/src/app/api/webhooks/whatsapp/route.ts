import { type NextRequest } from "next/server";
import { API_BASE_URL } from "@/server/session";

/**
 * Webhook público do WhatsApp (Meta) → API. A API não é exposta à internet; este repasse
 * mantém o corpo EXATO (a assinatura HMAC é calculada sobre os bytes originais) e o cabeçalho de assinatura.
 * Nenhuma lógica aqui: validação, idempotência e processamento acontecem na API.
 */
export async function GET(request: NextRequest) {
  const response = await fetch(`${API_BASE_URL}/webhooks/whatsapp${request.nextUrl.search}`, { cache: "no-store" });
  return new Response(await response.text(), { status: response.status, headers: { "Content-Type": "text/plain" } });
}

export async function POST(request: NextRequest) {
  const body = await request.text();
  if (body.length > 1_000_000) return new Response(null, { status: 413 });

  const response = await fetch(`${API_BASE_URL}/webhooks/whatsapp`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Hub-Signature-256": request.headers.get("x-hub-signature-256") ?? "",
      "X-Forwarded-For": request.headers.get("x-forwarded-for") ?? "",
    },
    body,
    cache: "no-store",
  });
  return new Response(null, { status: response.status });
}
