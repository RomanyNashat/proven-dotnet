-- 001_initial.up.sql: the LabResults schema, generated from LabDbContext (GenerateCreateScript).
-- Reviewed and applied by the pipeline or the DBA; the app never migrates (rules/efcore-rules.md).
-- LabDbContextModelTests.ReviewedUpScript_MatchesTheModel fails when the model drifts from this file.
BEGIN;

CREATE TABLE access_audit (
    "Id" bigint GENERATED ALWAYS AS IDENTITY,
    "UserId" character varying(64) NOT NULL,
    "Action" character varying(32) NOT NULL,
    "PatientRef" character varying(32),
    "ItemCount" integer NOT NULL,
    "At" timestamptz NOT NULL,
    CONSTRAINT "PK_access_audit" PRIMARY KEY ("Id")
);


CREATE TABLE lab_results (
    "Id" integer GENERATED ALWAYS AS IDENTITY,
    "PatientId" integer NOT NULL,
    "Test" character varying(40) NOT NULL,
    "Value" numeric(10,3) NOT NULL,
    "Unit" character varying(16) NOT NULL,
    "Notes" character varying(1000),
    "ReceivedAt" timestamptz NOT NULL,
    "RecordedBy" character varying(64) NOT NULL,
    CONSTRAINT "PK_lab_results" PRIMARY KEY ("Id")
);


CREATE TABLE sms_outbox (
    "Id" bigint GENERATED ALWAYS AS IDENTITY,
    "Phone" character varying(16) NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "Attempts" integer NOT NULL,
    "LastError" character varying(100),
    CONSTRAINT "PK_sms_outbox" PRIMARY KEY ("Id")
);


CREATE INDEX ix_access_audit_user_id_at ON access_audit ("UserId", "At");


CREATE INDEX "IX_lab_results_PatientId" ON lab_results ("PatientId");


CREATE INDEX ix_lab_results_recorded_by_received_at_id ON lab_results ("RecordedBy", "ReceivedAt" DESC, "Id" DESC);


CREATE INDEX ix_sms_outbox_attempts_id ON sms_outbox ("Attempts", "Id");


-- The service role may add audit rows, never change them (healthcare-compliance). Set the role name with the DBA:
-- GRANT SELECT, INSERT ON access_audit TO lab_results;
-- GRANT SELECT, INSERT, UPDATE ON lab_results TO lab_results;
-- GRANT SELECT, INSERT, UPDATE, DELETE ON sms_outbox TO lab_results;

COMMIT;
