import { NextResponse, type NextRequest } from "next/server";
import { isSameOriginRequest } from "@/server/csrf";
import { API_BASE_URL, clearTokens, readTokens } from "@/server/session";

export async function POST(request: NextRequest) {
  if (!isSameOriginRequest(request)) {
    return NextResponse.json({ title: "Requisição recusada." }, { status: 403 });
  }

  const { refreshToken } = await readTokens();
  if (refreshToken) {
    await fetch(`${API_BASE_URL}/api/v1/auth/logout`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    }).catch(() => undefined);
  }

  await clearTokens();
  return NextResponse.json({ ok: true });
}
