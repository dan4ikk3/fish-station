using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Fish.Thunderdome;

public sealed class ThunderdomeExitWindow : ThunderdomeWindow
{
    public event Action<bool>? OnChoice;

    public ThunderdomeExitWindow()
    {
        WindowTitle = Loc.GetString("thunderdome-exit-title");
        SetSize = new Vector2(380, 180);
        Resizable = false;

        Contents.AddChild(new Label
        {
            Text = Loc.GetString("thunderdome-exit-offer"),
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

        buttons.AddChild(MakeButton("thunderdome-exit-stay", true));
        buttons.AddChild(MakeButton("thunderdome-exit-return", false));
        Contents.AddChild(buttons);
    }

    private ThunderdomeButton MakeButton(string locId, bool stay)
    {
        var button = new ThunderdomeButton { Text = Loc.GetString(locId) };
        button.OnPressed += () =>
        {
            OnChoice?.Invoke(stay);
            Close();
        };
        return button;
    }
}
