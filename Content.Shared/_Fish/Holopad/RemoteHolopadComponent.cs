using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Holopad;

/// <summary>
/// Маркер транслирующего голопада remote-сети. Может звонить только на
/// <see cref="RemoteHolopadReceiverComponent"/>. Сам передатчик никак не влияет на своего
/// оператора — не замораживает его и не переносит обзор. Это просто источник картинки/звука,
/// которые видит и слышит зашедший в просмотр пользователь приёмника.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RemoteHolopadTransmitterComponent : Component
{
}

/// <summary>
/// Маркер принимающего голопада remote-сети. Пока идёт звонок с привязанного передатчика,
/// любой игрок рядом может «войти» в просмотр (клик по голопаду или кнопка в BUI, см.
/// RemoteHolopadSystem) — его обзор переносится на передатчик, а сам он замораживается до
/// тех пор, пока не нажмёт «Выйти из просмотра» в хотбаре. Сбросить сам звонок с приёмника
/// нельзя — это может только передатчик.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RemoteHolopadReceiverComponent : Component
{
    /// <summary>
    /// Игрок, который сейчас смотрит через этот приёмник. Только сервер.
    /// </summary>
    [ViewVariables]
    public EntityUid? Viewer;

    /// <summary>
    /// Действие «Выйти из просмотра», выдаётся Viewer-у на время просмотра.
    /// </summary>
    [DataField]
    public EntProtoId ExitViewAction = "ActionRemoteHolopadExitView";
}
