using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Fish.Thunderdome;

[DataDefinition]
public sealed partial class ThunderdomeWeaponLoadout
{
    [DataField(required: true)]
    public ProtoId<StartingGearPrototype> Gear;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public LocId? Description;

    [DataField(required: true)]
    public LocId Category;

    [DataField(required: true)]
    public EntProtoId Sprite;
}

[RegisterComponent]
public sealed partial class ThunderdomeRuleComponent : Component
{
    public MapId? ArenaMap;

    public HashSet<EntityUid> ArenaGrids = new();

    public HashSet<NetEntity> Players = new();

    public Dictionary<NetUserId, int> Kills = new();

    public Dictionary<NetUserId, int> Deaths = new();

    public bool Active;

    public TimeSpan NextCleanup;

    [DataField]
    public ProtoId<StartingGearPrototype> Gear = "ThunderdomeBaseGear";

    [DataField]
    public List<ThunderdomeWeaponLoadout> WeaponLoadouts = new();

    [DataField]
    public TimeSpan CleanupInterval = TimeSpan.FromSeconds(25);

    [DataField]
    public float SweepDespawnTime = 10f;
}
