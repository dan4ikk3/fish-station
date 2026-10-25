using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaLeaveWindow : DmArenaFrameWindow
{
    public event Action<bool>? OnChoice;

    public DmArenaLeaveWindow()
    {
        WindowTitle = Loc.GetString("dm-arena-exit-title");
        SetSize = new Vector2(480, 190);
        Resizable = false;

        var message = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(14, 14),
        };
        message.SetMessage(FormattedMessage.FromMarkupOrThrow(
            $"[color={DmArenaPalette.ButtonText.ToHex()}]{FormattedMessage.EscapeText(Loc.GetString("dm-arena-exit-offer"))}[/color]"));
        Contents.AddChild(message);

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
