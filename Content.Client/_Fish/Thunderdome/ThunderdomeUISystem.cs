using Content.Shared._Fish.Thunderdome;

namespace Content.Client._Fish.Thunderdome;

public sealed class ThunderdomeUISystem : EntitySystem
{
    private ThunderdomeRevivalWindow? _revivalWindow;
    private ThunderdomeExitWindow? _exitWindow;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ThunderdomeRevivalOfferEvent>(OnRevivalOffer);
        SubscribeNetworkEvent<ThunderdomeExitOfferEvent>(OnExitOffer);
    }

    private void OnRevivalOffer(ThunderdomeRevivalOfferEvent ev)
    {
        _revivalWindow?.Close();
        _revivalWindow = new ThunderdomeRevivalWindow();
        _revivalWindow.OnAccepted += () =>
        {
            RaiseNetworkEvent(new ThunderdomeRevivalAcceptEvent());
        };
        _revivalWindow.OpenCentered();
    }

    private void OnExitOffer(ThunderdomeExitOfferEvent ev)
    {
        _exitWindow?.Close();
        _exitWindow = new ThunderdomeExitWindow();
        _exitWindow.OnChoice += stay => RaiseNetworkEvent(new ThunderdomeExitChoiceEvent(stay));
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
