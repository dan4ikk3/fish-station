using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaButton : PanelContainer
{
    public event Action? OnPressed;

    private readonly StyleBoxFlat _styleBox;
    private readonly Label _label;
    private bool _disabled;

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            UpdateVisual();
        }
    }

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    public DmArenaButton()
    {
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        MinHeight = 45;

        _styleBox = new StyleBoxFlat
        {
            BackgroundColor = DmArenaTheme.ButtonBg,
            BorderColor = DmArenaTheme.Accent,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 12,
            ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8,
        };
        PanelOverride = _styleBox;

        _label = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            FontColorOverride = DmArenaTheme.ButtonText,
        };
        AddChild(_label);

        OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            if (!_disabled)
                OnPressed?.Invoke();

            args.Handle();
        };

        OnMouseEntered += _ =>
        {
            if (!_disabled)
                _styleBox.BackgroundColor = DmArenaTheme.ButtonHoverBg;
        };

        OnMouseExited += _ => UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_disabled)
        {
            _styleBox.BackgroundColor = DmArenaTheme.ButtonDisabledBg;
            _styleBox.BorderColor = DmArenaTheme.AccentDim;
            _label.FontColorOverride = DmArenaTheme.ButtonDisabledFg;
        }
        else
        {
            _styleBox.BackgroundColor = DmArenaTheme.ButtonBg;
            _styleBox.BorderColor = DmArenaTheme.Accent;
            _label.FontColorOverride = DmArenaTheme.ButtonText;
        }
    }
}
