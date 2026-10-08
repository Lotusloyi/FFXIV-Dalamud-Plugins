namespace FriendCompass;

public sealed class FriendSnapshot
{
    public required ulong ContentId;
    public required string Name;
    public required ushort HomeWorld;
    public required ushort CurrentWorld;
    public ushort Location;
    public required byte Job;
    public required bool Online;

    public bool InZone(uint territoryId) => Online && Location == territoryId && territoryId != 0;
}
