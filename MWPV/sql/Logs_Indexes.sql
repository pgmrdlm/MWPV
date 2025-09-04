CREATE INDEX IF NOT EXISTS IX_Logs_CreatedUtc ON Logs(CreatedUtc);
CREATE INDEX IF NOT EXISTS IX_Logs_Level      ON Logs(Level);
CREATE INDEX IF NOT EXISTS IX_Logs_EventCode  ON Logs(EventCode);
CREATE INDEX IF NOT EXISTS IX_Logs_IsCrash    ON Logs(IsCrash);
CREATE INDEX IF NOT EXISTS IX_Logs_StackHash  ON Logs(StackHash);
