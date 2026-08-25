START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825105204_Phase4_AuthSuperAdminOnboarding') THEN
    ALTER TABLE users ADD auth_version integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825105204_Phase4_AuthSuperAdminOnboarding') THEN
    ALTER TABLE users ADD must_change_password boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825105204_Phase4_AuthSuperAdminOnboarding') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260825105204_Phase4_AuthSuperAdminOnboarding', '10.0.11');
    END IF;
END $EF$;
COMMIT;
