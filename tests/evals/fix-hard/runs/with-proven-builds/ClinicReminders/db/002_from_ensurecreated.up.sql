-- 002 up: brings a database the old version created with EnsureCreated to the 001 schema.
--  * datetimeoffset(3) -> datetime2(3) holding UTC. Values are first moved to +00:00 (SWITCHOFFSET keeps the
--    instant), because converting datetimeoffset to datetime2 keeps the local clock time and drops the offset.
--  * Reminders gets ClaimedAt and Attempts (the at-most-once claim) and its new indexes.
-- Not compatible with the old version: stop it first. Its job re-sends every unsent reminder, and its writes
-- of a non-UTC offset would land in datetime2 as local time.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DROP INDEX IF EXISTS [IX_Reminders_CreatedAt_Id] ON [Reminders];

UPDATE [Reminders] SET
    [AppointmentAt] = SWITCHOFFSET([AppointmentAt], '+00:00'),
    [CreatedAt] = SWITCHOFFSET([CreatedAt], '+00:00'),
    [SentAt] = SWITCHOFFSET([SentAt], '+00:00');
UPDATE [Audit] SET [At] = SWITCHOFFSET([At], '+00:00');

ALTER TABLE [Reminders] ALTER COLUMN [AppointmentAt] datetime2(3) NOT NULL;
ALTER TABLE [Reminders] ALTER COLUMN [CreatedAt] datetime2(3) NOT NULL;
ALTER TABLE [Reminders] ALTER COLUMN [SentAt] datetime2(3) NULL;
ALTER TABLE [Audit] ALTER COLUMN [At] datetime2(3) NOT NULL;

ALTER TABLE [Reminders] ADD [ClaimedAt] datetime2(3) NULL;
-- The default only fills the existing rows; the model has none, so it is dropped again.
ALTER TABLE [Reminders] ADD [Attempts] int NOT NULL CONSTRAINT [DF_Reminders_Attempts_Backfill] DEFAULT 0;
ALTER TABLE [Reminders] DROP CONSTRAINT [DF_Reminders_Attempts_Backfill];

COMMIT;
GO

-- In a batch of their own: they name columns added above.
CREATE INDEX [IX_Reminders_Due] ON [Reminders] ([AppointmentAt]) WHERE [SentAt] IS NULL AND [ClaimedAt] IS NULL;
GO

CREATE INDEX [IX_Reminders_PatientId_CreatedAt_Id] ON [Reminders] ([PatientId], [CreatedAt], [Id]);
GO
