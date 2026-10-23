SET lock_timeout = '5s';
BEGIN;

-- Constant defaults: instant on PostgreSQL 11+, no table rewrite.
ALTER TABLE readings ADD COLUMN "AlertDelivery" varchar(10) NOT NULL DEFAULT 'NotNeeded';
ALTER TABLE readings ADD COLUMN "AlertAttempts" integer NOT NULL DEFAULT 0;
ALTER TABLE readings ADD COLUMN "AlertClaimedUntil" timestamptz NULL;
ALTER TABLE readings ADD COLUMN "AlertLastError" varchar(200) NULL;

-- A stored generated column rewrites meters (one row per linked meter: small).
ALTER TABLE meters ADD COLUMN "SerialKey" varchar(40) GENERATED ALWAYS AS (upper("SerialNumber")) STORED;

-- Keys: identity ALWAYS (the app never supplies an id).
ALTER TABLE patients ALTER COLUMN "Id" SET GENERATED ALWAYS;
ALTER TABLE meters ALTER COLUMN "Id" SET GENERATED ALWAYS;
ALTER TABLE readings ALTER COLUMN "Id" SET GENERATED ALWAYS;

-- Foreign keys without checking existing rows yet (04-validate.sql does, without blocking writes).
ALTER TABLE meters ADD CONSTRAINT "FK_meters_patients_PatientId"
    FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT NOT VALID;
ALTER TABLE readings ADD CONSTRAINT "FK_readings_meters_MeterId"
    FOREIGN KEY ("MeterId") REFERENCES meters ("Id") ON DELETE RESTRICT NOT VALID;
ALTER TABLE readings ADD CONSTRAINT "FK_readings_patients_PatientId"
    FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT NOT VALID;

-- Already sent: nothing for the new sweeper to do.
UPDATE readings SET "AlertDelivery" = 'Sent' WHERE "AlertSentAt" IS NOT NULL;

COMMIT;
