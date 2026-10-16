-- Up: the Reminders and Audit tables, as RemindersDbContext models them (SQL Server).
-- Written by hand while the model changed; before review, regenerate it from the model
-- (dotnet ef migrations add / dotnet ef migrations script --idempotent) and diff the two.
-- Applied by the pipeline or the DBA, never by the app. Safe to run twice.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- required for the filtered index, and off by default in sqlcmd
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[Reminders]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Reminders] (
        [Id]            int IDENTITY(1,1) NOT NULL,
        [PatientId]     int               NOT NULL,
        [Mobile]        varchar(20)       NOT NULL,
        [AppointmentAt] datetime2(3)      NOT NULL,   -- UTC
        [CreatedAt]     datetime2(3)      NOT NULL,   -- UTC
        [SentAt]        datetime2(3)      NULL,       -- UTC
        [ClaimedAt]     datetime2(3)      NULL,       -- UTC
        [Attempts]      int               NOT NULL,
        CONSTRAINT [PK_Reminders] PRIMARY KEY ([Id])
    );

    CREATE INDEX [IX_Reminders_PatientId_CreatedAt_Id] ON [dbo].[Reminders] ([PatientId], [CreatedAt], [Id]);
    CREATE INDEX [IX_Reminders_Due] ON [dbo].[Reminders] ([AppointmentAt]) WHERE [SentAt] IS NULL AND [ClaimedAt] IS NULL;
END;

IF OBJECT_ID(N'[dbo].[Audit]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Audit] (
        [Id]        int IDENTITY(1,1) NOT NULL,
        [ActorId]   int               NOT NULL,
        [Action]    varchar(100)      NOT NULL,
        [PatientId] int               NOT NULL,
        [At]        datetime2(3)      NOT NULL,   -- UTC
        CONSTRAINT [PK_Audit] PRIMARY KEY ([Id])
    );
END;

COMMIT;
