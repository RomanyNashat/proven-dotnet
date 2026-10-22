-- 001: create the readings schema. Generated from ReadingsDbContext (GenerateCreateScript), then reviewed.
-- Applied by the pipeline or the DBA, never by the app. Down: 001_create_readings_schema.down.sql.
SET lock_timeout = '5s';

BEGIN;

CREATE TABLE readings (
    "Id" integer GENERATED ALWAYS AS IDENTITY,
    "PatientId" integer NOT NULL,
    "NationalId" character varying(10) NOT NULL,
    "Value" numeric(6,2),
    "OutOfRange" character varying(8),
    "Unit" character varying(16) NOT NULL,
    "CollectedAt" timestamptz NOT NULL,
    "Notes" character varying(500),
    "PhotoPath" character varying(64),
    "ReceivedAt" timestamptz NOT NULL,
    CONSTRAINT "PK_readings" PRIMARY KEY ("Id")
);

CREATE TABLE care_team_alerts (
    "Id" integer GENERATED ALWAYS AS IDENTITY,
    "ReadingId" integer NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "SentAt" timestamptz,
    "SendingStartedAt" timestamptz,
    "NeedsFollowUpAt" timestamptz,
    "Attempts" integer NOT NULL,
    "LastError" character varying(100),
    CONSTRAINT "PK_care_team_alerts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_care_team_alerts_readings_ReadingId" FOREIGN KEY ("ReadingId") REFERENCES readings ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_care_team_alerts_ReadingId" ON care_team_alerts ("ReadingId");

CREATE INDEX ix_care_team_alerts_waiting ON care_team_alerts ("Id") WHERE "SentAt" IS NULL AND "NeedsFollowUpAt" IS NULL;

CREATE INDEX "IX_readings_PatientId_CollectedAt" ON readings ("PatientId", "CollectedAt");


COMMIT;
