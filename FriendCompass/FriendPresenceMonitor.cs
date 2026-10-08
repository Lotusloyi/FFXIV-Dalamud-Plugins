namespace FriendCompass;

internal sealed class FriendPresenceMonitor
{
    private readonly Dictionary<ulong, State> states = [];

    public List<(string Name, bool Online)> Update(IReadOnlyList<FriendSnapshot> friends)
    {
        List<(string Name, bool Online)> changes = [];
        // An empty/unavailable proxy is not evidence that everyone logged out.
        if (friends.Count == 0)
            return changes;

        foreach (var friend in friends)
        {
            if (!states.TryGetValue(friend.ContentId, out var state))
            {
                states[friend.ContentId] = new State(friend.Online);
                continue;
            }

            if (friend.Online == state.Online)
            {
                state.PendingSamples = 0;
                continue;
            }

            // Confirm transitions across two samples to ignore a proxy being repopulated.
            if (++state.PendingSamples < 2)
                continue;

            state.Online = friend.Online;
            state.PendingSamples = 0;
            changes.Add((friend.Name, friend.Online));
        }
        return changes;
    }

    public void Clear() => states.Clear();

    private sealed class State(bool online)
    {
        public bool Online = online;
        public int PendingSamples;
    }
}
