using System.Linq;
using System.Numerics;
using Content.Client.Eui;
using Content.Client.Lobby;
using Content.Client.Resources;
using Content.Client.Station;
using Content.Client.Stylesheets;
using Content.Shared.Body;
using Content.Shared.Eui;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared._Fish.DmArena;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Fish.DmArena;

public sealed partial class DmArenaKitWindow : DmArenaFrameWindow
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IClientPreferencesManager _prefs = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    public event Action<List<int>>? OnLoadoutConfirmed;
    public event Action<List<int>>? OnPresetSaved;

    private DmArenaKitEuiState? _state;

    private readonly Dictionary<int, int> _selected = new();
    private readonly Dictionary<int, DmArenaItemCard> _cards = new();
    private readonly Dictionary<int, DmArenaStoreOption> _options = new();

    private readonly Label _playerCountLabel;
    private readonly Label _balanceLabel;
    private readonly BoxContainer _leftColumn;
    private readonly BoxContainer _rightColumn;
    private readonly DmArenaDollFrame _preview;
    private readonly DmArenaPillButton _confirmButton;
    private readonly DmArenaPillButton _saveButton;
    private readonly DmArenaPillButton _loadButton;
    private readonly DmArenaPillButton _clearButton;
    private readonly DmArenaPillButton _builderTab;
    private readonly DmArenaPillButton _setsTab;

    private EntityUid _dummy;
    private string? _heldCategory;
    private bool _setsOpen;

    public DmArenaKitWindow()
    {
        IoCManager.InjectDependencies(this);

        WindowTitle = Loc.GetString("dm-arena-loadout-title");
        Resizable = true;
        MinSize = new Vector2(920, 600);
        SetSize = _uiManager.WindowRoot.Size * 0.9f;

        var tabs = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
            Margin = new Thickness(12, 10, 12, 0),
        };
        _builderTab = new DmArenaPillButton { Text = Loc.GetString("dm-arena-tab-builder"), HorizontalExpand = false, MinWidth = 190, MinHeight = 38 };
        _setsTab = new DmArenaPillButton { Text = Loc.GetString("dm-arena-tab-sets"), HorizontalExpand = false, MinWidth = 190, MinHeight = 38 };
        tabs.AddChild(_builderTab);
        tabs.AddChild(_setsTab);
        tabs.AddChild(new Control { HorizontalExpand = true });
        Contents.AddChild(tabs);

        var main = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 12,
            Margin = new Thickness(12, 10, 12, 12),
        };

        _leftColumn = MakeColumn(main);
        var center = MakeCenter(out _preview, out _balanceLabel, out _playerCountLabel,
            out _confirmButton, out _saveButton, out _loadButton, out _clearButton);
        main.AddChild(center);
        _rightColumn = MakeColumn(main);

        Contents.AddChild(main);

        _confirmButton.OnPressed += () => OnLoadoutConfirmed?.Invoke(Expand());
        _saveButton.OnPressed += () => OnPresetSaved?.Invoke(Expand());
        _loadButton.OnPressed += LoadPreset;
        _clearButton.OnPressed += () =>
        {
            _selected.Clear();
            Refresh();
        };
        _builderTab.OnPressed += () => SwitchTab(false);
        _setsTab.OnPressed += () => SwitchTab(true);
        UpdateTabs();
    }

    private static BoxContainer MakeColumn(BoxContainer parent)
    {
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            MinWidth = 250,
            SizeFlagsStretchRatio = 1.5f,
        };
        scroll.AddChild(column);
        parent.AddChild(scroll);
        return column;
    }

    private BoxContainer MakeCenter(
        out DmArenaDollFrame preview,
        out Label balance,
        out Label players,
        out DmArenaPillButton confirm,
        out DmArenaPillButton save,
        out DmArenaPillButton load,
        out DmArenaPillButton clear)
    {
        var center = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 6,
            MinWidth = 220,
            SizeFlagsStretchRatio = 0.7f,
        };

        var previewPanel = new PanelContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            MaxHeight = 420,
            PanelOverride = DmArenaPalette.Rounded(DmArenaPalette.CardBg, 6, 6),
        };
        preview = new DmArenaDollFrame();
        previewPanel.AddChild(preview);
        center.AddChild(previewPanel);

        balance = new Label
        {
            FontOverride = DmArenaPalette.GetFont(true, 18),
            FontColorOverride = DmArenaPalette.Cost,
            HorizontalAlignment = HAlignment.Center,
        };
        center.AddChild(balance);

        players = new Label
        {
            FontOverride = DmArenaPalette.GetFont(false, 11),
            FontColorOverride = DmArenaPalette.TextMuted,
            HorizontalAlignment = HAlignment.Center,
        };
        center.AddChild(players);

        var presetRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        save = new DmArenaPillButton { Text = Loc.GetString("dm-arena-shop-preset-save") };
        load = new DmArenaPillButton { Text = Loc.GetString("dm-arena-shop-preset-load") };
        clear = new DmArenaPillButton { Text = Loc.GetString("dm-arena-shop-clear") };
        presetRow.AddChild(save);
        presetRow.AddChild(load);
        presetRow.AddChild(clear);
        center.AddChild(presetRow);

        confirm = new DmArenaPillButton
        {
            Text = Loc.GetString("dm-arena-loadout-confirm"),
            Disabled = true,
            MinHeight = 50,
        };
        center.AddChild(confirm);

        return center;
    }

    private List<int> Expand()
    {
        return _selected.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)).ToList();
    }

    private void SwitchTab(bool sets)
    {
        if (_setsOpen == sets)
            return;

        _setsOpen = sets;
        UpdateTabs();
        BuildColumns();
        Refresh();
    }

    private void UpdateTabs()
    {
        _builderTab.Selected = !_setsOpen;
        _setsTab.Selected = _setsOpen;
    }

    public void UpdateState(DmArenaKitEuiState state)
    {
        _state = state;
        _playerCountLabel.Text = Loc.GetString("dm-arena-loadout-players", ("count", state.PlayerCount));

        _options.Clear();
        state.Items.ForEach(item => _options[item.Index] = item);

        foreach (var stale in _selected.Keys.Except(_options.Keys).ToList())
        {
            _selected.Remove(stale);
        }

        _heldCategory = state.Items.Count > 0 ? state.Items[0].Category : null;

        BuildColumns();
        Refresh();
    }

    private void BuildColumns()
    {
        if (_state == null)
            return;

        _leftColumn.RemoveAllChildren();
        _rightColumn.RemoveAllChildren();
        _cards.Clear();

        if (_setsOpen)
            BuildSets();
        else
            BuildShop();
    }

    private void BuildShop()
    {
        if (_state == null)
            return;

        string? lastCategory = null;
        string? lastGroup = null;

        foreach (var item in _state.Items)
        {
            var column = item.Category == _heldCategory ? _leftColumn : _rightColumn;

            if (item.Category != lastCategory)
            {
                lastCategory = item.Category;
                lastGroup = null;
                column.AddChild(MakeHeader(item.Category));
            }

            if (item.Group != lastGroup && !string.IsNullOrEmpty(item.Group))
            {
                column.AddChild(new Label
                {
                    Text = item.Group,
                    FontOverride = DmArenaPalette.GetFont(true, 11),
                    FontColorOverride = DmArenaPalette.TextMuted,
                    Margin = new Thickness(8, 6, 0, 0),
                });
            }

            lastGroup = item.Group;

            var card = new DmArenaItemCard(item);
            card.OnCountChanged += OnCountChanged;
            _cards[item.Index] = card;
            column.AddChild(card);
        }
    }

    private void BuildSets()
    {
        if (_state == null)
            return;

        if (_state.Sets.Count == 0)
        {
            _leftColumn.AddChild(new Label
            {
                Text = Loc.GetString("dm-arena-set-empty"),
                FontColorOverride = DmArenaPalette.TextMuted,
                Margin = new Thickness(8),
            });
            return;
        }

        _leftColumn.AddChild(MakeHeader(Loc.GetString("dm-arena-tab-sets")));

        for (var i = 0; i < _state.Sets.Count; i++)
        {
            var set = _state.Sets[i];
            var names = set.Items
                .Where(_options.ContainsKey)
                .GroupBy(index => index)
                .Select(group => group.Count() > 1
                    ? $"{_options[group.Key].Name} ×{group.Count()}"
                    : _options[group.Key].Name)
                .ToList();

            var card = new DmArenaKitCard(set, names);
            card.OnChosen += OnSetChosen;

            (i % 2 == 0 ? _leftColumn : _rightColumn).AddChild(card);
        }
    }

    private void OnSetChosen(DmArenaStoreSetOption set)
    {
        _selected.Clear();
        foreach (var index in set.Items)
        {
            _selected[index] = _selected.GetValueOrDefault(index) + 1;
        }

        SwitchTab(false);
    }

    private static Control MakeHeader(string text)
    {
        var panel = new PanelContainer
        {
            Margin = new Thickness(0, 8, 0, 2),
            PanelOverride = DmArenaPalette.Rounded(DmArenaPalette.TitleBarBg, 10, 4),
        };
        panel.AddChild(new Label
        {
            Text = text,
            FontOverride = DmArenaPalette.GetFont(true, 14),
            FontColorOverride = DmArenaPalette.Accent,
        });
        return panel;
    }

    private void OnCountChanged(DmArenaItemCard card, int delta)
    {
        var index = card.ItemIndex;
        var count = _selected.GetValueOrDefault(index);

        if (delta > 0)
        {
            if (!_options.TryGetValue(index, out var option) || count >= card.MaxCount || !AmmoAvailable(option))
                return;

            // Первый экземпляр вытесняет предметы, занимающие те же слоты или руки
            var conflicts = count == 0 ? GetConflicts(index) : new List<int>();
            if (Spent() - RefundFor(conflicts) + card.Cost > _state!.Budget)
                return;

            conflicts.ForEach(conflict => _selected.Remove(conflict));
            _selected[index] = count + 1;
        }
        else if (count > 1)
        {
            _selected[index] = count - 1;
        }
        else
        {
            _selected.Remove(index);
        }

        PruneAmmo();
        Refresh();
    }

    private int RefundFor(IEnumerable<int> indexes)
    {
        return indexes.Sum(index => _options.TryGetValue(index, out var option) ? option.Cost * _selected.GetValueOrDefault(index) : 0);
    }

    private bool AmmoAvailable(DmArenaStoreOption option)
    {
        if (string.IsNullOrEmpty(option.AmmoFor) || option.AmmoFor == "base")
            return true;

        return _selected.Keys.Any(index => _options.TryGetValue(index, out var other) && other.AmmoType == option.AmmoFor);
    }

    // Убранное оружие тянет за собой боеприпасы, а те могли держать следующие
    private void PruneAmmo()
    {
        while (FindUnsupportedAmmo() is { } index)
        {
            _selected.Remove(index);
        }
    }

    private int? FindUnsupportedAmmo()
    {
        foreach (var index in _selected.Keys)
        {
            if (!_options.TryGetValue(index, out var option) || !AmmoAvailable(option))
                return index;
        }

        return null;
    }

    private List<int> GetConflicts(int index)
    {
        var result = new List<int>();
        if (!_options.TryGetValue(index, out var option))
            return result;

        var others = _selected.Keys.Where(other => other != index && _options.ContainsKey(other)).ToList();

        result.AddRange(others.Where(other => _options[other].Slots.Any(option.Slots.Contains)));

        // В руках максимум два предмета
        var hands = option.Hands + others.Where(other => !result.Contains(other)).Sum(other => _options[other].Hands);
        foreach (var other in others)
        {
            if (hands <= 2)
                break;

            var otherHands = _options[other].Hands;
            if (result.Contains(other) || otherHands == 0)
                continue;

            result.Add(other);
            hands -= otherHands;
        }

        return result;
    }

    private void LoadPreset()
    {
        if (_state?.Preset is not { } preset)
            return;

        _selected.Clear();
        foreach (var index in preset.Where(_options.ContainsKey))
        {
            _selected[index] = _selected.GetValueOrDefault(index) + 1;
        }

        PruneAmmo();
        Refresh();
    }

    private int Spent()
    {
        return _selected.Sum(pair => _options.TryGetValue(pair.Key, out var option) ? option.Cost * pair.Value : 0);
    }

    private void Refresh()
    {
        if (_state == null)
            return;

        var spent = Spent();
        foreach (var (index, card) in _cards)
        {
            var count = _selected.GetValueOrDefault(index);

            // Уже выбранный предмет можно только добирать, новый может вытеснить прежний
            var needsWeapon = count == 0 && !AmmoAvailable(_options[index]);
            var locked = count > 0
                ? spent + card.Cost > _state.Budget
                : spent - RefundFor(GetConflicts(index)) + card.Cost > _state.Budget || needsWeapon;

            card.SetState(count, locked, needsWeapon);
        }

        _balanceLabel.Text = Loc.GetString("dm-arena-shop-balance", ("left", _state.Budget - spent), ("total", _state.Budget));

        var empty = _selected.Count == 0;
        _confirmButton.Disabled = empty;
        _saveButton.Disabled = empty;
        _loadButton.Disabled = _state.Preset == null;
        _clearButton.Disabled = empty;

        RebuildPreview();
    }

    private void RebuildPreview()
    {
        if (_state == null)
            return;

        DeleteDummy();

        var profile = _prefs.Preferences?.SelectedCharacter as HumanoidCharacterProfile;
        if (!_prototype.TryIndex(profile?.Species ?? HumanoidCharacterProfile.DefaultSpecies, out var species))
            return;

        _dummy = _entMan.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);

        if (profile != null)
            _entMan.System<SharedVisualBodySystem>().ApplyProfileTo(_dummy, profile);

        _entMan.System<StationSpawningSystem>().EquipStartingGear(_dummy, _state.BaseGear);

        var chosen = _state.Items
            .Where(item => _selected.ContainsKey(item.Index))
            .OrderByDescending(item => item.EquipFirst);

        foreach (var item in chosen)
        {
            DressPreview(item);
        }

        _preview.View.SetEntity(_dummy);
    }

    private void DressPreview(DmArenaStoreOption item)
    {
        var inventory = _entMan.System<InventorySystem>();

        // Старую одежду удаляем, чтобы не лежала рядом
        foreach (var slot in item.Slots)
        {
            if (!inventory.TryGetSlotEntity(_dummy, slot, out var old))
                continue;

            inventory.TryUnequip(_dummy, slot, silent: true, force: true);
            _entMan.DeleteEntity(old);
        }

        _entMan.System<StationSpawningSystem>().EquipStartingGear(_dummy, item.GearPrototype);

        // В руку кладём только оружие основной категории
        if (item.InHand || item.Category != _heldCategory || !_entMan.TryGetComponent(_dummy, out HandsComponent? handsComp))
            return;

        var hands = _entMan.System<SharedHandsSystem>();
        var held = _entMan.SpawnEntity(item.SpritePrototype, MapCoordinates.Nullspace);

        if (!hands.TryGetEmptyHand((_dummy, handsComp), out var emptyHand)
            || !hands.TryPickup(_dummy, held, emptyHand, checkActionBlocker: false, handsComp: handsComp))
            _entMan.DeleteEntity(held);
    }

    private void DeleteDummy()
    {
        if (_dummy.IsValid())
            _entMan.DeleteEntity(_dummy);

        _dummy = EntityUid.Invalid;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        DeleteDummy();
    }
}

