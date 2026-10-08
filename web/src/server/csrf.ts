import "server-only";
import type { NextRequest } from "next/server";

export const CSRF_HEADER = "x-crm-request";

/**
 * Defesa CSRF para rotas do BFF que alteram estado:
 *  - cookie de sessão SameSite;
 *  - header customizado obrigatório (não enviável por formulário cross-site sem CORS);
 *  - Origin, quando presente, deve ser o próprio host.
 */
export function isSameOriginRequest(request: NextRequest): boolean {
  if (request.headers.get(CSRF_HEADER) !== "1") return false;

  const origin = request.headers.get("origin");
  if (!origin) return true;

  try {
    return new URL(origin).host === request.headers.get("host");
  } catch {
    return false;
  }
}
