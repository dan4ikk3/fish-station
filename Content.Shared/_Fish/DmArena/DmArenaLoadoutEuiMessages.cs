using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Fish.DmArena;

[Serializable, NetSerializable]
public sealed class DmArenaLoadoutEuiState : EuiStateBase
{
    public List<DmArenaShopOption> Items { get; }
    public int PlayerCount { get; }
    public int Budget { get; }
    public List<int>? Preset { get; }
    public string BaseGear { get; }
    public List<DmArenaSetOption> Sets { get; }

    public DmArenaLoadoutEuiState(List<DmArenaShopOption> items, List<DmArenaSetOption> sets, int playerCount, int budget, List<int>? preset, string baseGear)
    {
        Sets = sets;
        BaseGear = baseGear;
        Items = items;
        PlayerCount = playerCount;
        Budget = budget;
        Preset = preset;
    }
}

[Serializable, NetSerializable]
public sealed class DmArenaShopOption
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string SpritePrototype { get; set; } = string.Empty;
    public string GearPrototype { get; set; } = string.Empty;
    public int Cost { get; set; }
    public bool EquipFirst { get; set; }
    public bool InHand { get; set; }

    public List<string> Slots { get; set; } = new();

    public int Hands { get; set; }
    public int MaxCount { get; set; } = 1;
    public string AmmoType { get; set; } = string.Empty;
    public string AmmoFor { get; set; } = string.Empty;
}

[Serializable, NetSerializable]
public sealed class DmArenaSetOption
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<int> Items { get; set; } = new();
    public int Cost { get; set; }
}

[Serializable, NetSerializable]
public sealed class DmArenaLoadoutSelectedMessage : EuiMessageBase
{
    public List<int> Items { get; }

    public DmArenaLoadoutSelectedMessage(List<int> items)
    {
        Items = items;
    }
}

[Serializable, NetSerializable]
public sealed class DmArenaPresetSaveMessage : EuiMessageBase
{
    public List<int> Items { get; }

    public DmArenaPresetSaveMessage(List<int> items)
    {
        Items = items;
    }
}
