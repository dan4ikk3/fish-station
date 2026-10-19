using System.Linq;
using System.Numerics;
using Content.Client.Lobby;
using Content.Client.Station;
using Content.Shared._Fish.DmArena;
using Content.Shared.Body;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Fish.DmArena;

public sealed partial class DmArenaLoadoutWindow : DmArenaWindow
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IClientPreferencesManager _prefs = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    public event Action<List<int>>? OnLoadoutConfirmed;
    public event Action<List<int>>? OnPresetSaved;

    private DmArenaLoadoutEuiState? _state;

    private readonly Dictionary<int, int> _selected = new();
    private readonly Dictionary<int, DmArenaWeaponCard> _cards = new();
    private readonly Dictionary<int, DmArenaShopOption> _options = new();

    private readonly Label _playerCountLabel;
    private readonly Label _balanceLabel;
    private readonly BoxContainer _leftColumn;
    private readonly BoxContainer _rightColumn;
    private readonly DmArenaPreviewHost _preview;
    private readonly DmArenaButton _confirmButton;
    private readonly DmArenaButton _saveButton;
    private readonly DmArenaButton _loadButton;
    private readonly DmArenaButton _clearButton;
    private readonly DmArenaButton _builderTab;
    private readonly DmArenaButton _setsTab;

    private EntityUid _dummy;
    private string? _heldCategory;
    private bool _setsOpen;

    public DmArenaLoadoutWindow()
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
        _builderTab = new DmArenaButton { Text = Loc.GetString("dm-arena-tab-builder"), HorizontalExpand = false, MinWidth = 190, MinHeight = 38 };
        _setsTab = new DmArenaButton { Text = Loc.GetString("dm-arena-tab-sets"), HorizontalExpand = false, MinWidth = 190, MinHeight = 38 };
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
        out DmArenaPreviewHost preview,
        out Label balance,
        out Label players,
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
            MinWidth = 220,
            SizeFlagsStretchRatio = 0.7f,
        };

        var previewPanel = new PanelContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            MaxHeight = 420,
            PanelOverride = DmArenaTheme.Rounded(DmArenaTheme.CardBg, 6, 6),
        };
        preview = new DmArenaPreviewHost();
        previewPanel.AddChild(preview);
        center.AddChild(previewPanel);

        balance = new Label
        {
            FontOverride = DmArenaTheme.GetFont(true, 18),
            FontColorOverride = DmArenaTheme.Cost,
            HorizontalAlignment = HAlignment.Center,
        };
        center.AddChild(balance);

        players = new Label
        {
            FontOverride = DmArenaTheme.GetFont(false, 11),
            FontColorOverride = DmArenaTheme.TextMuted,
            HorizontalAlignment = HAlignment.Center,
        };
        center.AddChild(players);

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

        confirm = new DmArenaButton
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
        var result = new List<int>();
        foreach (var (index, count) in _selected)
        {
            for (var i = 0; i < count; i++)
            {
                result.Add(index);
            }
        }

        return result;
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

    public void UpdateState(DmArenaLoadoutEuiState state)
    {
        _state = state;
        _playerCountLabel.Text = Loc.GetString("dm-arena-loadout-players", ("count", state.PlayerCount));

        _options.Clear();
        foreach (var item in state.Items)
        {
            _options[item.Index] = item;
        }

        foreach (var index in _selected.Keys.Where(index => !_options.ContainsKey(index)).ToList())
        {
            _selected.Remove(index);
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
                    FontOverride = DmArenaTheme.GetFont(true, 11),
                    FontColorOverride = DmArenaTheme.TextMuted,
                    Margin = new Thickness(8, 6, 0, 0),
                });
            }

            lastGroup = item.Group;

            var card = new DmArenaWeaponCard(item);
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
                FontColorOverride = DmArenaTheme.TextMuted,
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

            var card = new DmArenaSetCard(set, names);
            card.OnChosen += OnSetChosen;

            (i % 2 == 0 ? _leftColumn : _rightColumn).AddChild(card);
        }
    }

    private void OnSetChosen(DmArenaSetOption set)
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
            PanelOverride = DmArenaTheme.Rounded(DmArenaTheme.TitleBarBg, 10, 4),
        };
        panel.AddChild(new Label
        {
            Text = text,
            FontOverride = DmArenaTheme.GetFont(true, 14),
            FontColorOverride = DmArenaTheme.Accent,
        });
        return panel;
    }

    private void OnCountChanged(DmArenaWeaponCard card, int delta)
    {
        var index = card.ItemIndex;
        var count = _selected.GetValueOrDefault(index);

        if (delta > 0)
        {
            if (count >= card.MaxCount || !AmmoAvailable(_options[index]))
                return;

            var conflicts = count == 0 ? GetConflicts(index) : new List<int>();
            var refund = conflicts.Sum(conflict => _options[conflict].Cost * _selected[conflict]);
            if (Spent() - refund + card.Cost > _state!.Budget)
                return;

            foreach (var conflict in conflicts)
            {
                _selected.Remove(conflict);
            }

            _selected[index] = count + 1;
        }
        else if (count > 0)
        {
            if (count == 1)
                _selected.Remove(index);
            else
                _selected[index] = count - 1;
        }

        PruneAmmo();
        Refresh();
    }

    private bool AmmoAvailable(DmArenaShopOption option)
    {
        if (string.IsNullOrEmpty(option.AmmoFor) || option.AmmoFor == "base")
            return true;

        return _selected.Keys.Any(index => _options.TryGetValue(index, out var other) && other.AmmoType == option.AmmoFor);
    }

    private void PruneAmmo()
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var index in _selected.Keys.ToList())
            {
                if (AmmoAvailable(_options[index]))
                    continue;

                _selected.Remove(index);
                changed = true;
            }
        }
    }

    private List<int> GetConflicts(int index)
    {
        var result = new List<int>();
        if (!_options.TryGetValue(index, out var option))
            return result;

        var hands = option.Hands;
        foreach (var other in _selected.Keys)
        {
            if (other == index)
                continue;

            var otherOption = _options[other];
            if (otherOption.Slots.Any(option.Slots.Contains))
                result.Add(other);
            else
                hands += otherOption.Hands;
        }

        // В руках максимум два предмета
        if (hands > 2)
        {
            foreach (var other in _selected.Keys)
            {
                if (hands <= 2)
                    break;

                var otherOption = _options[other];
                if (other == index || result.Contains(other) || otherOption.Hands == 0)
                    continue;

                result.Add(other);
                hands -= otherOption.Hands;
            }
        }

        return result;
    }

    private void LoadPreset()
    {
        if (_state?.Preset is not { } preset)
            return;

        _selected.Clear();
        foreach (var index in preset)
        {
            if (_options.ContainsKey(index))
                _selected[index] = _selected.GetValueOrDefault(index) + 1;
        }

        PruneAmmo();
        Refresh();
    }

    private int Spent()
    {
        var total = 0;
        foreach (var (index, count) in _selected)
        {
            if (_options.TryGetValue(index, out var option))
                total += option.Cost * count;
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
            var count = _selected.GetValueOrDefault(index);
            bool locked;

            if (count > 0)
            {
                locked = spent + card.Cost > _state.Budget;
            }
            else
            {
                var refund = GetConflicts(index).Sum(conflict => _options[conflict].Cost * _selected[conflict]);
                locked = spent - refund + card.Cost > _state.Budget || !AmmoAvailable(_options[index]);
            }

            card.SetState(count, locked);
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

        HumanoidCharacterProfile? profile = _prefs.Preferences?.SelectedCharacter as HumanoidCharacterProfile;
        var species = profile?.Species ?? HumanoidCharacterProfile.DefaultSpecies;
        _dummy = _entMan.SpawnEntity(_prototype.Index(species).DollPrototype, MapCoordinates.Nullspace);

        if (profile != null)
            _entMan.System<SharedVisualBodySystem>().ApplyProfileTo(_dummy, profile);

        var spawning = _entMan.System<StationSpawningSystem>();
        spawning.EquipStartingGear(_dummy, _state.BaseGear);

        var chosen = _state.Items.Where(item => _selected.ContainsKey(item.Index)).OrderByDescending(item => item.EquipFirst);
        var hands = _entMan.System<SharedHandsSystem>();
        var inventory = _entMan.System<InventorySystem>();

        foreach (var item in chosen)
        {
            // Старую одежду удаляем, чтобы не лежала рядом
            foreach (var slot in item.Slots)
            {
                if (!inventory.TryGetSlotEntity(_dummy, slot, out var old))
                    continue;

                inventory.TryUnequip(_dummy, slot, silent: true, force: true);
                _entMan.DeleteEntity(old);
            }

            spawning.EquipStartingGear(_dummy, item.GearPrototype);

            if (item.InHand || item.Category != _heldCategory || !_entMan.TryGetComponent(_dummy, out HandsComponent? handsComp))
                continue;

            var held = _entMan.SpawnEntity(item.SpritePrototype, MapCoordinates.Nullspace);
            if (!hands.TryGetEmptyHand((_dummy, handsComp), out var emptyHand)
                || !hands.TryPickup(_dummy, held, emptyHand, checkActionBlocker: false, handsComp: handsComp))
                _entMan.DeleteEntity(held);
        }

        _preview.View.SetEntity(_dummy);
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