[UsedImplicitly]
public sealed class DmArenaKitEui : BaseEui
{
    private readonly DmArenaKitWindow _window;

    public DmArenaKitEui()
    {
        _window = new DmArenaKitWindow();
        _window.OnClose += OnWindowClosed;
        _window.OnLoadoutConfirmed += items => SendMessage(new DmArenaKitConfirmMessage(items));
        _window.OnPresetSaved += items => SendMessage(new DmArenaKitSaveMessage(items));
    }

    private void OnWindowClosed()
    {
        SendMessage(new CloseEuiMessage());
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();

        // Сервер уже закрыл EUI, повторно ему писать не нужно
        _window.OnClose -= OnWindowClosed;
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        base.HandleState(state);

        if (state is not DmArenaKitEuiState loadoutState)
            return;

        _window.UpdateState(loadoutState);
    }
}

/// <summary>
/// Карточка готового набора. По нажатию переносит набор в конструктор.
/// </summary>
public sealed class DmArenaKitCard : PanelContainer
{
    public event Action<DmArenaStoreSetOption>? OnChosen;

    private readonly StyleBoxFlat _styleBox;

    public DmArenaKitCard(DmArenaStoreSetOption option, IReadOnlyList<string> itemNames)
    {
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;

        _styleBox = new StyleBoxFlat
        {
            BackgroundColor = DmArenaPalette.CardBg,
            BorderColor = DmArenaPalette.AccentDim,
            BorderThickness = new Thickness(3, 0, 0, 0),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8,
        };
        PanelOverride = _styleBox;

        var vbox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };

        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        top.AddChild(new Label
        {
            Text = option.Name,
            FontOverride = DmArenaPalette.GetFont(true, 14),
            FontColorOverride = DmArenaPalette.ButtonText,
            HorizontalExpand = true,
        });
        top.AddChild(new Label
        {
            Text = Loc.GetString("dm-arena-set-cost", ("cost", option.Cost)),
            FontOverride = DmArenaPalette.GetFont(true, 12),
            FontColorOverride = DmArenaPalette.Cost,
        });
        vbox.AddChild(top);

