using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Fish.Thunderdome;

[Serializable, NetSerializable]
public sealed class ThunderdomeLoadoutEuiState : EuiStateBase
{
    public List<ThunderdomeShopOption> Items { get; }
    public int PlayerCount { get; }
    public int Budget { get; }
    public List<int>? Preset { get; }
    public string BaseGear { get; }

    public ThunderdomeLoadoutEuiState(List<ThunderdomeShopOption> items, int playerCount, int budget, List<int>? preset, string baseGear)
    {
        BaseGear = baseGear;
        Items = items;
        PlayerCount = playerCount;
        Budget = budget;
        Preset = preset;
    }
}

[Serializable, NetSerializable]
public sealed class ThunderdomeShopOption
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SpritePrototype { get; set; } = string.Empty;
    public string GearPrototype { get; set; } = string.Empty;
    public int Cost { get; set; }
    public bool EquipFirst { get; set; }
}

[Serializable, NetSerializable]
public sealed class ThunderdomeLoadoutSelectedMessage : EuiMessageBase
{
    public List<int> Items { get; }

    public ThunderdomeLoadoutSelectedMessage(List<int> items)
    {
        Items = items;
    }
}

[Serializable, NetSerializable]
public sealed class ThunderdomePresetSaveMessage : EuiMessageBase
{
    public List<int> Items { get; }

    public ThunderdomePresetSaveMessage(List<int> items)
    {
        Items = items;
    }
}
