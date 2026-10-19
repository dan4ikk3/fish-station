using Robust.Shared.GameObjects;

namespace Content.Shared._Fish.DmArena;

[RegisterComponent]
public sealed partial class DmArenaPlayerComponent : Component
{
    /// <summary>
    /// Сущность правила арены, к которому привязан игрок.
    /// </summary>
    [ViewVariables]
    public EntityUid? RuleEntity;

    /// <summary>
    /// Количество убийств игрока на арене.
    /// </summary>
    [ViewVariables]
    public int Kills;

    /// <summary>
    /// Количество смертей игрока на арене.
    /// </summary>
    [ViewVariables]
    public int Deaths;

    /// <summary>
    /// Последний участник арены, атаковавший игрока.
    /// </summary>
    [ViewVariables]
    public EntityUid? LastAttacker;
}