        if (!string.IsNullOrEmpty(option.Description))
            vbox.AddChild(MakeText(option.Description, DmArenaPalette.TextMuted));

        vbox.AddChild(MakeText(string.Join(", ", itemNames), DmArenaPalette.Accent));

        var apply = new DmArenaPillButton
        {
            Text = Loc.GetString("dm-arena-set-apply"),
            MinHeight = 34,
            Margin = new Thickness(0, 4, 0, 0),
        };
        apply.OnPressed += () => OnChosen?.Invoke(option);
        vbox.AddChild(apply);

        AddChild(vbox);

        OnMouseEntered += _ => _styleBox.BackgroundColor = DmArenaPalette.CardHoverBg;
        OnMouseExited += _ => _styleBox.BackgroundColor = DmArenaPalette.CardBg;
    }

    // RichTextLabel переносит длинный текст
    private static RichTextLabel MakeText(string text, Color color)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromMarkupOrThrow(
            $"[font size=11][color={color.ToHex()}]{FormattedMessage.EscapeText(text)}[/color][/font]"));
        return label;
    }
}

public sealed class DmArenaItemCard : PanelContainer
{
    public event Action<DmArenaItemCard, int>? OnCountChanged;

    public int ItemIndex { get; }
    public int Cost { get; }
    public int MaxCount { get; }

