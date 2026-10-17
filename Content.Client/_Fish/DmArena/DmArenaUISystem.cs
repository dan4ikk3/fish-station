using Content.Shared._Fish.DmArena;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaUISystem : EntitySystem
{
    private DmArenaRevivalWindow? _revivalWindow;
    private DmArenaExitWindow? _exitWindow;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<DmArenaRevivalOfferEvent>(OnRevivalOffer);
        SubscribeNetworkEvent<DmArenaExitOfferEvent>(OnExitOffer);
    }

    private void OnRevivalOffer(DmArenaRevivalOfferEvent ev)
    {
        _revivalWindow?.Close();
        _revivalWindow = new DmArenaRevivalWindow();
        _revivalWindow.OnAccepted += () =>
        {
            RaiseNetworkEvent(new DmArenaRevivalAcceptEvent());
        };
        _revivalWindow.OpenCentered();
    }

    private void OnExitOffer(DmArenaExitOfferEvent ev)
    {
        _exitWindow?.Close();
        _exitWindow = new DmArenaExitWindow();
        _exitWindow.OnChoice += stay => RaiseNetworkEvent(new DmArenaExitChoiceEvent(stay));
        _exitWindow.OpenCentered();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _revivalWindow?.Close();
        _revivalWindow = null;
        _exitWindow?.Close();
        _exitWindow = null;
    }
}
