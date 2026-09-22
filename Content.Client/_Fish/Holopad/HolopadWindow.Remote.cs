using Content.Shared.Holopad;
using Content.Shared.Telephone;

namespace Content.Client.Holopad;

/// <summary>
/// Remote-часть окна голопада: кнопка остановки трансляции и её видимость/доступность.
/// Вызывается только из HolopadWindow.xaml.cs через partial-методы, минимальными хуками.
/// </summary>
public sealed partial class HolopadWindow
{
    /// <summary>
    /// Уведомляет о нажатии кнопки остановки трансляции с транслирующего голопада.
    /// </summary>
    public event Action? SendRemoteHolopadStopBroadcastMessageAction;

    /// <summary>
    /// Навешивает обработчик и стиль на кнопку остановки трансляции. Вызывается один раз
    /// из конструктора HolopadWindow.
    /// </summary>
    partial void InitializeRemoteControls()
    {
        StopBroadcastButton.OnPressed += _ => SendRemoteHolopadStopBroadcastMessageAction?.Invoke();
        StopBroadcastButton.AddStyleClass("Caution");
    }

    /// <summary>
    /// Обновляет видимость и доступность remote-контролов. Вызывается каждый раз из
    /// UpdateAppearance() в основном файле.
    /// </summary>
    partial void UpdateRemoteAppearance(bool lockButtons)
    {
        if (_owner == null)
            return;

        var isTransmitter = _entManager.HasComponent<RemoteHolopadTransmitterComponent>(_owner.Value);

        StopBroadcastButton.Disabled = _currentState != TelephoneState.InCall || lockButtons;
        RemoteBroadcastContainer.Visible = isTransmitter && _currentState == TelephoneState.InCall;
    }
}