    private int _count;
    private bool _locked;
    private bool _needsWeapon;
    private bool _hovered;
    private readonly StyleBoxTexture _styleBox;
    private readonly Label _nameLabel;
    private readonly Label _costLabel;
    private readonly Label _countLabel;
    private readonly DmArenaPillButton _minusButton;
    private readonly DmArenaPillButton _plusButton;

    public DmArenaItemCard(DmArenaStoreOption option)
    {
        ItemIndex = option.Index;
        Cost = option.Cost;
        MaxCount = Math.Max(1, option.MaxCount);
        MouseFilter = MouseFilterMode.Stop;

        if (!string.IsNullOrEmpty(option.Description) || !string.IsNullOrEmpty(option.AmmoFor))
            TooltipSupplier = _ => BuildTooltip(option);

        _styleBox = DmArenaPalette.Rounded(DmArenaPalette.CardBg, 10, 5);
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
            FontOverride = DmArenaPalette.GetFont(true, 13),
            FontColorOverride = DmArenaPalette.ButtonText,
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
            FontOverride = DmArenaPalette.GetFont(true, 13),
            FontColorOverride = DmArenaPalette.Accent,
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
            FontOverride = DmArenaPalette.GetFont(true, 12),
            FontColorOverride = DmArenaPalette.Cost,
        };

