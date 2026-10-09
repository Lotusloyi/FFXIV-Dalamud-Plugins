using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using NativeObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;

namespace FriendCompass;

internal sealed unsafe class NearbyPlayerScanner
{
    private readonly List<NearbyPlayer> players = [];
    private readonly HashSet<nint> addresses = [];

    public IReadOnlyList<NearbyPlayer> Players => players;
    public int NativeCount { get; private set; }
    public int ObjectTableCount { get; private set; }

    public void Scan(IObjectTable objectTable, nint localAddress)
    {
        Clear();
        var manager = CharacterManager.Instance();
        if (manager != null)
        {
            // DR's PlayersManager uses these slots. They are character slots, not a distance limit.
            foreach (var slot in manager->BattleCharas)
            {
                var player = (Character*)slot.Value;
                if (!IsValidNativePlayer(player, localAddress))
                    continue;
                NativeCount++;
                Add(player, true);
            }
        }

        foreach (var obj in objectTable)
        {
            if (obj is not IPlayerCharacter || obj.Address == nint.Zero || obj.Address == localAddress)
                continue;
            ObjectTableCount++;
            Add((Character*)obj.Address, false);
        }
    }

    public void Clear()
    {
        players.Clear();
        addresses.Clear();
        NativeCount = 0;
        ObjectTableCount = 0;
    }

    private static bool IsValidNativePlayer(Character* player, nint localAddress)
        => player != null && (nint)player != localAddress && player->ObjectKind == NativeObjectKind.Pc &&
           player->ClassJob != 0 && player->NameId == 0 && player->ContentId != 0 && player->AccountId != 0 &&
           player->HomeWorld != ushort.MaxValue && player->CurrentWorld != ushort.MaxValue;

    private void Add(Character* player, bool fromCharacterManager)
    {
        var position = new System.Numerics.Vector3(player->Position.X, player->Position.Y, player->Position.Z);
        if (!TrackingMath.IsFinite(position) || !addresses.Add((nint)player))
            return;
        var height = float.IsFinite(player->Height) ? Math.Clamp(player->Height, 0.8f, 4f) : 2f;
        players.Add(new((nint)player, player->ContentId, player->NameString, player->HomeWorld,
            position, height, fromCharacterManager));
    }
}
