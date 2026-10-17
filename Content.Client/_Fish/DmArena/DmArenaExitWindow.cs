using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaExitWindow : DmArenaWindow
{
    public event Action<bool>? OnChoice;

    public DmArenaExitWindow()
    {
        WindowTitle = Loc.GetString("dm-arena-exit-title");
        SetSize = new Vector2(380, 180);
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

    private DmArenaButton MakeButton(string locId, bool stay)
    {
        var button = new DmArenaButton { Text = Loc.GetString(locId) };
        button.OnPressed += () =>
        {
            OnChoice?.Invoke(stay);
            Close();
        };
        return button;
    }
}