        var costPill = new PanelContainer
        {
            VerticalAlignment = VAlignment.Center,
            MinWidth = 56,
            PanelOverride = DmArenaPalette.Rounded(DmArenaPalette.TitleBarBg, 8, 2),
        };
        costPill.AddChild(_costLabel);
        hbox.AddChild(costPill);

        AddChild(hbox);

        OnKeyBindDown += OnClicked;
        OnMouseEntered += _ => SetHovered(true);
        OnMouseExited += _ => SetHovered(false);

        UpdateVisual();
    }

    private Control BuildTooltip(DmArenaStoreOption option)
    {
        var panel = new PanelContainer { PanelOverride = DmArenaPalette.Rounded(DmArenaPalette.TitleBarBg, 10, 5) };
        panel.AddChild(new Label
        {
            Text = _needsWeapon ? Loc.GetString("dm-arena-ammo-locked") : option.Description,
            FontOverride = DmArenaPalette.GetFont(false, 11),
            FontColorOverride = DmArenaPalette.ButtonText,
        });
        return panel;
    }

    private void OnClicked(GUIBoundKeyEventArgs args)
    {
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        // Одиночные предметы включаются кликом, расходники через +/−
        if (MaxCount == 1)
            OnCountChanged?.Invoke(this, _count > 0 ? -1 : 1);
        else if (_count < MaxCount && !_locked)
            OnCountChanged?.Invoke(this, 1);

        args.Handle();
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        UpdateVisual();
    }

    private DmArenaPillButton MakeStepButton(string text, int delta)
    {
        var button = new DmArenaPillButton
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

    public void SetState(int count, bool locked, bool needsWeapon)
    {
        _count = count;
        _locked = locked;
        _needsWeapon = needsWeapon;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        var selected = _count > 0;
        var dim = _locked && !selected;

        _styleBox.Modulate = selected
            ? DmArenaPalette.CardSelectedBg
            : dim ? DmArenaPalette.CardLockedBg
            : _hovered ? DmArenaPalette.CardHoverBg : DmArenaPalette.CardBg;

        _nameLabel.FontColorOverride = dim ? DmArenaPalette.ButtonDisabledFg : DmArenaPalette.ButtonText;
        _costLabel.FontColorOverride = dim ? DmArenaPalette.ButtonDisabledFg : DmArenaPalette.Cost;

        _countLabel.Text = _count > 0 ? $"×{_count}" : string.Empty;
        _minusButton.Disabled = _count == 0;
        _plusButton.Disabled = _count >= MaxCount || _locked;
    }
}

