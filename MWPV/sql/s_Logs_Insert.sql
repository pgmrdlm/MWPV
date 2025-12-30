INSERT INTO Logs
(
  WhenUtc, CreatedUtc, Level, Source, EventCode, SessionId,
  LoginId, ItemId, MachineId,
  DeviceMake, DeviceModel, OSVersion, DeviceIdHash, InstallType,
  AppVersion, IsCrash,
  KeySetVersion, StackHash
)
VALUES
(
  @WhenUtc, @CreatedUtc, @Level, @Source, @EventCode, @SessionId,
  @LoginId, @ItemId, @MachineId,
  @DeviceMake, @DeviceModel, @OSVersion, @DeviceIdHash, @InstallType,
  @AppVersion, @IsCrash,
  @KeySetVersion, @StackHash
);
