using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Administration;
using Content.Server.EUI;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Administration;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Dataset;
using Content.Shared.Eui;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mind.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Tools.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._Fish.DmArena;
using Robust.Server.Audio;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Fish.DmArena;

public sealed partial class DmArenaMatchSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private EuiManager _euiManager = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private DmArenaMindSwapSystem _mindSwap = default!;
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private InventorySystem _inventory = default!;

    private static readonly EntProtoId RulePrototype = "FishDmArenaRule";
    private static readonly ProtoId<LocalizedDatasetPrototype> DmArenaNameSuffixDataset = "NamesOperationPrefix";
    private static readonly SoundPathSpecifier LeaveSound = new("/Audio/Effects/pop_high.ogg");
    private EntityUid? _ruleEntity;
    private bool _enabled = true;

    private bool _refillOnKill;

    private readonly Dictionary<ICommonSession, DmArenaKitEui> _activeEuis = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, DmArenaConfigVars.DmArenaRefill, value => _refillOnKill = value, true);
        Subs.CVar(_cfg, DmArenaConfigVars.DmArenaEnabled, OnEnabledChanged, true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundEnding);
        SubscribeLocalEvent<DmArenaRuleComponent, RuleLoadedGridsEvent>(OnGridsLoaded);
        SubscribeNetworkEvent<DmArenaJoinRequestEvent>(OnJoinRequest);
        SubscribeNetworkEvent<DmArenaLeaveRequestEvent>(OnLeaveRequest);
        SubscribeLocalEvent<DmArenaFighterComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<DmArenaDormantBodyComponent, MobStateChangedEvent>(OnOriginalBodyStateChanged);
        SubscribeNetworkEvent<DmArenaRevivalAcceptEvent>(OnRevivalAccept);
        SubscribeNetworkEvent<DmArenaExitChoiceEvent>(OnExitChoice);
        SubscribeLocalEvent<DmArenaFighterComponent, SuicideGhostEvent>(OnSuicideAttempt);
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<DmArenaShieldedComponent, BeforeDamageChangedEvent>(OnArenaEntityDamage);
        SubscribeLocalEvent<DamageableComponent, ComponentStartup>(OnDamageableStartup);
        SubscribeLocalEvent<DmArenaShieldedComponent, ToolUseAttemptEvent>(OnArenaEntityToolUse);
        SubscribeLocalEvent<DmArenaFighterComponent, ToolUserAttemptUseEvent>(OnFighterToolUse);
        SubscribeLocalEvent<TimedDespawnComponent, EntGotInsertedIntoContainerMessage>(OnDespawnPickedUp);
        SubscribeLocalEvent<DmArenaDormantBodyComponent, ExaminedEvent>(OnOriginalBodyExamined);
        SubscribeLocalEvent<DmArenaFighterComponent, PlayerDetachedEvent>(OnPlayerDetached);

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    // Новый игрок не видел прошлых рассылок, отправляем ему текущее состояние
    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame)
            return;

        RaiseNetworkEvent(new DmArenaPlayerCountEvent(CurrentRule?.Players.Count ?? 0, _enabled), args.Session.Channel);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (CurrentRule is not { Active: true } rule || _timing.CurTime < rule.NextCleanup)
            return;

        rule.NextCleanup = _timing.CurTime + rule.CleanupInterval;
        SweepLooseItems(rule);
    }

    private DmArenaRuleComponent? CurrentRule =>
        _ruleEntity is { } uid ? CompOrNull<DmArenaRuleComponent>(uid) : null;

    private bool TryGetFighterRule(Entity<DmArenaFighterComponent> fighter, [NotNullWhen(true)] out DmArenaRuleComponent? rule)
    {
        rule = fighter.Comp.RuleEntity is { } uid ? CompOrNull<DmArenaRuleComponent>(uid) : null;
        return rule != null;
    }

    // Призрак, который ещё не вышел на арену
    private bool IsIdleGhost([NotNullWhen(true)] EntityUid? uid)
    {
        return uid is { Valid: true } ghost && HasComp<GhostComponent>(ghost) && !HasComp<DmArenaFighterComponent>(ghost);
    }

    private void EnsureRule()
    {
        if (_ruleEntity is { } existing && !TerminatingOrDeleted(existing))
            return;

        if (_ticker.StartGameRule(RulePrototype, out var started))
            _ruleEntity = started;
        else
            Log.Error($"Failed to start DM arena rule {RulePrototype}");
    }

    public void OnEuiClosed(ICommonSession session, DmArenaKitEui eui)
    {
        if (_activeEuis.TryGetValue(session, out var current) && current == eui)
            _activeEuis.Remove(session);
    }

    private void CloseEuis()
    {
        foreach (var (session, eui) in _activeEuis.ToList())
        {
            if (session.Status != SessionStatus.Disconnected)
                eui.Close();
        }

        _activeEuis.Clear();
    }

    private void OnRoundEnding(RoundRestartCleanupEvent ev)
    {
        CloseEuis();

        if (CurrentRule is { } rule)
        {
            var fighters = EntityQueryEnumerator<DmArenaFighterComponent>();
            while (fighters.MoveNext(out var uid, out _))
            {
                _mindSwap.TryRestoreAsGhost(uid);
                QueueDel(uid);
            }

            rule.Players.Clear();
            rule.Presets.Clear();
            rule.Active = false;
            BroadcastPlayerCount(rule);
        }
        else if (_ruleEntity == null)
        {
            return;
        }

        var dormant = EntityQueryEnumerator<DmArenaDormantBodyComponent>();
        while (dormant.MoveNext(out var uid, out _))
        {
            RemComp<DmArenaDormantBodyComponent>(uid);
        }

        _ruleEntity = null;
    }

    private void OnGridsLoaded(Entity<DmArenaRuleComponent> ent, ref RuleLoadedGridsEvent args)
    {
        ent.Comp.ArenaMap = args.Map;
        ent.Comp.ArenaGrids.UnionWith(args.Grids);
        ent.Comp.Active = true;
        ShieldArenaEntities(args.Map);
        MarkMapItems(args.Map);
        BroadcastPlayerCount(ent.Comp);
    }

    private void ShieldArenaEntities(MapId map)
    {
        var query = EntityQueryEnumerator<DamageableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map)
                ShieldEntity(uid);
        }
    }

    // Что лежало на карте с самого начала, очистка не трогает
    private void MarkMapItems(MapId map)
    {
        var items = EntityQueryEnumerator<ItemComponent, TransformComponent>();
        while (items.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map)
                EnsureComp<DmArenaMapItemComponent>(uid);
        }

        var puddles = EntityQueryEnumerator<PuddleComponent, TransformComponent>();
        while (puddles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map)
                EnsureComp<DmArenaMapItemComponent>(uid);
        }
    }

    // Вещи, появившиеся на арене после загрузки карты, тоже неразрушимы
    private void OnDamageableStartup(Entity<DamageableComponent> ent, ref ComponentStartup args)
    {
        if (CurrentRule?.ArenaMap is { } map && Transform(ent).MapID == map)
            ShieldEntity(ent);
    }

    private void ShieldEntity(EntityUid uid)
    {
        // Бойцы и другие существа получают урон как обычно
        if (!HasComp<MobStateComponent>(uid))
            EnsureComp<DmArenaShieldedComponent>(uid);
    }

    // Вскрытие пола идёт с целью-инструментом, у остальных работ цель другая
    private void OnFighterToolUse(Entity<DmArenaFighterComponent> ent, ref ToolUserAttemptUseEvent args)
    {
        if (args.Target is not { } target || !HasComp<ToolTileCompatibleComponent>(target))
            return;

        _popup.PopupEntity(Loc.GetString("dm-arena-indestructible"), ent.Owner, ent.Owner);
        args.Cancelled = true;
    }

    // Событие приходит и инструменту, и цели: сами инструменты не блокируем
    private void OnArenaEntityToolUse(EntityUid uid, DmArenaShieldedComponent component, ToolUseAttemptEvent args)
    {
        if (HasComp<ToolComponent>(uid))
            return;

        _popup.PopupEntity(Loc.GetString("dm-arena-indestructible"), uid, args.User);
        args.Cancel();
    }

    private static void OnArenaEntityDamage(Entity<DmArenaShieldedComponent> ent, ref BeforeDamageChangedEvent args)
    {
        args.Cancelled = true;
    }

    private void OnJoinRequest(DmArenaJoinRequestEvent ev, EntitySessionEventArgs args)
    {
        TryOpenLoadout(args.SenderSession);
    }

    public bool TryOpenLoadout(ICommonSession session)
    {
        if (!_enabled)
        {
            // Клиент мог не знать, что арена выключена
            RaiseNetworkEvent(new DmArenaPlayerCountEvent(0, false), session.Channel);
            return false;
        }

        EnsureRule();

        if (!CanOpenLoadout(session, out var ruleUid))
            return false;

        OpenLoadout(session, ruleUid);
        return true;
    }

    public bool CanOpenLoadout(ICommonSession session, out EntityUid ruleUid)
    {
        ruleUid = default;

        if (_ruleEntity is not { } rule || CurrentRule is not { Active: true })
        {
            Log.Warning($"DM arena join rejected: rule {ToPrettyString(_ruleEntity)} is not active or the map is not loaded");
            return false;
        }

        if (!IsIdleGhost(session.AttachedEntity))
        {
            Log.Warning($"DM arena join rejected for {session.Name}: {ToPrettyString(session.AttachedEntity)} is not a ghost or is already on the arena");
            return false;
        }

        ruleUid = rule;
        return true;
    }

    private void OpenLoadout(ICommonSession session, EntityUid ruleUid)
    {
        if (_activeEuis.Remove(session, out var previous) && previous.Player.Status != SessionStatus.Disconnected)
            previous.Close();

        var eui = new DmArenaKitEui(this, ruleUid, session);
        _euiManager.OpenEui(eui, session);
        _activeEuis[session] = eui;
    }

    private void OnExitChoice(DmArenaExitChoiceEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!IsIdleGhost(session.AttachedEntity))
            return;

        if (ev.Stay)
            TryOpenLoadout(session);
        else
            TryLeaveToOriginalBody(session.AttachedEntity.Value);
    }

    public bool TryLeaveToOriginalBody(EntityUid ghost)
    {
        if (!CanLeaveToOriginalBody(ghost, out var mindId, out var mind))
            return false;

        _mind.UnVisit(mindId, mind);

        if (!TerminatingOrDeleted(ghost))
            QueueDel(ghost);

        return true;
    }

    public bool CanLeaveToOriginalBody(EntityUid ghost, out EntityUid mindId, [NotNullWhen(true)] out MindComponent? mind)
    {
        return _mind.TryGetMind(ghost, out mindId, out mind)
            && mind.OwnedEntity is { Valid: true } body
            && body != ghost
            && Exists(body);
    }

    private void OnLeaveRequest(DmArenaLeaveRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { Valid: true } entity
            && TryComp<DmArenaFighterComponent>(entity, out var fighter))
            LeaveDmArena((entity, fighter));
    }

    private void OnMobStateChanged(Entity<DmArenaFighterComponent> ent, ref MobStateChangedEvent args)
    {
        if (!TryGetFighterRule(ent, out var rule))
            return;

        switch (args.NewMobState)
        {
            case MobState.Critical when args.Origin is { } attacker && HasComp<DmArenaFighterComponent>(attacker):
                ent.Comp.LastAttacker = attacker;
                break;
            case MobState.Dead:
                RewardKill(ent, args.Origin);
                RemoveFromArena(ent, rule, playSound: false);
                break;
        }
    }

    public bool SpawnPlayer(ICommonSession session, EntityUid ruleEntity, List<int> items)
    {
        if (!_enabled
            || !TryComp<DmArenaRuleComponent>(ruleEntity, out var rule)
            || !rule.Active
            || session.AttachedEntity is not { Valid: true } ghost
            || !HasComp<GhostComponent>(ghost)
            || !TryGetValidItems(rule, items, out var chosen)
            || GetRandomSpawnPoint(rule) is not { } spawnCoords
            || !_mind.TryGetMind(ghost, out _, out var mind))
            return false;

        var originalBody = mind.OwnedEntity != ghost ? mind.OwnedEntity : null;
        var ownerId = mind.UserId;
        var mob = _stationSpawning.SpawnPlayerMob(spawnCoords, null, BuildArenaProfile(mind), null);

        // Выдача могла упасть: удаляем тело без владельца
        if (!TryEquipLoadout(mob, rule, chosen, session))
        {
            QueueDel(mob);
            return false;
        }

        EnsureComp<DmArenaFighterComponent>(mob).RuleEntity = ruleEntity;

        if (!_mindSwap.TrySwapTempMind(session, mob))
        {
            QueueDel(mob);
            return false;
        }

        // Метку ставим только после успешной подмены, иначе она осталась бы на теле без бойца
        if (originalBody is { Valid: true } body && !HasComp<DmArenaDormantBodyComponent>(body))
        {
            var marker = EnsureComp<DmArenaDormantBodyComponent>(body);
            if (ownerId is { } userId)
                marker.UserId = userId;
        }

        rule.Players.Add(GetNetEntity(mob));
        _activeEuis.Remove(session);
        BroadcastPlayerCount(rule);
        return true;
    }

    private HumanoidCharacterProfile? BuildArenaProfile(MindComponent mind)
    {
        if (mind.UserId is not { } userId
            || !_prefs.TryGetCachedPreferences(userId, out var prefs)
            || prefs.SelectedCharacter is not HumanoidCharacterProfile selected)
            return null;

        var suffix = Loc.GetString(_random.Pick(_prototype.Index(DmArenaNameSuffixDataset).Values));
        return selected.WithName($"{suffix} {selected.Name}");
    }

    private bool TryEquipLoadout(EntityUid mob, DmArenaRuleComponent rule, List<(DmArenaStoreItem Item, int Count)> chosen, ICommonSession session)
    {
        try
        {
            _stationSpawning.EquipStartingGear(mob, rule.Gear);

            foreach (var (item, count) in chosen.OrderByDescending(x => x.Item.EquipFirst))
            {
                // Старую одежду удаляем, иначе она упадёт на пол
                var kept = ClearReplacedSlots(mob, item.Gear);
                for (var i = 0; i < count; i++)
                    _stationSpawning.EquipStartingGear(mob, item.Gear);

                RestoreDependentSlots(mob, kept);
            }

            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to equip DM arena loadout for {session.Name} on {ToPrettyString(mob)}: {e}");
            return false;
        }
    }

    private List<(string Slot, EntityUid Item)> ClearReplacedSlots(EntityUid mob, ProtoId<StartingGearPrototype> gear)
    {
        var kept = new List<(string Slot, EntityUid Item)>();
        if (!_inventory.TryGetSlots(mob, out var definitions))
            return kept;

        foreach (var slot in _prototype.Index(gear).Equipment.Keys)
        {
            if (!_inventory.TryGetSlotEntity(mob, slot, out var old))
                continue;

            // Слоты, зависящие от одежды (карманы), снимаются вместе с ней: сохраняем их содержимое
            foreach (var dependent in definitions.Where(x => x.DependsOn == slot))
            {
                if (_inventory.TryGetSlotEntity(mob, dependent.Name, out var content)
                    && _inventory.TryUnequip(mob, dependent.Name, silent: true, force: true))
                    kept.Add((dependent.Name, content.Value));
            }

            _inventory.TryUnequip(mob, slot, silent: true, force: true);
            QueueDel(old);
        }

        return kept;
    }

    private void RestoreDependentSlots(EntityUid mob, List<(string Slot, EntityUid Item)> kept)
    {
        foreach (var (slot, item) in kept)
        {
            // Новая одежда может не иметь таких слотов, тогда предмет не нужен
            if (!_inventory.TryEquip(mob, item, slot, silent: true, force: true))
                QueueDel(item);
        }
    }

    private void OnEnabledChanged(bool value)
    {
        _enabled = value;

        if (!value)
        {
            CloseEuis();

            var fighters = new List<Entity<DmArenaFighterComponent>>();
            var query = EntityQueryEnumerator<DmArenaFighterComponent>();
            while (query.MoveNext(out var uid, out var fighter))
            {
                fighters.Add((uid, fighter));
            }

            fighters.ForEach(LeaveDmArena);
        }

        BroadcastPlayerCount();
    }

    private void RemoveFromArena(Entity<DmArenaFighterComponent> ent, DmArenaRuleComponent rule, bool playSound = true)
    {
        var session = CompOrNull<ActorComponent>(ent)?.PlayerSession;

        if (playSound && _transform.TryGetMapOrGridCoordinates(ent, out var coords))
        {
            var name = Identity.Entity(ent, EntityManager);
            _popup.PopupCoordinates(Loc.GetString("dm-arena-leave-01", ("user", name)), coords.Value, PopupType.LargeCaution);

            var filter = Filter.Pvs(coords.Value, 1, EntityManager, _playerManager);
            _audio.PlayStatic(LeaveSound, filter, coords.Value, true, new AudioParams().WithVolume(3));
        }

        ReleaseFighter(ent, rule);

        if (session != null && _enabled)
            RaiseNetworkEvent(new DmArenaExitOfferEvent(), session.Channel);
    }

    private void ReleaseFighter(Entity<DmArenaFighterComponent> ent, DmArenaRuleComponent rule)
    {
        rule.Players.Remove(GetNetEntity(ent));
        ClearOriginalBodyMarker(ent);
        _mindSwap.TryRestoreAsGhost(ent);
        QueueDel(ent);
        BroadcastPlayerCount(rule);
    }

    private void OnSuicideAttempt(Entity<DmArenaFighterComponent> ent, ref SuicideGhostEvent args)
    {
        if (args.Victim != ent.Owner || !TryGetFighterRule(ent, out var rule))
            return;

        RewardKill(ent);
        RemoveFromArena(ent, rule);
        args.Handled = true;
    }

    private void OnGhostAttempt(GhostAttemptHandleEvent args)
    {
        if (args.Mind.CurrentEntity is not { } entity
            || !TryComp<DmArenaFighterComponent>(entity, out var fighter)
            || !TryGetFighterRule((entity, fighter), out var rule))
            return;

        RewardKill((entity, fighter));
        RemoveFromArena((entity, fighter), rule);
        args.Handled = true;
        args.Result = true;
    }

    private void RewardKill(Entity<DmArenaFighterComponent> victim, EntityUid? killer = null)
    {
        killer ??= victim.Comp.LastAttacker;

        if (_refillOnKill && killer is { } winner && HasComp<DmArenaFighterComponent>(winner))
            RefillAmmo(winner);
    }

    private void LeaveDmArena(Entity<DmArenaFighterComponent> ent)
    {
        if (TryGetFighterRule(ent, out var rule))
            ReleaseFighter(ent, rule);
    }

    private void OnOriginalBodyStateChanged(Entity<DmArenaDormantBodyComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is MobState.Dead or MobState.Invalid || args.OldMobState == MobState.Alive)
            return;

        if (_playerManager.TryGetSessionById(ent.Comp.UserId, out var session) && session.AttachedEntity is { Valid: true })
            RaiseNetworkEvent(new DmArenaRevivalOfferEvent(), session.Channel);
    }

    private void OnRevivalAccept(DmArenaRevivalAcceptEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { Valid: true } current)
            TryReturnToBody(current);
    }

    public bool TryReturnToBody(EntityUid current)
    {
        if (!CanReturnToBody(current, out var fighter, out var originalBody))
            return false;

        if (TryGetFighterRule((current, fighter), out var rule))
        {
            rule.Players.Remove(GetNetEntity(current));
            BroadcastPlayerCount(rule);
        }

        _mindSwap.TryRestoreToOriginalBody(current);
        RemComp<DmArenaDormantBodyComponent>(originalBody);
        QueueDel(current);
        return true;
    }

    // Вернуться можно только в живое прежнее тело
    public bool CanReturnToBody(EntityUid current, [NotNullWhen(true)] out DmArenaFighterComponent? fighter, out EntityUid originalBody)
    {
        originalBody = default;

        if (!TryComp<DmArenaFighterComponent>(current, out fighter)
            || !TryComp<DmArenaMindSwapComponent>(current, out var swap)
            || !TryComp<MindComponent>(swap.OriginalMind, out var origMind)
            || origMind.OwnedEntity is not { } body
            || !Exists(body)
            || !TryComp<MobStateComponent>(body, out var mobState)
            || mobState.CurrentState == MobState.Dead)
            return false;

        originalBody = body;
        return true;
    }

    private void SweepLooseItems(DmArenaRuleComponent rule)
    {
        if (rule.ArenaMap is not { } map)
            return;

        var items = EntityQueryEnumerator<ItemComponent, TransformComponent>();
        while (items.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map && !HasComp<DmArenaMapItemComponent>(uid))
                MarkForDespawn(uid, rule.SweepDespawnTime, checkContainer: true);
        }

        var puddles = EntityQueryEnumerator<PuddleComponent, TransformComponent>();
        while (puddles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map && !HasComp<DmArenaMapItemComponent>(uid))
                MarkForDespawn(uid, rule.SweepDespawnTime);
        }
    }

    private void OnOriginalBodyExamined(Entity<DmArenaDormantBodyComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var dead = CompOrNull<MobStateComponent>(ent)?.CurrentState == MobState.Dead;
        var key = dead ? "comp-mind-examined-dead-and-ssd" : "comp-mind-examined-ssd";
        args.PushMarkup($"[color=yellow]{Loc.GetString(key, ("ent", ent))}[/color]");
    }

    private void OnPlayerDetached(Entity<DmArenaFighterComponent> ent, ref PlayerDetachedEvent args)
    {
        if (args.Player.Status != SessionStatus.Disconnected)
            return;

        // Разум мог быть уже очищен, ищем по UserId
        var dormant = EntityQueryEnumerator<DmArenaDormantBodyComponent>();
        while (dormant.MoveNext(out var uid, out var marker))
        {
            if (marker.UserId != args.Player.UserId)
                continue;

            RemComp<DmArenaDormantBodyComponent>(uid);
            break;
        }

        if (!TryGetFighterRule(ent, out var rule))
            return;

        rule.Players.Remove(GetNetEntity(ent));
        QueueDel(ent);
        BroadcastPlayerCount(rule);
    }

    private void ClearOriginalBodyMarker(EntityUid tempBody)
    {
        if (TryComp<DmArenaMindSwapComponent>(tempBody, out var swap)
            && TryComp<MindComponent>(swap.OriginalMind, out var origMind)
            && origMind.OwnedEntity is { } originalBody)
            RemComp<DmArenaDormantBodyComponent>(originalBody);
    }

    private void OnDespawnPickedUp(Entity<TimedDespawnComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        // Рюкзак и другое хранилище бойца тоже считаются: поднятое в них не должно исчезнуть
        EntityUid? owner = args.Container.Owner;
        while (owner is { } current)
        {
            if (HasComp<DmArenaFighterComponent>(current))
            {
                RemComp<TimedDespawnComponent>(ent);
                return;
            }

            owner = _container.TryGetContainingContainer((current, null, null), out var outer) ? outer.Owner : null;
        }
    }

    private void MarkForDespawn(EntityUid uid, float lifetime, bool checkContainer = false)
    {
        if (HasComp<TimedDespawnComponent>(uid) || checkContainer && _container.IsEntityInContainer(uid))
            return;

        EnsureComp<TimedDespawnComponent>(uid).Lifetime = lifetime;
    }

    private EntityCoordinates? GetRandomSpawnPoint(DmArenaRuleComponent rule)
    {
        var points = new List<EntityCoordinates>();

        var query = EntityQueryEnumerator<DmArenaSpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } grid && rule.ArenaGrids.Contains(grid))
                points.Add(xform.Coordinates);
        }

        return points.Count > 0 ? _random.Pick(points) : null;
    }

    private void BroadcastPlayerCount(DmArenaRuleComponent? rule = null)
    {
        rule ??= CurrentRule;

        foreach (var eui in _activeEuis.Values)
        {
            eui.StateDirty();
        }

        RaiseNetworkEvent(new DmArenaPlayerCountEvent(rule?.Players.Count ?? 0, _enabled), Filter.Broadcast());
    }

    public DmArenaKitEuiState GetLoadoutState(EntityUid ruleEntity, NetUserId userId)
    {
        if (!TryComp<DmArenaRuleComponent>(ruleEntity, out var rule))
            return new DmArenaKitEuiState(new List<DmArenaStoreOption>(), new List<DmArenaStoreSetOption>(), 0, 0, null, string.Empty);

        var items = rule.ShopItems.Select((item, index) => BuildItemOption(item, index)).ToList();
        var sets = rule.Sets
            .Select(set => BuildSetOption(rule, set))
            .Where(option => option.Items.Count > 0 && option.Cost <= rule.Budget)
            .ToList();

        rule.Presets.TryGetValue(userId, out var preset);
        return new DmArenaKitEuiState(items, sets, rule.Players.Count, rule.Budget, preset, rule.Gear);
    }

    private DmArenaStoreOption BuildItemOption(DmArenaStoreItem item, int index)
    {
        var gear = _prototype.Index(item.Gear);

        return new DmArenaStoreOption
        {
            Index = index,
            Name = Loc.GetString(item.Name),
            Description = item.Description is { } description ? Loc.GetString(description) : string.Empty,
            Category = Loc.GetString(item.Category),
            Group = item.Group is { } group ? Loc.GetString(group) : string.Empty,
            SpritePrototype = item.Sprite,
            GearPrototype = item.Gear,
            Cost = item.Cost,
            EquipFirst = item.EquipFirst,
            InHand = item.InHand,
            Slots = gear.Equipment.Keys.ToList(),
            Hands = gear.Inhand.Count,
            MaxCount = item.MaxCount,
            AmmoType = item.AmmoType ?? string.Empty,
            AmmoFor = item.AmmoFor ?? string.Empty,
        };
    }

    private DmArenaStoreSetOption BuildSetOption(DmArenaRuleComponent rule, DmArenaStoreSet set)
    {
        var option = new DmArenaStoreSetOption
        {
            Name = Loc.GetString(set.Name),
            Description = set.Description is { } description ? Loc.GetString(description) : string.Empty,
        };

        foreach (var gearId in set.Items)
        {
            var index = rule.ShopItems.FindIndex(x => x.Gear == gearId);
            if (index < 0)
                continue;

            option.Items.Add(index);
            option.Cost += rule.ShopItems[index].Cost;
        }

        return option;
    }

    public void SavePreset(EntityUid ruleEntity, NetUserId userId, List<int> items)
    {
        if (TryComp<DmArenaRuleComponent>(ruleEntity, out var rule) && TryGetValidItems(rule, items, out _))
            rule.Presets[userId] = new List<int>(items);
    }

    private bool TryGetValidItems(DmArenaRuleComponent rule, List<int> indexes, out List<(DmArenaStoreItem Item, int Count)> items)
    {
        items = new List<(DmArenaStoreItem, int)>();

        // Каждый предмет стоит минимум 1 очко, длиннее бюджета список быть не может
        if (indexes.Count > rule.Budget)
            return false;

        var counts = new Dictionary<int, int>();
        foreach (var index in indexes)
        {
            if (index < 0 || index >= rule.ShopItems.Count)
                return false;

            counts[index] = counts.GetValueOrDefault(index) + 1;
        }

        var cost = 0;
        var hands = 0;
        var usedSlots = new HashSet<string>();
        var ammoTypes = new HashSet<string>();

        foreach (var (index, count) in counts)
        {
            var item = rule.ShopItems[index];
            var gear = _prototype.Index(item.Gear);

            // Повторно берутся только расходники
            var equipable = gear.Equipment.Count > 0 || gear.Inhand.Count > 0;
            if (count > item.MaxCount || count > 1 && equipable)
                return false;

            // Нельзя два предмета в один слот и больше двух в руки
            if (gear.Equipment.Keys.Any(slot => !usedSlots.Add(slot)))
                return false;

            if (item.AmmoType != null)
                ammoTypes.Add(item.AmmoType);

            hands += gear.Inhand.Count;
            cost += item.Cost * count;
            items.Add((item, count));
        }

        // Боеприпасы только к выбранному оружию
        var ammoMatches = items.All(x => x.Item.AmmoFor is null or "base" || ammoTypes.Contains(x.Item.AmmoFor));
        return ammoMatches && cost <= rule.Budget && hands <= 2;
    }

    private void RefillAmmo(EntityUid killer)
    {
        var pending = new Stack<EntityUid>();
        pending.Push(killer);

        while (pending.TryPop(out var current))
        {
            if (!TryComp<ContainerManagerComponent>(current, out var manager))
                continue;

            foreach (var container in _container.GetAllContainers(current, manager))
            {
                // Патронник и барабан хранят уже потраченные патроны
                var spentAmmo = container.ID is "gun_chamber" or "revolver-ammo";

                foreach (var contained in container.ContainedEntities)
                {
                    pending.Push(contained);

                    if (!spentAmmo)
                        RefillProviders(contained);
                }
            }
        }
    }

    private void RefillProviders(EntityUid uid)
    {
        if (TryComp<BallisticAmmoProviderComponent>(uid, out var ballistic))
            _gun.RefillBallisticAmmo((uid, ballistic));

        if (HasComp<BatteryAmmoProviderComponent>(uid))
            RefillBattery(uid);

        if (TryComp<RevolverAmmoProviderComponent>(uid, out var revolver))
            _gun.RefillRevolver((uid, revolver));
    }

    private void RefillBattery(EntityUid uid)
    {
        var getCharge = new GetChargeEvent();
        RaiseLocalEvent(uid, ref getCharge);

        var missing = getCharge.MaxCharge - getCharge.CurrentCharge;
        if (getCharge.MaxCharge <= 0 || missing <= 0)
            return;

        var change = new ChangeChargeEvent(missing);
        RaiseLocalEvent(uid, ref change);
    }
}

