-- Down: removes what 001_create_reminders.up.sql created. Drops the data with it: run only on a database
-- where the up script has just been applied, or after a backup.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[Audit]', N'U') IS NOT NULL
    DROP TABLE [dbo].[Audit];

IF OBJECT_ID(N'[dbo].[Reminders]', N'U') IS NOT NULL
    DROP TABLE [dbo].[Reminders];

COMMIT;
