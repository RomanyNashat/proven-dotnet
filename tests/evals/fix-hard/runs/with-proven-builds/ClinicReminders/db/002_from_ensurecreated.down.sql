-- 002 down: back to the schema the old version created with EnsureCreated.
-- Loses ClaimedAt and Attempts: a reminder claimed but never confirmed sent becomes unsent again, and the
-- old version would send it. Values come back as datetimeoffset(3) at +00:00 (they hold UTC).
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DROP INDEX IF EXISTS [IX_Reminders_Due] ON [Reminders];
DROP INDEX IF EXISTS [IX_Reminders_PatientId_CreatedAt_Id] ON [Reminders];

ALTER TABLE [Reminders] DROP COLUMN [Attempts];
ALTER TABLE [Reminders] DROP COLUMN [ClaimedAt];

ALTER TABLE [Reminders] ALTER COLUMN [AppointmentAt] datetimeoffset(3) NOT NULL;
ALTER TABLE [Reminders] ALTER COLUMN [CreatedAt] datetimeoffset(3) NOT NULL;
ALTER TABLE [Reminders] ALTER COLUMN [SentAt] datetimeoffset(3) NULL;
ALTER TABLE [Audit] ALTER COLUMN [At] datetimeoffset(3) NOT NULL;

CREATE INDEX [IX_Reminders_CreatedAt_Id] ON [Reminders] ([CreatedAt], [Id]);

COMMIT;
GO