public sealed class DmArenaKitEui : BaseEui
{
    private readonly DmArenaMatchSystem _arena;
    private readonly EntityUid _ruleEntity;
    private readonly ICommonSession _session;

    public DmArenaKitEui(DmArenaMatchSystem arena, EntityUid ruleEntity, ICommonSession session)
    {
        _arena = arena;
        _ruleEntity = ruleEntity;
        _session = session;
    }

    public override EuiStateBase GetNewState()
    {
        return _arena.GetLoadoutState(_ruleEntity, _session.UserId);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case DmArenaKitConfirmMessage selected:
                if (_arena.SpawnPlayer(_session, _ruleEntity, selected.Items))
                    Close();
                else
                    StateDirty();
                break;
            case DmArenaKitSaveMessage preset:
                _arena.SavePreset(_ruleEntity, _session.UserId, preset.Items);
                StateDirty();
                break;
        }
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        _arena.OnEuiClosed(_session, this);
    }
}

/// <summary>
/// Временная подмена разума: настоящий разум игрока откладывается, а в тело арены идёт одноразовый.
/// После выхода одноразовый разум удаляется, и игрок возвращается к своему прежнему разуму.
/// </summary>
public sealed partial class DmArenaMindSwapSystem : EntitySystem
{
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private MindExamineSystem _mindExamine = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DmArenaMindSwapComponent, PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerDetached(Entity<DmArenaMindSwapComponent> ent, ref PlayerDetachedEvent args)
    {
        if (args.Player.Status != SessionStatus.Disconnected)
            return;

        CleanupDisposableMind(ent, ent.Comp);
        RemComp<DmArenaMindSwapComponent>(ent);
    }

