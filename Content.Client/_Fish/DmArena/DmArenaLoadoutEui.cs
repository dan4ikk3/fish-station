using Content.Client.Eui;
using Content.Shared._Fish.DmArena;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._Fish.DmArena;

[UsedImplicitly]
public sealed class DmArenaLoadoutEui : BaseEui
{
    private readonly DmArenaLoadoutWindow _window;

    public DmArenaLoadoutEui()
    {
        _window = new DmArenaLoadoutWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnLoadoutConfirmed += items => SendMessage(new DmArenaLoadoutSelectedMessage(items));
        _window.OnPresetSaved += items => SendMessage(new DmArenaPresetSaveMessage(items));
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

        if (state is not DmArenaLoadoutEuiState loadoutState)
            return;

        _window.UpdateState(loadoutState);
    }
}
