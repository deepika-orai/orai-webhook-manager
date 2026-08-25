# Database Migrations & Deployment Guide

This directory contains version-controlled, deployment-ready idempotent SQL migration scripts for ORAI Webhook Manager PostgreSQL databases (local development and Azure Database for PostgreSQL).

---

## 1. Directory Structure & Architecture

```text
backend/
├── migrations/                                           # Deployment-ready idempotent SQL scripts
│   ├── README.md                                         # This documentation
│   ├── 001_phase2_postgresql_foundation.sql              # Initial Phase 2 core multi-tenant schema
│   └── 002_auth_super_admin_onboarding.sql               # Phase 4 Auth, auth_version and must_change_password
└── src/
    └── OraiWebhookManager.Infrastructure/
        └── Persistence/
            └── Migrations/                               # EF Core migration C# source files & snapshot
                ├── 20260825073218_Phase2_PostgreSqlFoundation.cs
                ├── 20260825073218_Phase2_PostgreSqlFoundation.Designer.cs
                ├── 20260825105204_Phase4_AuthSuperAdminOnboarding.cs
                ├── 20260825105204_Phase4_AuthSuperAdminOnboarding.Designer.cs
                └── AppDbContextModelSnapshot.cs
```

---

## 2. Migration Execution Rules & Policies

1. **No Automatic Startup Migrations in Production**:
   - `DbContext.Database.Migrate()` **MUST NEVER** be executed automatically at application startup.
   - *Rationale*: Running migrations on app startup causes race conditions during multi-instance scale-outs (e.g. Azure App Service / AKS), requires the API runtime identity to have elevated DDL permissions, and risks uncoordinated table-level lock contention.
2. **Immutability of Applied Migrations**:
   - **Never edit an already-deployed migration**.
   - If a schema change or bugfix is required, always create the next sequentially numbered migration (e.g., `002_add_field.sql`).
3. **Strict Separation of Concerns**:
   - Schema DDL migrations must remain separate from reference data / seed scripts.
4. **Idempotency & UTF-8 Encoding**:
   - All SQL migration files are generated using EF Core idempotent script generation (`--idempotent`).
   - All SQL files **MUST** be saved as **UTF-8 without BOM** so CLI utilities (`psql`, Azure CLI) can execute them directly without syntax errors.
5. **No Embedded Secrets**:
   - Scripts and configuration files must never contain passwords, real tokens, or tenant data.

---

## 3. Migration Sequence & Chronology

- `001_phase2_postgresql_foundation.sql`: Core multi-tenant foundation (tenants, users, tenant_memberships, webhook_endpoints, webhook_inbox, messages, message_status_events, audit_logs).
- `002_auth_super_admin_onboarding.sql`: Phase 4 Authentication, auth_version, must_change_password column, Super Admin and client onboarding foundation.

---

## 4. Local Execution

### Option A: Direct PostgreSQL CLI (`psql`)
Run the idempotent SQL script against your local PostgreSQL database:

```bash
# PowerShell / Bash
psql -h localhost -p 5432 -U postgres -d orai_webhooks -f backend/migrations/001_phase2_postgresql_foundation.sql
```

### Option B: .NET EF CLI (Development Only)
```powershell
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=orai_webhooks;Username=postgres;Password=YOUR_DEV_PASSWORD;"
dotnet ef database update --context AppDbContext --project backend/src/OraiWebhookManager.Infrastructure --startup-project backend/src/OraiWebhookManager.Api
```

---

## 5. Azure PostgreSQL Deployment Approach

In production and staging environments on Azure (e.g., Azure Database for PostgreSQL Flexible Server):

1. **Pre-Deployment Backup**:
   - Always initiate or verify an on-demand server backup before running DDL migrations:
   ```bash
   az postgres flexible-server backup create \
     --resource-group <rg-name> \
     --name <server-name> \
     --backup-name pre_migration_backup_$(date +%Y%m%d%H%M%S)
   ```
2. **Dedicated CI/CD Release Pipeline**:
   - Execute migrations as a dedicated pre-deployment release gate in GitHub Actions / Azure DevOps using an administrative pipeline service principal or Azure Managed Identity with DDL privileges.
   - Example Azure CLI / `psql` deployment step:
   ```bash
   psql "host=<server-name>.postgres.database.azure.com port=5432 dbname=orai_webhooks user=<admin-user> sslmode=require" \
     -f backend/migrations/001_phase2_postgresql_foundation.sql
   ```

---

## 6. Verification via `__EFMigrationsHistory`

Each idempotent migration records its execution state in the `__EFMigrationsHistory` table upon successful transaction commit.

To verify applied migrations in the database:
```sql
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" ASC;
```

Expected Output:
```text
                  MigrationId                  | ProductVersion 
-----------------------------------------------+----------------
 20260825073218_Phase2_PostgreSqlFoundation     | 10.0.11
```
