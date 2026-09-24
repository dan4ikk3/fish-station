using Content.Shared.Holopad;
using Content.Shared.Telephone;

namespace Content.Client.Holopad;


public sealed partial class HolopadWindow
{

    public event Action? SendRemoteHolopadStopBroadcastMessageAction;


    partial void InitializeRemoteControls()
    {
        StopBroadcastButton.OnPressed += _ => SendRemoteHolopadStopBroadcastMessageAction?.Invoke();
        StopBroadcastButton.AddStyleClass("Caution");
    }


    partial void UpdateRemoteAppearance(bool lockButtons)
    {
        if (_owner is not { } owner)
            return;

        var isTransmitter = _entManager.HasComponent<RemoteHolopadTransmitterComponent>(owner);

        StopBroadcastButton.Disabled = _currentState != TelephoneState.InCall || lockButtons;
        RemoteBroadcastContainer.Visible = isTransmitter && _currentState == TelephoneState.InCall;

        if (_entManager.HasComponent<RemoteHolopadReceiverComponent>(owner))
        {
            EndCallButton.Disabled = true;
            EndCallButton.Visible = false;
        }
    }
}
