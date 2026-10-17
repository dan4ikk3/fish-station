using System.Linq;
using System.Numerics;
using Content.Client.Lobby;
using Content.Client.Station;
using Content.Shared._Fish.DmArena;
using Content.Shared.Body;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Fish.DmArena;

public sealed class DmArenaLoadoutWindow : DmArenaWindow
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IClientPreferencesManager _prefs = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    public event Action<List<int>>? OnLoadoutConfirmed;
    public event Action<List<int>>? OnPresetSaved;

    private DmArenaLoadoutEuiState? _state;
    private readonly HashSet<int> _selected = new();
    private readonly Dictionary<int, DmArenaWeaponCard> _cards = new();

    private readonly Label _playerCountLabel;
    private readonly Label _balanceLabel;
    private readonly Label _selectedLabel;
    private readonly BoxContainer _leftColumn;
    private readonly BoxContainer _rightColumn;
    private readonly SpriteView _preview;
    private readonly DmArenaButton _confirmButton;
    private readonly DmArenaButton _saveButton;
    private readonly DmArenaButton _loadButton;
    private readonly DmArenaButton _clearButton;

    private EntityUid _dummy;
    private string? _heldCategory;

    public DmArenaLoadoutWindow()
    {
        IoCManager.InjectDependencies(this);

        WindowTitle = Loc.GetString("dm-arena-loadout-title");
        Resizable = false;
        SetSize = _uiManager.WindowRoot.Size * 0.94f;

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(12, 6),
        };
        header.AddChild(new Label
        {
            Text = Loc.GetString("dm-arena-loadout-subtitle"),
            StyleClasses = { "LabelSubText" },
            HorizontalExpand = true,
        });
        _playerCountLabel = new Label { StyleClasses = { "LabelSubText" } };
        header.AddChild(_playerCountLabel);
        Contents.AddChild(header);

        var main = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 12,
            Margin = new Thickness(12, 0, 12, 12),
        };

        _leftColumn = MakeColumn(main);
        var center = MakeCenter(out _preview, out _balanceLabel, out _selectedLabel,
            out _confirmButton, out _saveButton, out _loadButton, out _clearButton);
        main.AddChild(center);
        _rightColumn = MakeColumn(main);

        Contents.AddChild(main);

        _confirmButton.OnPressed += () => OnLoadoutConfirmed?.Invoke(_selected.ToList());
        _saveButton.OnPressed += () => OnPresetSaved?.Invoke(_selected.ToList());
        _loadButton.OnPressed += LoadPreset;
        _clearButton.OnPressed += () =>
        {
            _selected.Clear();
            Refresh();
        };
    }

    private static BoxContainer MakeColumn(BoxContainer parent)
    {
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 1.2f,
        };
        scroll.AddChild(column);
        parent.AddChild(scroll);
        return column;
    }

    private BoxContainer MakeCenter(
        out SpriteView preview,
        out Label balance,
        out Label selected,
        out DmArenaButton confirm,
        out DmArenaButton save,
        out DmArenaButton load,
        out DmArenaButton clear)
    {
        var center = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 6,
        };

        var previewPanel = new PanelContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = DmArenaTheme.CardBg,
                BorderColor = DmArenaTheme.AccentDim,
                BorderThickness = new Thickness(1),
            },
        };
        preview = new SpriteView
        {
            Scale = new Vector2(5, 5),
            OverrideDirection = Direction.South,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        previewPanel.AddChild(preview);
        center.AddChild(previewPanel);

        balance = new Label
        {
            StyleClasses = { "LabelHeadingBigger" },
            FontColorOverride = DmArenaTheme.Accent,
            HorizontalAlignment = HAlignment.Center,
        };
        center.AddChild(balance);

        selected = new Label
        {
            StyleClasses = { "LabelSubText" },
            HorizontalAlignment = HAlignment.Center,
        };
        center.AddChild(selected);

        var presetRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        save = new DmArenaButton { Text = Loc.GetString("dm-arena-shop-preset-save") };
        load = new DmArenaButton { Text = Loc.GetString("dm-arena-shop-preset-load") };
        clear = new DmArenaButton { Text = Loc.GetString("dm-arena-shop-clear") };
        presetRow.AddChild(save);
        presetRow.AddChild(load);
        presetRow.AddChild(clear);
        center.AddChild(presetRow);

        center.AddChild(new Label
        {
            Text = Loc.GetString("dm-arena-shop-preset-hint"),
            StyleClasses = { "LabelSubText" },
            HorizontalAlignment = HAlignment.Center,
        });

        confirm = new DmArenaButton
        {
            Text = Loc.GetString("dm-arena-loadout-confirm"),
            Disabled = true,
        };
        center.AddChild(confirm);

        return center;
    }

    public void UpdateState(DmArenaLoadoutEuiState state)
    {
        _state = state;
        _playerCountLabel.Text = Loc.GetString("dm-arena-loadout-players", ("count", state.PlayerCount));

        _leftColumn.RemoveAllChildren();
        _rightColumn.RemoveAllChildren();
        _cards.Clear();
        _selected.RemoveWhere(index => state.Items.All(item => item.Index != index));

        string? lastCategory = null;
        _heldCategory = state.Items.Count > 0 ? state.Items[0].Category : null;

        foreach (var item in state.Items)
        {
            // Первая категория слева, остальные справа
            var column = item.Category == _heldCategory ? _leftColumn : _rightColumn;

            if (item.Category != lastCategory)
            {
                lastCategory = item.Category;
                column.AddChild(new Label
                {
                    Text = item.Category,
                    StyleClasses = { "LabelKeyText" },
                    FontColorOverride = DmArenaTheme.Accent,
                    Margin = new Thickness(4, 8, 0, 2),
                });
            }

            var card = new DmArenaWeaponCard(item);
            card.OnToggled += OnCardToggled;
            _cards[item.Index] = card;
            column.AddChild(card);
        }

        Refresh();
    }

    private void OnCardToggled(DmArenaWeaponCard card)
    {
        if (!_selected.Remove(card.ItemIndex))
            _selected.Add(card.ItemIndex);

        Refresh();
    }

    private void LoadPreset()
    {
        if (_state?.Preset is not { } preset)
            return;

        _selected.Clear();
        foreach (var index in preset)
        {
            if (_cards.ContainsKey(index))
                _selected.Add(index);
        }

        Refresh();
    }

    private int Spent()
    {
        var total = 0;
        foreach (var index in _selected)
        {
            if (_cards.TryGetValue(index, out var card))
                total += card.Cost;
        }

        return total;
    }

    private void Refresh()
    {
        if (_state == null)
            return;

        var spent = Spent();
        foreach (var (index, card) in _cards)
        {
            var selected = _selected.Contains(index);
            card.SetState(selected, !selected && spent + card.Cost > _state.Budget);
        }

        _balanceLabel.Text = Loc.GetString("dm-arena-shop-balance", ("left", _state.Budget - spent), ("total", _state.Budget));
        _selectedLabel.Text = Loc.GetString("dm-arena-shop-selected", ("count", _selected.Count));

        _confirmButton.Disabled = _selected.Count == 0;
        _saveButton.Disabled = _selected.Count == 0;
        _loadButton.Disabled = _state.Preset == null;
        _clearButton.Disabled = _selected.Count == 0;

        RebuildPreview();
    }

    private void RebuildPreview()
    {
        if (_state == null)
            return;

        DeleteDummy();

        HumanoidCharacterProfile? profile = _prefs.Preferences?.SelectedCharacter as HumanoidCharacterProfile;
        var species = profile?.Species ?? HumanoidCharacterProfile.DefaultSpecies;
        _dummy = _entMan.SpawnEntity(_prototype.Index(species).DollPrototype, MapCoordinates.Nullspace);

        if (profile != null)
            _entMan.System<SharedVisualBodySystem>().ApplyProfileTo(_dummy, profile);

        var spawning = _entMan.System<StationSpawningSystem>();
        spawning.EquipStartingGear(_dummy, _state.BaseGear);

        var chosen = _state.Items.Where(item => _selected.Contains(item.Index)).OrderByDescending(item => item.EquipFirst);
        var hands = _entMan.System<SharedHandsSystem>();

        foreach (var item in chosen)
        {
            spawning.EquipStartingGear(_dummy, item.GearPrototype);

            // Оружие показываем в руках, чтобы оно было видно на модели
            if (item.Category != _heldCategory || !_entMan.TryGetComponent(_dummy, out HandsComponent? handsComp))
                continue;

            var held = _entMan.SpawnEntity(item.SpritePrototype, MapCoordinates.Nullspace);
            if (!hands.TryGetEmptyHand((_dummy, handsComp), out var emptyHand)
                || !hands.TryPickup(_dummy, held, emptyHand, checkActionBlocker: false, handsComp: handsComp))
                _entMan.DeleteEntity(held);
        }

        _preview.SetEntity(_dummy);
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
