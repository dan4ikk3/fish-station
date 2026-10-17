using Content.Client.Eui;
using Content.Shared._Fish.Thunderdome;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._Fish.Thunderdome;

[UsedImplicitly]
public sealed class ThunderdomeLoadoutEui : BaseEui
{
    private readonly ThunderdomeLoadoutWindow _window;

    public ThunderdomeLoadoutEui()
    {
        _window = new ThunderdomeLoadoutWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnLoadoutConfirmed += items => SendMessage(new ThunderdomeLoadoutSelectedMessage(items));
        _window.OnPresetSaved += items => SendMessage(new ThunderdomePresetSaveMessage(items));
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        base.HandleState(state);

        if (state is not ThunderdomeLoadoutEuiState loadoutState)
            return;

        _window.UpdateState(loadoutState);
    }
}
