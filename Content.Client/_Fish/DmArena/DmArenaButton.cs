using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaButton : PanelContainer
{
    public event Action? OnPressed;

    private readonly StyleBoxTexture _styleBox;
    private readonly Label _label;
    private bool _disabled;
    private bool _selected;
    private bool _hovered;

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            UpdateVisual();
        }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
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

        _styleBox = DmArenaTheme.Rounded(DmArenaTheme.ButtonBg, 14, 8);
        PanelOverride = _styleBox;

        _label = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            FontOverride = DmArenaTheme.GetFont(true, 12),
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
            _hovered = true;
            UpdateVisual();
        };

        OnMouseExited += _ =>
        {
            _hovered = false;
            UpdateVisual();
        };

        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_disabled)
        {
            _styleBox.Modulate = DmArenaTheme.ButtonDisabledBg;
            _label.FontColorOverride = DmArenaTheme.ButtonDisabledFg;
            return;
        }

        _styleBox.Modulate = _selected
            ? DmArenaTheme.ButtonSelectedBg
            : _hovered ? DmArenaTheme.ButtonHoverBg : DmArenaTheme.ButtonBg;
        _label.FontColorOverride = DmArenaTheme.ButtonText;
    }
}
