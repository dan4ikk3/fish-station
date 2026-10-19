using System.Numerics;
using Content.Shared._Fish.DmArena;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaWeaponCard : PanelContainer
{
    public event Action<DmArenaWeaponCard, int>? OnCountChanged;

    public int ItemIndex { get; }
    public int Cost { get; }
    public int MaxCount { get; }

    private int _count;
    private bool _locked;
    private bool _hovered;
    private readonly StyleBoxTexture _styleBox;
    private readonly Label _nameLabel;
    private readonly Label _costLabel;
    private readonly Label _countLabel;
    private readonly DmArenaButton _minusButton;
    private readonly DmArenaButton _plusButton;

    public DmArenaWeaponCard(DmArenaShopOption option)
    {
        ItemIndex = option.Index;
        Cost = option.Cost;
        MaxCount = Math.Max(1, option.MaxCount);
        MouseFilter = MouseFilterMode.Stop;

        var description = option.Description;
        if (!string.IsNullOrEmpty(description) || !string.IsNullOrEmpty(option.AmmoFor))
        {
            TooltipSupplier = _ =>
            {
                var lockedAmmo = _locked && !string.IsNullOrEmpty(option.AmmoFor) && _count == 0;
                var panel = new PanelContainer { PanelOverride = DmArenaTheme.Rounded(DmArenaTheme.TitleBarBg, 10, 5) };
                panel.AddChild(new Label
                {
                    Text = lockedAmmo ? Loc.GetString("dm-arena-ammo-locked") : description,
                    FontOverride = DmArenaTheme.GetFont(false, 11),
                    FontColorOverride = DmArenaTheme.ButtonText,
                });
                return panel;
            };
        }

        _styleBox = DmArenaTheme.Rounded(DmArenaTheme.CardBg, 10, 5);
        PanelOverride = _styleBox;

        HorizontalExpand = true;
        MinHeight = 54;

        var hbox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 6,
        };

        if (!string.IsNullOrEmpty(option.SpritePrototype))
        {
            var spriteView = new EntityPrototypeView
            {
                MinSize = new Vector2(40, 40),
                SetSize = new Vector2(40, 40),
                Stretch = SpriteView.StretchMode.Fit,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
            };
            spriteView.SetPrototype(new EntProtoId(option.SpritePrototype));
            hbox.AddChild(spriteView);
        }

        _nameLabel = new Label
        {
            Text = option.Name,
            FontOverride = DmArenaTheme.GetFont(true, 13),
            FontColorOverride = DmArenaTheme.ButtonText,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            ClipText = true,
            Margin = new Thickness(4, 0, 0, 0),
        };
        hbox.AddChild(_nameLabel);

        _minusButton = MakeStepButton("−", -1);
        _plusButton = MakeStepButton("+", 1);
        _countLabel = new Label
        {
            FontOverride = DmArenaTheme.GetFont(true, 13),
            FontColorOverride = DmArenaTheme.Accent,
            VerticalAlignment = VAlignment.Center,
            MinWidth = 26,
            Align = Label.AlignMode.Center,
        };

        if (MaxCount > 1)
        {
            hbox.AddChild(_minusButton);
            hbox.AddChild(_countLabel);
            hbox.AddChild(_plusButton);
        }

        _costLabel = new Label
        {
            Text = Loc.GetString("dm-arena-shop-cost", ("cost", option.Cost)),
            FontOverride = DmArenaTheme.GetFont(true, 12),
            FontColorOverride = DmArenaTheme.Cost,
        };

        var costPill = new PanelContainer
        {
            VerticalAlignment = VAlignment.Center,
            MinWidth = 56,
            PanelOverride = DmArenaTheme.Rounded(DmArenaTheme.TitleBarBg, 8, 2),
        };
        costPill.AddChild(_costLabel);
        hbox.AddChild(costPill);

        AddChild(hbox);

        OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            // Одиночные предметы включаются кликом, расходники через +/−
            if (MaxCount == 1)
                OnCountChanged?.Invoke(this, _count > 0 ? -1 : 1);
            else if (_count < MaxCount && !_locked)
                OnCountChanged?.Invoke(this, 1);

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

    private DmArenaButton MakeStepButton(string text, int delta)
    {
        var button = new DmArenaButton
        {
            Text = text,
            HorizontalExpand = false,
            VerticalAlignment = VAlignment.Center,
            MinSize = new Vector2(30, 30),
            MinHeight = 30,
        };
        button.OnPressed += () => OnCountChanged?.Invoke(this, delta);
        return button;
    }

    public void SetState(int count, bool locked)
    {
        _count = count;
        _locked = locked;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        var selected = _count > 0;
        var dim = _locked && !selected;

        _styleBox.Modulate = selected
            ? DmArenaTheme.CardSelectedBg
            : dim ? DmArenaTheme.CardLockedBg
            : _hovered ? DmArenaTheme.CardHoverBg : DmArenaTheme.CardBg;

        _nameLabel.FontColorOverride = dim ? DmArenaTheme.ButtonDisabledFg : DmArenaTheme.ButtonText;
        _costLabel.FontColorOverride = dim ? DmArenaTheme.ButtonDisabledFg : DmArenaTheme.Cost;

        _countLabel.Text = _count > 0 ? $"×{_count}" : string.Empty;
        _minusButton.Disabled = _count == 0;
        _plusButton.Disabled = _count >= MaxCount || _locked;
    }
}
