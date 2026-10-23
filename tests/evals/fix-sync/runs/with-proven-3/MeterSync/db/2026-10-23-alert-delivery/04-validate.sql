-- Fails if orphans exist. Check first:
--   SELECT count(*) FROM meters m WHERE NOT EXISTS (SELECT 1 FROM patients p WHERE p."Id" = m."PatientId");
--   SELECT count(*) FROM readings r WHERE NOT EXISTS (SELECT 1 FROM meters m WHERE m."Id" = r."MeterId");
--   SELECT count(*) FROM readings r WHERE NOT EXISTS (SELECT 1 FROM patients p WHERE p."Id" = r."PatientId");
SET lock_timeout = '5s';
ALTER TABLE meters VALIDATE CONSTRAINT "FK_meters_patients_PatientId";
ALTER TABLE readings VALIDATE CONSTRAINT "FK_readings_meters_MeterId";
ALTER TABLE readings VALIDATE CONSTRAINT "FK_readings_patients_PatientId";
