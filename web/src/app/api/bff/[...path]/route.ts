import { NextResponse, type NextRequest } from "next/server";
import { isSameOriginRequest } from "@/server/csrf";
import { API_BASE_URL, clearTokens, readTokens, refreshTokens, storeTokens } from "@/server/session";

/**
 * Proxy autenticado do BFF → API (.NET).
 * - Anexa o access token a partir do cookie httpOnly.
 * - Renova a sessão de forma transparente uma vez em caso de 401.
 * - Apenas prefixos de rota conhecidos são encaminhados.
 * A AUTORIZAÇÃO continua sendo responsabilidade exclusiva da API.
 */
const ALLOWED_ROOTS = new Set(["me", "users", "leads", "board", "pipelines", "calendar", "agenda", "appointments", "tasks", "macros", "access-links", "suggestions", "dev"]);

type RouteContext = { params: Promise<{ path: string[] }> };

async function forward(request: NextRequest, context: RouteContext) {
  const { path } = await context.params;
  if (!ALLOWED_ROOTS.has(path[0]) || path.some((segment) => segment === "." || segment === ".." || segment === "")) {
    return NextResponse.json({ title: "Rota não encontrada." }, { status: 404 });
  }
  const target = path.map(encodeURIComponent).join("/");

  const mutating = request.method !== "GET" && request.method !== "HEAD";
  if (mutating && !isSameOriginRequest(request)) {
    return NextResponse.json({ title: "Requisição recusada." }, { status: 403 });
  }

  const body = mutating ? await request.text() : undefined;
  const url = `${API_BASE_URL}/api/v1/${target}${request.nextUrl.search}`;
  let { accessToken, refreshToken } = await readTokens();

  const call = (token?: string) =>
    fetch(url, {
      method: request.method,
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(body ? { "Content-Type": "application/json" } : {}),
      },
      body,
      cache: "no-store",
    });

  let response = accessToken ? await call(accessToken) : null;

  if ((!response || response.status === 401) && refreshToken) {
    const renewed = await refreshTokens(refreshToken);
    if (renewed) {
      await storeTokens(renewed);
      accessToken = renewed.accessToken;
      refreshToken = renewed.refreshToken;
      response = await call(accessToken);
    } else {
      await clearTokens();
    }
  }

  if (!response) {
    return NextResponse.json({ title: "Sessão expirada." }, { status: 401 });
  }

  const payload = await response.arrayBuffer();
  return new NextResponse(response.status === 204 ? null : payload, {
    status: response.status,
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "application/json",
      "Cache-Control": "no-store",
    },
  });
}

export { forward as GET, forward as POST, forward as PUT, forward as PATCH, forward as DELETE };