    public bool TrySwapTempMind(ICommonSession session, EntityUid newBody)
    {
        if (session.AttachedEntity is not { Valid: true } currentEntity
            || !_mind.TryGetMind(currentEntity, out var origMindId, out var origMind))
            return false;

        var userId = origMind.UserId;
        _mind.UnVisit(origMindId, origMind);

        // Освобождаем userId для одноразового разума
        _mind.SetUserId(origMindId, null, origMind);

        var newMind = _mind.CreateMind(userId, origMind.CharacterName);

        try
        {
            _mind.TransferTo(newMind, newBody);
            _playerManager.SetAttachedEntity(session, newBody);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to swap mind of {session.Name} into {ToPrettyString(newBody)}: {e}");
            QueueDel(newMind);
            _mind.SetUserId(origMindId, userId, origMind);
            return false;
        }

        var swap = EnsureComp<DmArenaMindSwapComponent>(newBody);
        swap.OriginalMind = origMindId;
        swap.DisposableMind = newMind;

        return true;
    }

    public EntityUid? TryRestoreAsGhost(EntityUid temporaryBody)
    {
        if (!TryComp<DmArenaMindSwapComponent>(temporaryBody, out var temp)
            || !TryComp<MindComponent>(temp.OriginalMind, out var origMind))
            return null;

        var coords = _transform.GetMapCoordinates(temporaryBody);

        CleanupDisposableMind(temporaryBody, temp);

        var ghost = Spawn("MobObserver", coords);
        _mind.Visit(temp.OriginalMind, ghost, origMind);

        if (TryComp<GhostComponent>(ghost, out var ghostComp))
            _ghost.SetCanReturnToBody((ghost, ghostComp), origMind.OwnedEntity is { } owned && Exists(owned));

        if (GetGhostName(origMind) is { } name)
            _meta.SetEntityName(ghost, FormattedMessage.EscapeText(name));

        RemComp<DmArenaMindSwapComponent>(temporaryBody);
        return ghost;
    }

