MERGE INTO dbo.Slots AS t
USING (SELECT @Id AS Id) AS s ON t.Id = s.Id
WHEN NOT MATCHED THEN INSERT (Id) VALUES (s.Id);
CREATE INDEX ix_slots_day ON dbo.Slots (Day) WITH (ONLINE = ON);
