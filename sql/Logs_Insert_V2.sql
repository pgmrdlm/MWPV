-- Inserts a single log row (v2 schema, compat with legacy WhenUtc)
INSERT INTO Logs
( WhenUtc, CreatedUtc, Level, Source, EventCode, SessionId, MachineId, AppVersion, IsCrash,
  Payload, PayloadFmt, PayloadVer, KeySetVersion, StackHash )
VALUES
( @WhenUtc, @CreatedUtc, @Level, @Source, @EventCode, @SessionId, @MachineId, @AppVersion, @IsCrash,
  @Payload, @PayloadFmt, 1, 1, @StackHash );
