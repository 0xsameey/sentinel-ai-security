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

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104095505_DelegatedSpending') THEN
    CREATE TABLE delegated_payments (
        "Id" character varying(64) NOT NULL,
        "DelegationId" character varying(64) NOT NULL,
        "ContractId" character varying(64) NOT NULL,
        "OwnerId" text NOT NULL,
        "AgentId" text NOT NULL,
        "IntentJson" jsonb NOT NULL,
        "IntentHash" text NOT NULL,
        "DecisionJson" jsonb NOT NULL,
        "Decision" text NOT NULL,
        "Fingerprint" text NOT NULL,
        "ApprovedAtUtc" timestamp with time zone,
        "ApprovalExpiresAtUtc" timestamp with time zone,
        "ApprovalFingerprint" text,
        "PermitId" uuid,
        "PermitFingerprint" text,
        "SignerPublicKey" text,
        "PermitExpiresAtUtc" timestamp with time zone,
        "ConsumedAtUtc" timestamp with time zone,
        "Status" text NOT NULL,
        "TransactionHash" text,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_delegated_payments" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104095505_DelegatedSpending') THEN
    CREATE TABLE delegations (
        "Id" character varying(64) NOT NULL,
        "ContractId" character varying(64) NOT NULL,
        "OwnerId" text NOT NULL,
        "StateJson" jsonb NOT NULL,
        "ObservedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_delegations" PRIMARY KEY ("ContractId", "Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104095505_DelegatedSpending') THEN
    CREATE INDEX "IX_delegated_payments_ContractId_DelegationId" ON delegated_payments ("ContractId", "DelegationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104095505_DelegatedSpending') THEN
    CREATE UNIQUE INDEX "IX_delegated_payments_PermitId" ON delegated_payments ("PermitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104095505_DelegatedSpending') THEN
    CREATE UNIQUE INDEX "IX_delegated_payments_TransactionHash" ON delegated_payments ("TransactionHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104095505_DelegatedSpending') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261104095505_DelegatedSpending', '10.0.14');
    END IF;
END $EF$;
COMMIT;

