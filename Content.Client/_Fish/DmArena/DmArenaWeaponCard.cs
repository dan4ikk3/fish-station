using System.Numerics;
using Content.Shared._Fish.DmArena;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaWeaponCard : PanelContainer
{
    public event Action<DmArenaWeaponCard>? OnToggled;

    public int ItemIndex { get; }
    public int Cost { get; }

    private bool _selected;
    private bool _locked;
    private readonly StyleBoxFlat _styleBox;
    private readonly Label _nameLabel;
    private readonly Label _costLabel;

    public DmArenaWeaponCard(DmArenaShopOption option)
    {
        ItemIndex = option.Index;
        Cost = option.Cost;
        MouseFilter = MouseFilterMode.Stop;

        if (!string.IsNullOrEmpty(option.Description))
        {
            TooltipSupplier = _ =>
            {
                var panel = new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = DmArenaTheme.TitleBarBg,
                        BorderColor = DmArenaTheme.Accent,
                        BorderThickness = new Thickness(1),
                        ContentMarginLeftOverride = 8,
                        ContentMarginRightOverride = 8,
                        ContentMarginTopOverride = 4,
                        ContentMarginBottomOverride = 4,
                    },
                };
                panel.AddChild(new Label { Text = option.Description, FontColorOverride = DmArenaTheme.ButtonText });
                return panel;
            };
        }

        _styleBox = new StyleBoxFlat
        {
            BackgroundColor = DmArenaTheme.CardBg,
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 4,
            ContentMarginBottomOverride = 4,
            BorderColor = Color.Transparent,
            BorderThickness = new Thickness(2),
        };
        PanelOverride = _styleBox;

        HorizontalExpand = true;
        MinHeight = 52;

        var hbox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
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
            StyleClasses = { "LabelHeading" },
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            Margin = new Thickness(8, 0, 0, 0),
        };
        hbox.AddChild(_nameLabel);

        _costLabel = new Label
        {
            Text = Loc.GetString("dm-arena-shop-cost", ("cost", option.Cost)),
            FontColorOverride = DmArenaTheme.Accent,
            VerticalAlignment = VAlignment.Center,
        };
        hbox.AddChild(_costLabel);

        AddChild(hbox);

        OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            if (!_locked || _selected)
                OnToggled?.Invoke(this);

            args.Handle();
        };

        OnMouseEntered += _ =>
        {
            if (!_selected && !_locked)
                _styleBox.BackgroundColor = DmArenaTheme.CardHoverBg;
        };

        OnMouseExited += _ => UpdateVisual();
    }

    public void SetState(bool selected, bool locked)
    {
        _selected = selected;
        _locked = locked;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        _styleBox.BorderColor = _selected ? DmArenaTheme.Border : Color.Transparent;
        _styleBox.BackgroundColor = _selected ? DmArenaTheme.CardHoverBg : DmArenaTheme.CardBg;

        var dim = _locked && !_selected;
        _nameLabel.FontColorOverride = dim ? DmArenaTheme.ButtonDisabledFg : null;
        _costLabel.FontColorOverride = dim ? DmArenaTheme.ButtonDisabledFg : DmArenaTheme.Accent;
    }
}
