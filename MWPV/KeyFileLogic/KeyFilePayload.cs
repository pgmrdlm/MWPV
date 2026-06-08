namespace KeyFileLogic;

public sealed record KeyFilePayload(
    long PayloadId,
    byte[] Value);
