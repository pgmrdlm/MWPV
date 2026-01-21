INSERT INTO Logs
(
  WhenUtc, CreatedUtc, Level, Source, EventCode, SessionId,
  LoginId, ItemId,
  SubjectText, MessageText,
  MachineId,
  DeviceMake, DeviceModel, OSVersion, DeviceIdHash, InstallType,
  AppVersion, IsCrash,
  KeySetVersion, StackHash
)
VALUES
(
  @WhenUtc, @CreatedUtc, @Level, @Source, @EventCode, @SessionId,
  @LoginId, @ItemId,
  @SubjectText, @MessageText,
  @MachineId,
  @DeviceMake, @DeviceModel, @OSVersion, @DeviceIdHash, @InstallType,
  @AppVersion, @IsCrash,
  @KeySetVersion, @StackHash
);
