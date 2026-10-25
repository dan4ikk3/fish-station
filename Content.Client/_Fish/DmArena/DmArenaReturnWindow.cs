using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaReturnWindow : DmArenaFrameWindow
{
    public event Action? OnAccepted;

    public DmArenaReturnWindow()
    {
        WindowTitle = Loc.GetString("dm-arena-revival-title");
        SetSize = new Vector2(380, 190);
        Resizable = false;

        var message = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(14, 12),
        };
        message.SetMessage(FormattedMessage.FromMarkupOrThrow(
            $"[color={DmArenaPalette.ButtonText.ToHex()}]{FormattedMessage.EscapeText(Loc.GetString("dm-arena-revival-offer"))}[/color]"));
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