public sealed class DmArenaPillButton : PanelContainer
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

    public DmArenaPillButton()
    {
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        MinHeight = 45;

        _styleBox = DmArenaPalette.Rounded(DmArenaPalette.ButtonBg, 14, 8);
        PanelOverride = _styleBox;

        _label = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            FontOverride = DmArenaPalette.GetFont(true, 12),
            FontColorOverride = DmArenaPalette.ButtonText,
        };
        AddChild(_label);

        OnKeyBindDown += OnClicked;
        OnMouseEntered += _ => SetHovered(true);
        OnMouseExited += _ => SetHovered(false);

        UpdateVisual();
    }

    private void OnClicked(GUIBoundKeyEventArgs args)
    {
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (!_disabled)
            OnPressed?.Invoke();

        args.Handle();
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_disabled)
        {
            _styleBox.Modulate = DmArenaPalette.ButtonDisabledBg;
            _label.FontColorOverride = DmArenaPalette.ButtonDisabledFg;
            return;
        }

        _styleBox.Modulate = _selected
            ? DmArenaPalette.ButtonSelectedBg
            : _hovered ? DmArenaPalette.ButtonHoverBg : DmArenaPalette.ButtonBg;
        _label.FontColorOverride = DmArenaPalette.ButtonText;
    }
}

/// <summary>
/// Рамка с моделью персонажа, масштаб зависит от размера рамки.
/// </summary>
public sealed class DmArenaDollFrame : Control
{
    public SpriteView View { get; }

    public DmArenaDollFrame()
    {
        RectClipContent = true;
        HorizontalExpand = true;
        VerticalExpand = true;

        View = new SpriteView
        {
            OverrideDirection = Direction.South,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        AddChild(View);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        View.Measure(availableSize);
        return Vector2.Zero;
    }

    protected override void Resized()
    {
        base.Resized();

        // Целый масштаб, чтобы спрайт не плыл
        var scale = Math.Clamp((int) (Math.Min(Size.X, Size.Y) / 36f), 3, 12);
        View.Scale = new Vector2(scale, scale);
        // Размер не зависит от спрайта предметов
        View.SetSize = new Vector2(scale * 40, scale * 40);
    }
}

[Virtual]
public class DmArenaFrameWindow : BaseWindow
{
    private const float TitleBarHeight = 36;
    private const float BorderWidth = 2;
    private const float DragMarginSize = 7;

    private readonly PanelContainer _outerBorder;
    private readonly PanelContainer _titleBar;
    private readonly Label _titleLabel;
    private readonly TextureButton _closeButton;
    private readonly PanelContainer _bodyPanel;
    private readonly BoxContainer _contentBox;

    public string WindowTitle
    {
        get => _titleLabel.Text ?? string.Empty;
        set => _titleLabel.Text = value;
    }

    public BoxContainer Contents => _contentBox;

