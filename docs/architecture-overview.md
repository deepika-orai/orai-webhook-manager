# ORAI Webhook Manager — Architecture Overview

## Summary
ORAI Webhook Manager is a multi-tenant webhook status monitoring solution for WhatsApp integrations.

## Tech Stack
- **Backend**: ASP.NET Core Web API on .NET 10 LTS (C# 14)
- **Frontend**: Next.js (App Router, TypeScript, Tailwind CSS)
- **Future Database (Phase 2+)**: PostgreSQL 17 (EF Core for application data, Dapper + Npgsql for high-throughput webhook ingestion)
- **Future Messaging (Phase 3+)**: PostgreSQL durable inbox with Azure Service Bus readiness
- **Documentation / OpenAPI**: Built-in .NET OpenAPI with Scalar UI
- **Testing**: xUnit with FluentAssertions & WebApplicationFactory

## Current Status (Phase 1)
- Modernized .NET 10 backend solution initialized with Clean Architecture layers:
  - `OraiWebhookManager.Api` (Presentation, Controllers, OpenAPI/Scalar, CORS)
  - `OraiWebhookManager.Application` (Business logic, interfaces, use cases)
  - `OraiWebhookManager.Domain` (Core domain models and rules)
  - `OraiWebhookManager.Infrastructure` (Persistence & external service implementations)
  - `OraiWebhookManager.UnitTests` (Unit test foundation)
  - `OraiWebhookManager.IntegrationTests` (Integration test foundation)
- Foundational `GET /api/health` endpoint implemented and verified.
- Next.js TypeScript application with Tailwind CSS.
- Database schemas, authentication, and webhook processing logic will be introduced in subsequent phases.