    private string? GetGhostName(MindComponent mind)
    {
        if (!string.IsNullOrWhiteSpace(mind.CharacterName))
            return mind.CharacterName;

        return mind.UserId is { } userId && _playerManager.TryGetSessionById(userId, out var session)
            ? session.Name
            : null;
    }

    public bool TryRestoreToOriginalBody(EntityUid temporaryBody)
    {
        if (!TryComp<DmArenaMindSwapComponent>(temporaryBody, out var temp)
            || !TryComp<MindComponent>(temp.OriginalMind, out var origMind)
            || origMind.OwnedEntity is not { } originalBody
            || !Exists(originalBody))
            return false;

        CleanupDisposableMind(temporaryBody, temp);

        if (origMind.UserId is { } userId && _playerManager.TryGetSessionById(userId, out var session))
            _playerManager.SetAttachedEntity(session, originalBody);

        RemComp<DmArenaMindSwapComponent>(temporaryBody);
        return true;
    }

    private void CleanupDisposableMind(EntityUid temporaryBody, DmArenaMindSwapComponent temp)
    {
        if (TryComp<MindContainerComponent>(temporaryBody, out var tempContainer)
            && tempContainer.LastMindStored == temp.DisposableMind)
        {
            tempContainer.LastMindStored = null;
            Dirty(temporaryBody, tempContainer);
        }

        if (Exists(temp.DisposableMind))
        {
            _mind.WipeMind(temp.DisposableMind);
            QueueDel(temp.DisposableMind);
        }

        if (!TryComp<MindComponent>(temp.OriginalMind, out var origMind)
            || origMind.OriginalOwnerUserId is not { } userId)
            return;

        _mind.SetUserId(temp.OriginalMind, userId);

        if (origMind.OwnedEntity is { } originalBody)
            _mindExamine.RefreshMindStatus(originalBody);
    }
}