    public DmArenaFrameWindow()
    {
        MouseFilter = MouseFilterMode.Stop;
        Resizable = true;

        _outerBorder = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = DmArenaPalette.BodyBg,
                BorderColor = DmArenaPalette.Border,
                BorderThickness = new Thickness(BorderWidth),
            },
        };
        AddChild(_outerBorder);

        var outerVBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
        };
        _outerBorder.AddChild(outerVBox);

        _titleBar = new PanelContainer
        {
            MinHeight = TitleBarHeight,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = DmArenaPalette.TitleBarBg,
                BorderColor = DmArenaPalette.Accent,
                BorderThickness = new Thickness(0, 0, 0, 1),
            },
        };

        var titleBarContent = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(10, 0),
        };

        var accentBar = new PanelContainer
        {
            MinWidth = 4,
            MaxWidth = 4,
            VerticalExpand = true,
            Margin = new Thickness(0, 6, 8, 6),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = DmArenaPalette.Accent,
            },
        };
        titleBarContent.AddChild(accentBar);

        _titleLabel = new Label
        {
            Text = string.Empty,
            FontOverride = DmArenaPalette.GetFont(true, 17),
            FontColorOverride = DmArenaPalette.ButtonText,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
        };
        titleBarContent.AddChild(_titleLabel);

        _closeButton = new TextureButton
        {
            StyleClasses = { DefaultWindow.StyleClassWindowCloseButton },
            VerticalAlignment = VAlignment.Center,
        };
        _closeButton.OnPressed += _ => Close();

        titleBarContent.AddChild(_closeButton);
        _titleBar.AddChild(titleBarContent);
        outerVBox.AddChild(_titleBar);

        _bodyPanel = new PanelContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
        };

        _contentBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        _bodyPanel.AddChild(_contentBox);
        outerVBox.AddChild(_bodyPanel);
    }

    protected override DragMode GetDragModeFor(Vector2 relativeMousePos)
    {
        if (Resizable)
        {
            if (relativeMousePos.Y < DragMarginSize)
                return DragMode.Top;
            if (relativeMousePos.Y > Size.Y - DragMarginSize)
                return DragMode.Bottom;
            if (relativeMousePos.X < DragMarginSize)
                return DragMode.Left;
            if (relativeMousePos.X > Size.X - DragMarginSize)
                return DragMode.Right;
        }

        if (relativeMousePos.Y < TitleBarHeight + BorderWidth)
            return DragMode.Move;

        return DragMode.None;
    }
}

public static class DmArenaPalette
{
    public static StyleBoxTexture Rounded(Color color, float horizontal = 12, float vertical = 6)
    {
        var texture = IoCManager.Resolve<IResourceCache>().GetTexture("/Textures/Interface/Nano/button.svg.96dpi.png");
        var box = new StyleBoxTexture { Texture = texture, Modulate = color };
        box.SetPatchMargin(StyleBox.Margin.All, 10);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, vertical);
        return box;
    }

    public static Font GetFont(bool bold, int size)
    {
        return IoCManager.Resolve<IResourceCache>().NotoStack(bold ? "Bold" : "Regular", size);
    }

    public static readonly Color Accent = Color.FromHex("#4fd1c5");
    public static readonly Color AccentDim = Color.FromHex("#1f5a5b");

    public static readonly Color BodyBg = Color.FromHex("#04080d");
    public static readonly Color TitleBarBg = Color.FromHex("#0a121b");
    public static readonly Color CardBg = Color.FromHex("#0c1520");
    public static readonly Color CardHoverBg = Color.FromHex("#132434");
    public static readonly Color CardSelectedBg = Color.FromHex("#123a25");

    public static readonly Color CardLockedBg = Color.FromHex("#2a1018");

    public static readonly Color Border = Color.FromHex("#1d4a63");

    public static readonly Color ButtonBg = Color.FromHex("#12324f");
    public static readonly Color ButtonHoverBg = Color.FromHex("#1a4a73");
    public static readonly Color ButtonSelectedBg = Color.FromHex("#1f7a49");
    public static readonly Color ButtonDisabledBg = Color.FromHex("#34161c");
    public static readonly Color ButtonDisabledFg = Color.FromHex("#b07a82");
    public static readonly Color ButtonText = Color.FromHex("#eaf4fb");

    public static readonly Color TextMuted = Color.FromHex("#7f98ab");
    public static readonly Color Cost = Color.FromHex("#f0c36d");
}
