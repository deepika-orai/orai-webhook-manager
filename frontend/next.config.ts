import type { NextConfig } from "next";

export function validateAndGetProxyTarget(
  targetEnv?: string,
  nodeEnv: string = process.env.NODE_ENV || "development"
): string {
  const isProd = nodeEnv === "production";
  const rawTarget = targetEnv?.trim();

  if (!rawTarget) {
    if (isProd) {
      throw new Error(
        "[next.config.ts] Missing required environment variable 'API_PROXY_TARGET' for production build. " +
          "Configure API_PROXY_TARGET=https://oraiapi.azurewebsites.net in Vercel environment settings."
      );
    }
    return "http://localhost:5135";
  }

  let parsed: URL;
  try {
    parsed = new URL(rawTarget);
  } catch {
    throw new Error(
      `[next.config.ts] Invalid API_PROXY_TARGET: '${rawTarget}'. Must be a valid absolute URL.`
    );
  }

  // Reject credentials in URL
  if (parsed.username || parsed.password) {
    throw new Error(
      `[next.config.ts] API_PROXY_TARGET must not contain user credentials.`
    );
  }

  // Reject query string or hash fragment
  if (parsed.search || parsed.hash) {
    throw new Error(
      `[next.config.ts] API_PROXY_TARGET must not contain query strings or hash fragments.`
    );
  }

  // Reject unexpected subpaths
  if (parsed.pathname !== "/" && parsed.pathname !== "") {
    throw new Error(
      `[next.config.ts] API_PROXY_TARGET must be an origin without subpaths (found: '${parsed.pathname}').`
    );
  }

  // Production target MUST use HTTPS
  if (isProd && parsed.protocol !== "https:") {
    throw new Error(
      `[next.config.ts] Production API_PROXY_TARGET must use HTTPS (received: '${parsed.protocol}').`
    );
  }

  // Development may allow HTTP only for localhost / 127.0.0.1
  if (!isProd && parsed.protocol === "http:") {
    const isLocalhost =
      parsed.hostname === "localhost" ||
      parsed.hostname === "127.0.0.1" ||
      parsed.hostname === "::1";
    if (!isLocalhost) {
      throw new Error(
        `[next.config.ts] HTTP is only permitted for localhost in development (received hostname: '${parsed.hostname}').`
      );
    }
  } else if (!isProd && parsed.protocol !== "https:") {
    throw new Error(
      `[next.config.ts] Invalid protocol in API_PROXY_TARGET: '${parsed.protocol}'. Expected http: or https:.`
    );
  }

  return parsed.origin;
}

export function getProxyRewrites(target: string) {
  // Explicit allowlist of browser application API routes used by the frontend
  // NOTE: Public webhook ingestion (/backend-api/webhooks/*) is intentionally excluded and never proxied.
  const allowlistedRoutes = [
    "auth",
    "admin",
    "dashboard",
    "messages",
    "webhook-endpoints",
  ];

  return allowlistedRoutes.flatMap((route) => [
    {
      source: `/backend-api/${route}`,
      destination: `${target}/api/${route}`,
    },
    {
      source: `/backend-api/${route}/:path*`,
      destination: `${target}/api/${route}/:path*`,
    },
  ]);
}

const nextConfig: NextConfig = {
  async rewrites() {
    const target = validateAndGetProxyTarget(process.env.API_PROXY_TARGET);
    return getProxyRewrites(target);
  },
};

export default nextConfig;
