START TRANSACTION;

DROP TABLE "AutomaticPaymentChecks";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261119071155_AddAutomaticPaymentChecks';

COMMIT;

