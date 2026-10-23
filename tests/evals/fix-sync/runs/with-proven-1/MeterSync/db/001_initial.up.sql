-- MeterSync: first deployment, up. Generated from the EF model (SyncDbContext.Database.GenerateCreateScript)
-- and reviewed. Run by the pipeline or the DBA, never by the app. The tables are new, so the indexes are
-- created plainly here; an index added to readings later is created CONCURRENTLY, in its own script,
-- outside a transaction and without --idempotent.
SET lock_timeout = '5s';

BEGIN;

CREATE TABLE patients (
    "Id" integer GENERATED ALWAYS AS IDENTITY,
    "NationalId" character varying(10) NOT NULL,
    "Phone" character varying(20) NOT NULL,
    CONSTRAINT "PK_patients" PRIMARY KEY ("Id")
);

CREATE TABLE meters (
    "Id" integer GENERATED ALWAYS AS IDENTITY,
    "PatientId" integer NOT NULL,
    "SerialNumber" character varying(40) NOT NULL,
    "LinkedAt" timestamptz NOT NULL,
    CONSTRAINT "PK_meters" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_meters_patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT
);

CREATE TABLE readings (
    "Id" integer GENERATED ALWAYS AS IDENTITY,
    "PatientId" integer NOT NULL,
    "MeterId" integer NOT NULL,
    "VendorId" character varying(64) NOT NULL,
    "Raw" character varying(16) NOT NULL,
    "Unit" character varying(8) NOT NULL,
    "ValueMgDl" numeric(6,1) NOT NULL,
    "TakenAt" timestamptz NOT NULL,
    "Alert" character varying(8) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "AlertAttempts" integer NOT NULL,
    "AlertClaimedUntil" timestamptz,
    "AlertSentAt" timestamptz,
    CONSTRAINT "PK_readings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_readings_meters_MeterId" FOREIGN KEY ("MeterId") REFERENCES meters ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_readings_patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_meters_PatientId" ON meters ("PatientId");

CREATE UNIQUE INDEX "IX_meters_SerialNumber" ON meters ("SerialNumber");

CREATE UNIQUE INDEX "IX_patients_NationalId" ON patients ("NationalId");

CREATE INDEX ix_readings_alert_due ON readings ("Id") WHERE "Status" = 'Pending';

CREATE INDEX "IX_readings_MeterId" ON readings ("MeterId");

CREATE INDEX "IX_readings_PatientId" ON readings ("PatientId");

CREATE UNIQUE INDEX "IX_readings_VendorId" ON readings ("VendorId");

COMMIT;
