using System.Numerics;
using Content.Shared._Fish.DmArena;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaPromptSystem : EntitySystem
{
    private DmArenaReturnWindow? _revivalWindow;
    private DmArenaLeaveWindow? _exitWindow;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<DmArenaRevivalOfferEvent>(OnRevivalOffer);
        SubscribeNetworkEvent<DmArenaExitOfferEvent>(OnExitOffer);
    }

    private void OnRevivalOffer(DmArenaRevivalOfferEvent ev)
    {
        _revivalWindow?.Close();
        _revivalWindow = new DmArenaReturnWindow();
        _revivalWindow.OnAccepted += () => RaiseNetworkEvent(new DmArenaRevivalAcceptEvent());
        _revivalWindow.OpenCentered();
    }

    private void OnExitOffer(DmArenaExitOfferEvent ev)
    {
        _exitWindow?.Close();
        _exitWindow = new DmArenaLeaveWindow();
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

public sealed class DmArenaLeaveWindow : DmArenaFrameWindow
{
    public event Action<bool>? OnChoice;

    public DmArenaLeaveWindow()
    {
        WindowTitle = Loc.GetString("dm-arena-exit-title");
        SetSize = new Vector2(480, 170);
        Resizable = false;

        Contents.AddChild(new Label
        {
            Text = Loc.GetString("dm-arena-exit-offer"),
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(8, 14),
        });

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
            Margin = new Thickness(8, 4),
        };

        buttons.AddChild(MakeButton("dm-arena-exit-stay", true));
        buttons.AddChild(MakeButton("dm-arena-exit-return", false));
        Contents.AddChild(buttons);
    }

    private DmArenaPillButton MakeButton(string locId, bool stay)
    {
        var button = new DmArenaPillButton { Text = Loc.GetString(locId) };
        button.OnPressed += () =>
        {
            OnChoice?.Invoke(stay);
            Close();
        };
        return button;
    }
}

public sealed class DmArenaReturnWindow : DmArenaFrameWindow
{
    public event Action? OnAccepted;

    public DmArenaReturnWindow()
    {
        WindowTitle = Loc.GetString("dm-arena-revival-title");
        SetSize = new Vector2(350, 160);
        Resizable = false;

        var message = new Label
        {
            Text = Loc.GetString("dm-arena-revival-offer"),
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(8, 12),
        };
        Contents.AddChild(message);

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            HorizontalAlignment = HAlignment.Center,
            SeparationOverride = 8,
            Margin = new Thickness(8, 4),
        };

        var acceptButton = new DmArenaPillButton
        {
            Text = Loc.GetString("dm-arena-revival-accept"),
        };
        acceptButton.OnPressed += () =>
        {
            OnAccepted?.Invoke();
            Close();
        };
        buttons.AddChild(acceptButton);

        var declineButton = new DmArenaPillButton
        {
            Text = Loc.GetString("dm-arena-revival-decline"),
        };
        declineButton.OnPressed += () => Close();
        buttons.AddChild(declineButton);

        Contents.AddChild(buttons);
    }
}
