
CREATE TABLE IF NOT EXISTS Logs (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    WhenUtc       TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CreatedUtc    TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    Level         TEXT    NOT NULL CHECK (UPPER(Level) IN ('TRACE','DEBUG','INFO','WARN','ERROR','FATAL','WARNING')),
    Source        TEXT,
    EventCode     TEXT,
    SessionId     TEXT    NOT NULL DEFAULT '',
    MachineId     TEXT,
    AppVersion    TEXT    NOT NULL DEFAULT '',
    IsCrash       INTEGER NOT NULL DEFAULT 0,
    Payload       BLOB,
    PayloadFmt    TEXT,
    StackHash     TEXT
);


ALTER TABLE Logs ADD COLUMN WhenUtc       TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'));
ALTER TABLE Logs ADD COLUMN CreatedUtc    TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'));
ALTER TABLE Logs ADD COLUMN Level         TEXT    NOT NULL;
ALTER TABLE Logs ADD COLUMN Source        TEXT;
ALTER TABLE Logs ADD COLUMN EventCode     TEXT;
ALTER TABLE Logs ADD COLUMN SessionId     TEXT    NOT NULL DEFAULT '';
ALTER TABLE Logs ADD COLUMN MachineId     TEXT;
ALTER TABLE Logs ADD COLUMN AppVersion    TEXT    NOT NULL DEFAULT '';
ALTER TABLE Logs ADD COLUMN IsCrash       INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Logs ADD COLUMN Payload       BLOB;
ALTER TABLE Logs ADD COLUMN PayloadFmt    TEXT;
ALTER TABLE Logs ADD COLUMN StackHash     TEXT;
