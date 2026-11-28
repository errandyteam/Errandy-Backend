START TRANSACTION;

CREATE TABLE "PaymentIntents" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "WalletId" uuid NOT NULL,
    "TransactionId" uuid,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Provider" character varying(50) NOT NULL,
    "Reference" character varying(100) NOT NULL,
    "CustomerEmail" character varying(200) NOT NULL,
    "AuthorizationUrl" character varying(500),
    "AccessCode" character varying(120),
    "Status" integer NOT NULL,
    "Channel" character varying(60),
    "GatewayResponse" character varying(250),
    "PaidAt" timestamp with time zone,
    "VerifiedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_PaymentIntents" PRIMARY KEY ("Id")
);

CREATE TABLE "WithdrawalRequests" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "WalletId" uuid NOT NULL,
    "TransactionId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL DEFAULT 'NGN',
    "BankName" character varying(120) NOT NULL,
    "BankCode" character varying(20),
    "AccountNumber" character varying(20) NOT NULL,
    "AccountName" character varying(150) NOT NULL,
    "Status" integer NOT NULL,
    "Reference" character varying(100) NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL,
    "EligibleAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    "ProcessedByAdminId" uuid,
    "AdminNote" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_WithdrawalRequests" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX "IX_PaymentIntents_Reference" ON "PaymentIntents" ("Reference");

CREATE INDEX "IX_PaymentIntents_UserId_CreatedAt" ON "PaymentIntents" ("UserId", "CreatedAt");

CREATE UNIQUE INDEX "IX_WithdrawalRequests_Reference" ON "WithdrawalRequests" ("Reference");

CREATE INDEX "IX_WithdrawalRequests_Status_EligibleAt" ON "WithdrawalRequests" ("Status", "EligibleAt");

CREATE INDEX "IX_WithdrawalRequests_UserId_RequestedAt" ON "WithdrawalRequests" ("UserId", "RequestedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261116173816_AddPaymentIntents', '8.0.8');

COMMIT;

START TRANSACTION;

CREATE TABLE "PaymentReconciliationAttempts" (
    "Id" uuid NOT NULL,
    "PaymentIntentId" uuid NOT NULL,
    "AdminUserId" uuid NOT NULL,
    "Reference" character varying(100) NOT NULL,
    "Reason" character varying(500) NOT NULL,
    "Outcome" character varying(20) NOT NULL,
    "ResultCode" character varying(100),
    "RequestedAt" timestamp with time zone NOT NULL,
    "FinishedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_PaymentReconciliationAttempts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PaymentReconciliationAttempts_PaymentIntents_PaymentIntentId" FOREIGN KEY ("PaymentIntentId") REFERENCES "PaymentIntents" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PaymentReconciliationAttempts_Users_AdminUserId" FOREIGN KEY ("AdminUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_PaymentReconciliationAttempts_AdminUserId_RequestedAt" ON "PaymentReconciliationAttempts" ("AdminUserId", "RequestedAt");

CREATE INDEX "IX_PaymentReconciliationAttempts_Outcome_RequestedAt" ON "PaymentReconciliationAttempts" ("Outcome", "RequestedAt");

CREATE INDEX "IX_PaymentReconciliationAttempts_PaymentIntentId_RequestedAt" ON "PaymentReconciliationAttempts" ("PaymentIntentId", "RequestedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261118144529_AddPaymentReconciliationAttempts', '8.0.8');

COMMIT;

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

START TRANSACTION;

 ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT TRUE;

 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "BankAccountName" text NULL;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "BankAccountNumber" text NULL;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "BankName" text NULL;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "BankVerificationStatus" integer NOT NULL DEFAULT 0;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "Bvn" text NULL;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "GovernmentIdImageUrl" text NULL;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "GovernmentIdNumber" text NULL;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "GovernmentIdType" text NULL;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "GovernmentIdVerificationStatus" integer NOT NULL DEFAULT 0;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "Level" integer NOT NULL DEFAULT 0;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "PhoneVerificationStatus" integer NOT NULL DEFAULT 0;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "SelfieImageUrl" text NULL;
 ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "SelfieVerificationStatus" integer NOT NULL DEFAULT 0;


INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261124131808_AddVerificationStatusesToRunner', '8.0.8');

COMMIT;

