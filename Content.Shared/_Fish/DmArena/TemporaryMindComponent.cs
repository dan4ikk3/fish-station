using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Fish.DmArena;

/// <summary>
/// Нужен, когда разуму надо побыть в другом теле, не затрагивая основной разум и не наследуя его знания.
/// </summary>
[RegisterComponent]
public sealed partial class TemporaryMindComponent : Component
{
    [DataField]
    public EntityUid OriginalMind;

    [DataField]
    public EntityUid DisposableMind;
}
