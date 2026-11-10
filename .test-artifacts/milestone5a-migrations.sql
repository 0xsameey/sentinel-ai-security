CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029173437_InitialCreate') THEN
    CREATE TABLE audit_events (
        "Id" uuid NOT NULL,
        "EventType" character varying(100) NOT NULL,
        "ActorId" character varying(128),
        "PaymentId" character varying(64),
        "DetailsJson" jsonb,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_audit_events" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029173437_InitialCreate') THEN
    CREATE INDEX "IX_audit_events_CreatedAtUtc" ON audit_events ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029173437_InitialCreate') THEN
    CREATE INDEX "IX_audit_events_PaymentId" ON audit_events ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029173437_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261029173437_InitialCreate', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261030115009_AddAuthChallenges') THEN
    CREATE TABLE auth_challenges (
        "Id" uuid NOT NULL,
        "AccountId" character varying(128) NOT NULL,
        "NonceBase64" character varying(44) NOT NULL,
        "Message" character varying(512) NOT NULL,
        "Recipient" character varying(256) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "ConsumedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_auth_challenges" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261030115009_AddAuthChallenges') THEN
    CREATE INDEX "IX_auth_challenges_ExpiresAtUtc" ON auth_challenges ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261030115009_AddAuthChallenges') THEN
    CREATE UNIQUE INDEX "IX_auth_challenges_NonceBase64" ON auth_challenges ("NonceBase64");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261030115009_AddAuthChallenges') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261030115009_AddAuthChallenges', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031235955_AddPaymentTracking') THEN
    CREATE TABLE payment_requests (
        "Id" uuid NOT NULL,
        "PaymentId" character varying(64) NOT NULL,
        "IdempotencyKey" character varying(100) NOT NULL,
        "OwnerAccountId" character varying(128) NOT NULL,
        "ContractId" character varying(128) NOT NULL,
        "Recipient" character varying(128) NOT NULL,
        "AmountYocto" character varying(39) NOT NULL,
        "ExpiresAtNs" character varying(20) NOT NULL,
        "Status" character varying(32) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CompletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_payment_requests" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031235955_AddPaymentTracking') THEN
    CREATE TABLE payment_transaction_attempts (
        "Id" uuid NOT NULL,
        "PaymentRequestId" uuid NOT NULL,
        "Operation" character varying(32) NOT NULL,
        "SignerAccountId" character varying(128) NOT NULL,
        "TransactionHash" character varying(128),
        "Status" character varying(32) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "VerifiedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_payment_transaction_attempts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_payment_transaction_attempts_payment_requests_PaymentReques~" FOREIGN KEY ("PaymentRequestId") REFERENCES payment_requests ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031235955_AddPaymentTracking') THEN
    CREATE UNIQUE INDEX "IX_payment_requests_ContractId_OwnerAccountId_IdempotencyKey" ON payment_requests ("ContractId", "OwnerAccountId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031235955_AddPaymentTracking') THEN
    CREATE INDEX "IX_payment_requests_CreatedAtUtc" ON payment_requests ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031235955_AddPaymentTracking') THEN
    CREATE UNIQUE INDEX "IX_payment_requests_PaymentId" ON payment_requests ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031235955_AddPaymentTracking') THEN
    CREATE INDEX "IX_payment_transaction_attempts_PaymentRequestId_Operation" ON payment_transaction_attempts ("PaymentRequestId", "Operation");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031235955_AddPaymentTracking') THEN
    CREATE INDEX "IX_payment_transaction_attempts_TransactionHash" ON payment_transaction_attempts ("TransactionHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031235955_AddPaymentTracking') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261031235955_AddPaymentTracking', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101002600_UniqueTransactionHash') THEN
    DROP INDEX "IX_payment_transaction_attempts_TransactionHash";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101002600_UniqueTransactionHash') THEN
    CREATE UNIQUE INDEX "IX_payment_transaction_attempts_TransactionHash" ON payment_transaction_attempts ("TransactionHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101002600_UniqueTransactionHash') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261101002600_UniqueTransactionHash', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101154640_AddPaymentRiskAssessment') THEN
    ALTER TABLE payment_requests ADD "AiExplanation" character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101154640_AddPaymentRiskAssessment') THEN
    ALTER TABLE payment_requests ADD "Purpose" character varying(500) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101154640_AddPaymentRiskAssessment') THEN
    ALTER TABLE payment_requests ADD "RiskDecision" character varying(16) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101154640_AddPaymentRiskAssessment') THEN
    ALTER TABLE payment_requests ADD "RiskLevel" character varying(16) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101154640_AddPaymentRiskAssessment') THEN
    ALTER TABLE payment_requests ADD "RiskReasonsJson" jsonb NOT NULL DEFAULT ('[]'::jsonb);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101154640_AddPaymentRiskAssessment') THEN
    ALTER TABLE payment_requests ADD "RiskScore" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101154640_AddPaymentRiskAssessment') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261101154640_AddPaymentRiskAssessment', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    ALTER TABLE payment_requests ADD "SecurityApprovalId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    ALTER TABLE payment_requests ADD "SecurityAssessmentId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    ALTER TABLE payment_requests ADD "SecurityIntentHash" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE TABLE counterparty_blocklist (
        "Id" uuid NOT NULL,
        "AccountId" character varying(64) NOT NULL,
        "ReasonCode" character varying(64) NOT NULL,
        "EvidenceUri" character varying(1024) NOT NULL,
        "EvidenceSha256" character varying(64) NOT NULL,
        "RecordedBy" character varying(64) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone,
        "RevokedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_counterparty_blocklist" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE TABLE security_assessments (
        "Id" uuid NOT NULL,
        "OwnerId" character varying(64) NOT NULL,
        "ActorId" character varying(64) NOT NULL,
        "ContractId" character varying(64) NOT NULL,
        "PaymentId" character varying(64),
        "Stage" character varying(16) NOT NULL,
        "IntentHash" character varying(64) NOT NULL,
        "IntentJson" jsonb NOT NULL,
        "DecisionJson" jsonb NOT NULL,
        "Fingerprint" character varying(64) NOT NULL,
        "Decision" character varying(8) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_security_assessments" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE TABLE human_approvals (
        "Id" uuid NOT NULL,
        "AssessmentId" uuid NOT NULL,
        "OwnerId" character varying(64) NOT NULL,
        "IntentHash" character varying(64) NOT NULL,
        "Fingerprint" character varying(64) NOT NULL,
        "PaymentId" character varying(64),
        "Stage" character varying(16) NOT NULL,
        "ConsumedByPaymentId" character varying(64),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "ConsumedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_human_approvals" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_human_approvals_security_assessments_AssessmentId" FOREIGN KEY ("AssessmentId") REFERENCES security_assessments ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE TABLE execution_permits (
        "PaymentId" character varying(64) NOT NULL,
        "PermitId" uuid NOT NULL,
        "AssessmentId" uuid NOT NULL,
        "ApprovalId" uuid,
        "IntentHash" character varying(64) NOT NULL,
        "Fingerprint" character varying(64) NOT NULL,
        "SignerPublicKey" character varying(128) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "ConsumedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_execution_permits" PRIMARY KEY ("PaymentId"),
        CONSTRAINT "FK_execution_permits_human_approvals_ApprovalId" FOREIGN KEY ("ApprovalId") REFERENCES human_approvals ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_execution_permits_security_assessments_AssessmentId" FOREIGN KEY ("AssessmentId") REFERENCES security_assessments ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE INDEX "IX_payment_requests_SecurityApprovalId" ON payment_requests ("SecurityApprovalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE INDEX "IX_payment_requests_SecurityAssessmentId" ON payment_requests ("SecurityAssessmentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE INDEX "IX_counterparty_blocklist_AccountId" ON counterparty_blocklist ("AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE INDEX "IX_execution_permits_ApprovalId" ON execution_permits ("ApprovalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE INDEX "IX_execution_permits_AssessmentId" ON execution_permits ("AssessmentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE UNIQUE INDEX "IX_execution_permits_PermitId" ON execution_permits ("PermitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE UNIQUE INDEX "IX_human_approvals_AssessmentId" ON human_approvals ("AssessmentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE INDEX "IX_human_approvals_PaymentId_ExpiresAtUtc" ON human_approvals ("PaymentId", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    CREATE INDEX "IX_security_assessments_ExpiresAtUtc" ON security_assessments ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    ALTER TABLE payment_requests ADD CONSTRAINT "FK_payment_requests_human_approvals_SecurityApprovalId" FOREIGN KEY ("SecurityApprovalId") REFERENCES human_approvals ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    ALTER TABLE payment_requests ADD CONSTRAINT "FK_payment_requests_security_assessments_SecurityAssessmentId" FOREIGN KEY ("SecurityAssessmentId") REFERENCES security_assessments ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103102602_SecurityGateway') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261103102602_SecurityGateway', '10.0.14');
    END IF;
END $EF$;
COMMIT;

