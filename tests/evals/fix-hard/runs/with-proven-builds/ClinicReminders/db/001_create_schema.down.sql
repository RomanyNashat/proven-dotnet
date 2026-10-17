-- 001 down: removes the ClinicReminders schema. Deletes every reminder and audit row.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DROP TABLE IF EXISTS [Reminders];
DROP TABLE IF EXISTS [Audit];

COMMIT;
GO
