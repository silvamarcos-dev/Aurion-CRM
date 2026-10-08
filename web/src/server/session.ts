import "server-only";
import { cookies } from "next/headers";

/**
 * BFF: os tokens da API ficam APENAS em cookies httpOnly no domínio do frontend.
 * O JavaScript do navegador nunca tem acesso a eles.
 */
export const API_BASE_URL = process.env.CRM_API_URL ?? "http://localhost:5180";

const ACCESS_COOKIE = "crm_at";
export const REFRESH_COOKIE = "crm_rt";
const secure = process.env.NODE_ENV === "production";

export type TokenPair = {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
};

export async function readTokens() {
  const jar = await cookies();
  return {
    accessToken: jar.get(ACCESS_COOKIE)?.value,
    refreshToken: jar.get(REFRESH_COOKIE)?.value,
  };
}

export async function storeTokens(tokens: TokenPair) {
  const jar = await cookies();
  jar.set(ACCESS_COOKIE, tokens.accessToken, {
    httpOnly: true,
    secure,
    sameSite: "lax",
    path: "/",
    expires: new Date(tokens.accessTokenExpiresAt),
  });
  jar.set(REFRESH_COOKIE, tokens.refreshToken, {
    httpOnly: true,
    secure,
    sameSite: "lax",
    path: "/",
    expires: new Date(tokens.refreshTokenExpiresAt),
  });
}

export async function clearTokens() {
  const jar = await cookies();
  jar.delete(ACCESS_COOKIE);
  jar.delete(REFRESH_COOKIE);
}

/**
 * Single-flight: requisições paralelas que encontram o access token expirado
 * compartilham a MESMA chamada de refresh (evita disparar detecção de reuso).
 */
const inflightRefresh = new Map<string, Promise<TokenPair | null>>();

export function refreshTokens(refreshToken: string): Promise<TokenPair | null> {
  const existing = inflightRefresh.get(refreshToken);
  if (existing) return existing;

  const promise = (async () => {
    try {
      const response = await fetch(`${API_BASE_URL}/api/v1/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken }),
        cache: "no-store",
      });
      return response.ok ? ((await response.json()) as TokenPair) : null;
    } finally {
      setTimeout(() => inflightRefresh.delete(refreshToken), 10_000);
    }
  })();

  inflightRefresh.set(refreshToken, promise);
  return promise;
}
