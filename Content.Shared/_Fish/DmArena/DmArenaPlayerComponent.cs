using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Fish.DmArena;

[RegisterComponent]
public sealed partial class DmArenaPlayerComponent : Component
{
    [DataField]
    public EntityUid? RuleEntity;

    [DataField]
    public int Kills;

    [DataField]
    public int Deaths;

    [DataField]
    public int CurrentStreak;

    public EntityUid? LastAttacker;
}
