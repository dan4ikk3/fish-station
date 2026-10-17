using Robust.Shared.Serialization;

namespace Content.Shared._Fish.DmArena;

/// <summary>
/// Ghost requests to join the DM arena.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaJoinRequestEvent : EntityEventArgs;

/// <summary>
/// Player requests to leave the DM arena.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaLeaveRequestEvent : EntityEventArgs;

/// <summary>
/// Server broadcasts the current DM arena player count to ghosts.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaPlayerCountEvent : EntityEventArgs
{
    public int Count { get; }

    public DmArenaPlayerCountEvent(int count)
    {
        Count = count;
    }
}

/// <summary>
/// Server announces kill streaks / events to DM arena participants.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaAnnouncementEvent : EntityEventArgs
{
    public string Message { get; }

    public DmArenaAnnouncementEvent(string message)
    {
        Message = message;
    }
}

/// <summary>
/// Server notifies a DM arena player that their original station body has been revived.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaRevivalOfferEvent : EntityEventArgs;

/// <summary>
/// Client accepts the revival offer and wants to return to their original body.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaRevivalAcceptEvent : EntityEventArgs;

/// <summary>
/// Server offers a player who just left the arena to stay or return to the body.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaExitOfferEvent : EntityEventArgs;

/// <summary>
/// Client answer to the exit offer: stay on the arena or return to the original body.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaExitChoiceEvent : EntityEventArgs
{
    public bool Stay { get; }

    public DmArenaExitChoiceEvent(bool stay)
    {
        Stay = stay;
    }
}
