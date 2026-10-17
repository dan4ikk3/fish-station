using Robust.Shared.Serialization;

namespace Content.Shared._Fish.Thunderdome;

/// <summary>
/// Ghost requests to join the thunderdome.
/// </summary>
[Serializable, NetSerializable]
public sealed class ThunderdomeJoinRequestEvent : EntityEventArgs;

/// <summary>
/// Player requests to leave the thunderdome.
/// </summary>
[Serializable, NetSerializable]
public sealed class ThunderdomeLeaveRequestEvent : EntityEventArgs;

/// <summary>
/// Server broadcasts the current thunderdome player count to ghosts.
/// </summary>
[Serializable, NetSerializable]
public sealed class ThunderdomePlayerCountEvent : EntityEventArgs
{
    public int Count { get; }

    public ThunderdomePlayerCountEvent(int count)
    {
        Count = count;
    }
}

/// <summary>
/// Server announces kill streaks / events to thunderdome participants.
/// </summary>
[Serializable, NetSerializable]
public sealed class ThunderdomeAnnouncementEvent : EntityEventArgs
{
    public string Message { get; }

    public ThunderdomeAnnouncementEvent(string message)
    {
        Message = message;
    }
}

/// <summary>
/// Server notifies a thunderdome player that their original station body has been revived.
/// </summary>
[Serializable, NetSerializable]
public sealed class ThunderdomeRevivalOfferEvent : EntityEventArgs;

/// <summary>
/// Client accepts the revival offer and wants to return to their original body.
/// </summary>
[Serializable, NetSerializable]
public sealed class ThunderdomeRevivalAcceptEvent : EntityEventArgs;

/// <summary>
/// Server offers a player who just left the arena to stay or return to the body.
/// </summary>
[Serializable, NetSerializable]
public sealed class ThunderdomeExitOfferEvent : EntityEventArgs;

/// <summary>
/// Client answer to the exit offer: stay on the arena or return to the original body.
/// </summary>
[Serializable, NetSerializable]
public sealed class ThunderdomeExitChoiceEvent : EntityEventArgs
{
    public bool Stay { get; }

    public ThunderdomeExitChoiceEvent(bool stay)
    {
        Stay = stay;
    }
}
