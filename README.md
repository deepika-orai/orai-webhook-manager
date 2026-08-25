# ORAI Webhook Manager

Secure multi-tenant WhatsApp webhook status monitoring system.

> **Note**: This repository is currently at **Phase 1 — Project Foundation**. It contains the foundational backend (.NET 10) and frontend (Next.js 16) project structures. Database schemas, authentication, webhook processing, and dashboard views will be implemented in subsequent phases.

---

## Technology Stack

- **Backend**: ASP.NET Core Web API on .NET 10 LTS (C# 14)
- **Frontend**: Next.js (App Router), React 19, TypeScript, Tailwind CSS
- **Package Managers**: `dotnet` CLI, `pnpm`
- **Future Database**: PostgreSQL 17 (EF Core + Dapper / Npgsql) (Phase 2+)
- **Target Hosting**:
  - Backend: Azure App Service / Container Apps
  - Frontend: Azure Static Web Apps

---

## Repository Structure

```
orai-webhook-manager/
├── backend/
│   ├── OraiWebhookManager.sln
│   ├── .env.example
│   ├── src/
│   │   ├── OraiWebhookManager.Api/
│   │   │   ├── Controllers/
│   │   │   │   └── HealthController.cs
│   │   │   ├── Models/
│   │   │   │   └── HealthResponse.cs
│   │   │   ├── Properties/
│   │   │   │   └── launchSettings.json
│   │   │   ├── appsettings.json
│   │   │   ├── appsettings.Development.json
│   │   │   ├── Program.cs
│   │   │   └── OraiWebhookManager.Api.csproj
│   │   ├── OraiWebhookManager.Application/
│   │   ├── OraiWebhookManager.Domain/
│   │   └── OraiWebhookManager.Infrastructure/
│   └── tests/
│       ├── OraiWebhookManager.UnitTests/
│       │   └── HealthControllerTests.cs
│       └── OraiWebhookManager.IntegrationTests/
│           └── HealthEndpointTests.cs
├── frontend/
│   ├── public/
│   ├── src/
│   │   └── app/
│   │       ├── globals.css
│   │       ├── layout.tsx
│   │       └── page.tsx
│   ├── .env.example
│   ├── eslint.config.mjs
│   ├── next.config.ts
│   ├── package.json
│   ├── pnpm-lock.yaml
│   ├── postcss.config.mjs
│   └── tsconfig.json
├── docs/
│   └── architecture-overview.md
├── .env.example
├── .gitignore
└── README.md
```

---

## Local Development & Build Instructions

### Backend (.NET 10 LTS)

#### Prerequisites
- .NET 10 SDK (v10.0+)

#### Restore & Build (Command Line)
```powershell
# Restore NuGet packages
dotnet restore backend\OraiWebhookManager.sln

# Build backend solution (Release)
dotnet build backend\OraiWebhookManager.sln -c Release

# Run tests
dotnet test backend\OraiWebhookManager.sln -c Release
```

#### Run Backend API Locally
```powershell
dotnet run --project backend\src\OraiWebhookManager.Api\OraiWebhookManager.Api.csproj
```

- Health Check Endpoint: `GET http://localhost:5135/api/health` or `https://localhost:7036/api/health`
- Scalar API Reference (Dev): `GET http://localhost:5135/scalar/v1`

---

### Frontend (Next.js + TypeScript)

#### Prerequisites
- Node.js (v20+)
- pnpm (v10+)

#### Install, Run & Build
```powershell
cd frontend

# Install dependencies
pnpm install

# Run development server
pnpm dev

# Run linter
pnpm lint

# Production build
pnpm build

# Start production server
pnpm start
```

Open [http://localhost:3000](http://localhost:3000) in your browser.
