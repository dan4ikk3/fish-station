using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Fish.DmArena;

[DataDefinition]
public sealed partial class DmArenaShopItem
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

    [DataField(required: true)]
    public int Cost;

    [DataField]
    public bool EquipFirst;

    [DataField]
    public bool InHand;
}

[RegisterComponent]
public sealed partial class DmArenaRuleComponent : Component
{
    public MapId? ArenaMap;

    public HashSet<EntityUid> ArenaGrids = new();

    public HashSet<NetEntity> Players = new();

    public Dictionary<NetUserId, int> Kills = new();

    public Dictionary<NetUserId, int> Deaths = new();

    public bool Active;

    // Пресет живёт только в рамках раунда: правило пересоздаётся каждый раунд
    public Dictionary<NetUserId, List<int>> Presets = new();

    public TimeSpan NextCleanup;

    [DataField]
    public ProtoId<StartingGearPrototype> Gear = "DmArenaBaseGear";

    [DataField]
    public List<DmArenaShopItem> ShopItems = new();

    [DataField]
    public int Budget = 100;

    [DataField]
    public TimeSpan CleanupInterval = TimeSpan.FromSeconds(25);

    [DataField]
    public float SweepDespawnTime = 10f;
}
