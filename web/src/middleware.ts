import { NextResponse, type NextRequest } from "next/server";

/**
 * Redireciona para /login quando não há sessão. É apenas conveniência de navegação:
 * a proteção real dos dados está na API (token + RBAC).
 */
export function middleware(request: NextRequest) {
  if (!request.cookies.has("crm_rt")) {
    const url = new URL("/login", request.url);
    url.searchParams.set("next", request.nextUrl.pathname);
    return NextResponse.redirect(url);
  }
  return NextResponse.next();
}

export const config = {
  matcher: ["/((?!login|api|_next|favicon.ico).*)"],
};
