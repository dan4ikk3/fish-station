using Content.Shared.Eui;
using Robust.Shared.Configuration;
using Robust.Shared.Serialization;

namespace Content.Shared._Fish.DmArena;

/// <summary>
/// Призрак просит войти на ДМ арену.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaJoinRequestEvent : EntityEventArgs;

/// <summary>
/// Игрок просит покинуть ДМ арену.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaLeaveRequestEvent : EntityEventArgs;

/// <summary>
/// Сервер рассылает призракам текущее число игроков на ДМ арене.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaPlayerCountEvent : EntityEventArgs
{
    public int Count { get; }

    /// <summary>
    /// Включена ли арена. Решает только сервер.
    /// </summary>
    public bool Enabled { get; }

    public DmArenaPlayerCountEvent(int count, bool enabled)
    {
        Count = count;
        Enabled = enabled;
    }
}

/// <summary>
/// Сервер сообщает игроку на арене, что его прежнее тело на станции снова живо.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaRevivalOfferEvent : EntityEventArgs;

/// <summary>
/// Игрок принимает предложение и хочет вернуться в своё прежнее тело.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaRevivalAcceptEvent : EntityEventArgs;

/// <summary>
/// Сервер предлагает покинувшему арену игроку вернуться в бой или в своё тело.
/// </summary>
[Serializable, NetSerializable]
public sealed class DmArenaExitOfferEvent : EntityEventArgs;

/// <summary>
/// Ответ игрока на предложение: вернуться на арену или в своё прежнее тело.
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

[Serializable, NetSerializable]
public sealed class DmArenaKitEuiState : EuiStateBase
{
    public List<DmArenaStoreOption> Items { get; }
    public int PlayerCount { get; }
    public int Budget { get; }
    public List<int>? Preset { get; }
    public string BaseGear { get; }
    public List<DmArenaStoreSetOption> Sets { get; }

    public DmArenaKitEuiState(List<DmArenaStoreOption> items, List<DmArenaStoreSetOption> sets, int playerCount, int budget, List<int>? preset, string baseGear)
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
public sealed class DmArenaStoreOption
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
public sealed class DmArenaStoreSetOption
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<int> Items { get; set; } = new();
    public int Cost { get; set; }
}

[Serializable, NetSerializable]
public sealed class DmArenaKitConfirmMessage : EuiMessageBase
{
    public List<int> Items { get; }

    public DmArenaKitConfirmMessage(List<int> items)
    {
        Items = items;
    }
}

[Serializable, NetSerializable]
public sealed class DmArenaKitSaveMessage : EuiMessageBase
{
    public List<int> Items { get; }

    public DmArenaKitSaveMessage(List<int> items)
    {
        Items = items;
    }
}

[CVarDefs]
public sealed class DmArenaConfigVars
{
    public static readonly CVarDef<bool> DmArenaEnabled =
        CVarDef.Create("dmarena.enabled", true, CVar.SERVERONLY);

    public static readonly CVarDef<bool> DmArenaRefill =
        CVarDef.Create("dmarena.refill", true, CVar.SERVERONLY);
}
