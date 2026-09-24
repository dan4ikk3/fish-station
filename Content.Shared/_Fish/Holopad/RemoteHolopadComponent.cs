using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Holopad;

/// <summary>
/// Маркер транслирующего голопада
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RemoteHolopadTransmitterComponent : Component
{
}

/// <summary>
/// Маркер принимающего голопада
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RemoteHolopadReceiverComponent : Component
{
    /// <summary>
    /// Игрок, который сейчас смотрит через ресивер
    /// </summary>
    [ViewVariables]
    public EntityUid? Viewer;

    /// <summary>
    /// "Выйти из просмотра", выдаётся Viewer на время просмотра
    /// </summary>
    [DataField]
    public EntProtoId ExitViewAction = "ActionRemoteHolopadExitView";
}
