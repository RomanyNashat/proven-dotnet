-- 001 up: the ClinicReminders schema, for an EMPTY database.
-- A database the old version created itself (EnsureCreated) takes 002 instead.
-- Generated from RemindersDbContext (GenerateCreateScript); ClinicReminders.Tests/SchemaScriptTests fails if
-- the model and this script drift apart. Applied by the pipeline or the DBA, never by the app.

CREATE TABLE [Audit] (
    [Id] int NOT NULL IDENTITY,
    [ActorId] int NOT NULL,
    [Action] nvarchar(100) NOT NULL,
    [PatientId] int NOT NULL,
    [At] datetime2(3) NOT NULL,
    CONSTRAINT [PK_Audit] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Reminders] (
    [Id] int NOT NULL IDENTITY,
    [PatientId] int NOT NULL,
    [Mobile] varchar(20) NOT NULL,
    [AppointmentAt] datetime2(3) NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    [SentAt] datetime2(3) NULL,
    [ClaimedAt] datetime2(3) NULL,
    [Attempts] int NOT NULL,
    CONSTRAINT [PK_Reminders] PRIMARY KEY ([Id])
);
GO


CREATE INDEX [IX_Reminders_Due] ON [Reminders] ([AppointmentAt]) WHERE [SentAt] IS NULL AND [ClaimedAt] IS NULL;
GO


CREATE INDEX [IX_Reminders_PatientId_CreatedAt_Id] ON [Reminders] ([PatientId], [CreatedAt], [Id]);
GO



