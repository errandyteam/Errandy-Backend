START TRANSACTION;

CREATE TABLE "AutomaticPaymentChecks" (
    "Id" uuid NOT NULL,
    "PaymentIntentId" uuid NOT NULL,
    "Status" character varying(24) NOT NULL,
    "LastResult" character varying(24) NOT NULL,
    "AttemptsStarted" integer NOT NULL,
    "MaximumAttempts" integer NOT NULL,
    "NextAttemptAt" timestamp with time zone,
    "LeaseToken" uuid,
    "LeaseExpiresAt" timestamp with time zone,
    "LastStartedAt" timestamp with time zone,
    "LastFinishedAt" timestamp with time zone,
    "Revision" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_AutomaticPaymentChecks" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AutomaticPaymentChecks_PaymentIntents_PaymentIntentId" FOREIGN KEY ("PaymentIntentId") REFERENCES "PaymentIntents" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_AutomaticPaymentChecks_PaymentIntentId" ON "AutomaticPaymentChecks" ("PaymentIntentId");

CREATE INDEX "IX_AutomaticPaymentChecks_Status_LeaseExpiresAt_Id" ON "AutomaticPaymentChecks" ("Status", "LeaseExpiresAt", "Id");

CREATE INDEX "IX_AutomaticPaymentChecks_Status_NextAttemptAt_Id" ON "AutomaticPaymentChecks" ("Status", "NextAttemptAt", "Id");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119071155_AddAutomaticPaymentChecks', '8.0.8');

COMMIT;