/// <summary>
/// Включает и выключает ДМ арену. Состояние хранится только на сервере, клиент его изменить не может.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class DmArenaSwitchCommand : LocalizedCommands
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    public override string Command => "dmarena";
    public override string Description => LocalizationManager.GetString("cmd-dmarena-desc");
    public override string Help => LocalizationManager.GetString("cmd-dmarena-help");

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_admin.HasAdminFlag(player, AdminFlags.Fun))
        {
            shell.WriteError(LocalizationManager.GetString("cmd-dmarena-no-access"));
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteError(LocalizationManager.GetString("shell-wrong-arguments-number"));
            return;
        }

        var enabled = _cfg.GetCVar(DmArenaConfigVars.DmArenaEnabled);
        var action = args.Length == 0 ? "status" : args[0].ToLowerInvariant();

        switch (action)
        {
            case "status":
                WriteStatus(shell, enabled);
                break;
            case "on" or "off":
                SetEnabled(shell, enabled, action == "on");
                break;
            default:
                shell.WriteError(LocalizationManager.GetString("cmd-dmarena-bad-argument"));
                break;
        }
    }

    private void WriteStatus(IConsoleShell shell, bool enabled)
    {
        shell.WriteLine(LocalizationManager.GetString(enabled ? "cmd-dmarena-status-on" : "cmd-dmarena-status-off"));
    }

    private void SetEnabled(IConsoleShell shell, bool current, bool value)
    {
        if (value == current)
        {
            WriteStatus(shell, current);
            return;
        }

        _cfg.SetCVar(DmArenaConfigVars.DmArenaEnabled, value);

        var who = shell.Player?.Name ?? "server";
        _adminLog.Add(LogType.AdminCommands, LogImpact.High, $"{who} {(value ? "enabled" : "disabled")} the DM arena");

        shell.WriteLine(LocalizationManager.GetString(value ? "cmd-dmarena-enabled" : "cmd-dmarena-disabled"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                new[] { "on", "off", "status" },
                LocalizationManager.GetString("cmd-dmarena-hint"));
        }

        return CompletionResult.Empty;
    }
}
