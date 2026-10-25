using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Shared._Fish.DmArena;

[RegisterComponent]
public sealed partial class DmArenaFighterComponent : Component
{
    /// <summary>
    /// Сущность правила арены, к которому привязан игрок.
    /// </summary>
    [ViewVariables]
    public EntityUid? RuleEntity;

    /// <summary>
    /// Последний участник арены, атаковавший игрока.
    /// </summary>
    [ViewVariables]
    public EntityUid? LastAttacker;
}

[RegisterComponent]
public sealed partial class DmArenaDormantBodyComponent : Component
{
    /// <summary>
    /// Игрок, чьё прежнее тело помечено.
    /// </summary>
    [ViewVariables]
    public NetUserId UserId;
}

/// <summary>
/// Нужен, когда разуму надо побыть в другом теле, не затрагивая основной разум и не наследуя его знания.
/// </summary>
[RegisterComponent]
public sealed partial class DmArenaMindSwapComponent : Component
{
    /// <summary>
    /// Настоящий разум игрока, отложенный на время подмены.
    /// </summary>
    [ViewVariables]
    public EntityUid OriginalMind;

    /// <summary>
    /// Одноразовый разум, который играет в теле арены.
    /// </summary>
    [ViewVariables]
    public EntityUid DisposableMind;
}

[RegisterComponent]
public sealed partial class DmArenaShieldedComponent : Component;

/// <summary>
/// Предмет или лужа, существовавшие на карте с самого начала. Очистка арены их не удаляет.
/// </summary>
[RegisterComponent]
public sealed partial class DmArenaMapItemComponent : Component;

/// <summary>
/// Точка, в которой появляются бойцы ДМ арены.
/// </summary>
[RegisterComponent]
public sealed partial class DmArenaSpawnPointComponent : Component;
