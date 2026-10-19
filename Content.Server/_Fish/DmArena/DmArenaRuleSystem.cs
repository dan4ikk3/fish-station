using System.Linq;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Fish.DmArena;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Dataset;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Network;
using Robust.Server.Audio;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._Fish.DmArena;

public sealed class DmArenaRuleSystem : EntitySystem
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
    [Dependency] private TemporaryMindSystem _tempMind = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private InventorySystem _inventory = default!;

    private static readonly EntProtoId RulePrototype = "DmArenaRule";
    private static readonly ProtoId<LocalizedDatasetPrototype> DmArenaNameSuffixDataset = "NamesOperationPrefix";
    private EntityUid? _ruleEntity;
    private bool _enabled = true;

    private bool _refillOnKill;

    private readonly Dictionary<ICommonSession, DmArenaLoadoutEui> _activeEuis = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, DmArenaCVars.DmArenaRefill, value => _refillOnKill = value, true);
        Subs.CVar(_cfg, DmArenaCVars.DmArenaEnabled, OnEnabledChanged, true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundEnding);
        SubscribeLocalEvent<DmArenaRuleComponent, RuleLoadedGridsEvent>(OnGridsLoaded);
        SubscribeNetworkEvent<DmArenaJoinRequestEvent>(OnJoinRequest);
        SubscribeNetworkEvent<DmArenaLeaveRequestEvent>(OnLeaveRequest);
        SubscribeLocalEvent<DmArenaPlayerComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<DmArenaOriginalBodyComponent, MobStateChangedEvent>(OnOriginalBodyStateChanged);
        SubscribeNetworkEvent<DmArenaRevivalAcceptEvent>(OnRevivalAccept);
        SubscribeNetworkEvent<DmArenaExitChoiceEvent>(OnExitChoice);
        SubscribeLocalEvent<DmArenaPlayerComponent, SuicideGhostEvent>(OnSuicideAttempt);
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<DmArenaProtectedComponent, BeforeDamageChangedEvent>(OnArenaEntityDamage);
        SubscribeLocalEvent<TimedDespawnComponent, EntGotInsertedIntoContainerMessage>(OnDespawnPickedUp);
        SubscribeLocalEvent<DmArenaOriginalBodyComponent, ExaminedEvent>(OnOriginalBodyExamined);
        SubscribeLocalEvent<DmArenaPlayerComponent, PlayerDetachedEvent>(OnPlayerDetached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_ruleEntity != null && TryComp<DmArenaRuleComponent>(_ruleEntity.Value, out var rule) && rule.Active)
        {
            var now = _timing.CurTime;
            if (now >= rule.NextCleanup)
            {
                rule.NextCleanup = now + rule.CleanupInterval;
                SweepLooseItems(rule);
            }
        }
    }

    private void EnsureRule()
    {
        if (_ruleEntity != null && !TerminatingOrDeleted(_ruleEntity.Value))
            return;

        if (!_ticker.StartGameRule(RulePrototype, out var ruleEntity))
        {
            Log.Error($"ДМ арена: не удалось запустить правило {RulePrototype}");
            return;
        }

        _ruleEntity = ruleEntity;
    }

    private void OnRoundEnding(RoundRestartCleanupEvent ev)
    {
        foreach (var eui in _activeEuis.Values)
        {
            if (eui.Player.Status != SessionStatus.Disconnected)
                eui.Close();
        }
        _activeEuis.Clear();

        if (_ruleEntity == null)
            return;

        if (TryComp<DmArenaRuleComponent>(_ruleEntity.Value, out var rule))
        {
            var query = EntityQueryEnumerator<DmArenaPlayerComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                _tempMind.TryRestoreAsGhost(uid);
                QueueDel(uid);
            }

            rule.Players.Clear();
            rule.Presets.Clear();
            rule.Active = false;
            BroadcastPlayerCount(rule);
        }

        var bodyQuery = EntityQueryEnumerator<DmArenaOriginalBodyComponent>();
        while (bodyQuery.MoveNext(out var uid, out _))
        {
            RemComp<DmArenaOriginalBodyComponent>(uid);
        }

        _ruleEntity = null;
    }

    private void OnGridsLoaded(Entity<DmArenaRuleComponent> ent, ref RuleLoadedGridsEvent args)
    {
        ent.Comp.ArenaMap = args.Map;
        ent.Comp.ArenaGrids.UnionWith(args.Grids);
        ent.Comp.Active = true;
        WorldGuard(args.Map);
        BroadcastPlayerCount(ent.Comp);
    }

    private void WorldGuard(MapId map)
    {
        var query = EntityQueryEnumerator<DamageableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map && !HasComp<DmArenaPlayerComponent>(uid))
                EnsureComp<DmArenaProtectedComponent>(uid);
        }
    }

    private static void OnArenaEntityDamage(Entity<DmArenaProtectedComponent> ent, ref BeforeDamageChangedEvent args)
    {
        args.Cancelled = true;
    }

    private void OnJoinRequest(DmArenaJoinRequestEvent ev, EntitySessionEventArgs args)
    {
        TryOpenLoadout(args.SenderSession);
    }

    private void TryOpenLoadout(ICommonSession session)
    {
        if (!_enabled)
        {
            // Клиент мог не знать, что арена выключена
            RaiseNetworkEvent(new DmArenaPlayerCountEvent(0, false), session.Channel);
            return;
        }

        EnsureRule();

        if (_ruleEntity == null
            || !TryComp<DmArenaRuleComponent>(_ruleEntity.Value, out var rule)
            || !rule.Active)
        {
            Log.Warning($"ДМ арена: правило не запущено или карта не загружена (rule={_ruleEntity})");
            return;
        }

        if (session.AttachedEntity is not { Valid: true } ghostEntity
            || !HasComp<GhostComponent>(ghostEntity)
            || HasComp<DmArenaPlayerComponent>(ghostEntity))
        {
            Log.Warning($"ДМ арена: {session.Name} не призрак или уже на арене");
            return;
        }

        if (_activeEuis.TryGetValue(session, out var existingEui))
        {
            existingEui.Close();
            _activeEuis.Remove(session);
        }

        var eui = new DmArenaLoadoutEui(this, _ruleEntity.Value, session);
        _euiManager.OpenEui(eui, session);
        _activeEuis[session] = eui;
    }

    private void OnExitChoice(DmArenaExitChoiceEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.AttachedEntity is not { Valid: true } ghost
            || !HasComp<GhostComponent>(ghost)
            || HasComp<DmArenaPlayerComponent>(ghost))
            return;

        if (ev.Stay)
        {
            TryOpenLoadout(session);
            return;
        }

        if (!_mind.TryGetMind(ghost, out var mindId, out var mind)
            || mind.OwnedEntity is not { Valid: true } body
            || body == ghost
            || !Exists(body))
            return;

        _mind.UnVisit(mindId, mind);

        if (!TerminatingOrDeleted(ghost))
            QueueDel(ghost);
    }

    private void OnLeaveRequest(DmArenaLeaveRequestEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.AttachedEntity is not { Valid: true } entity
            || !TryComp<DmArenaPlayerComponent>(entity, out var tdPlayer))
            return;

        LeaveDmArena((entity, tdPlayer));
    }

    private void OnMobStateChanged(Entity<DmArenaPlayerComponent> ent, ref MobStateChangedEvent args)
    {
        if (ent.Comp.RuleEntity == null
            || !TryComp<DmArenaRuleComponent>(ent.Comp.RuleEntity.Value, out var rule))
            return;

        if (args.NewMobState == MobState.Critical && args.Origin is { } attacker && HasComp<DmArenaPlayerComponent>(attacker))
            ent.Comp.LastAttacker = attacker;

        if (args.NewMobState != MobState.Dead)
            return;

        CreditKill(ent, rule, args.Origin);
        GhostDomePlayer(ent, rule, playSound: false);
    }

    public void SpawnPlayer(ICommonSession session, EntityUid ruleEntity, List<int> items)
    {
        if (!_enabled
            || !TryComp<DmArenaRuleComponent>(ruleEntity, out var rule)
            || !rule.Active
            || session.AttachedEntity is not { Valid: true } ghostEntity
            || !HasComp<GhostComponent>(ghostEntity)
            || !TryGetValidItems(rule, items, out var chosen))
            return;

        var spawnCoords = GetRandomSpawnPoint(rule);
        if (spawnCoords == null || !_mind.TryGetMind(ghostEntity, out _, out var mindComp))
            return;

        HumanoidCharacterProfile? profile = null;
        if (mindComp.UserId is { } userId
            && _prefs.TryGetCachedPreferences(userId, out var prefs)
            && prefs.SelectedCharacter is HumanoidCharacterProfile selectedProfile)
        {
            var dataset = _prototype.Index(DmArenaNameSuffixDataset);
            var suffix = _loc.GetString(_random.Pick(dataset.Values));
            profile = selectedProfile.WithName($"{suffix} {selectedProfile.Name}");
        }

        var originalBody = mindComp.OwnedEntity != ghostEntity ? mindComp.OwnedEntity : null;

        var mob = _stationSpawning.SpawnPlayerMob(spawnCoords.Value, null, profile, null);

        // Выдача могла упасть: удаляем тело без владельца
        try
        {
            _stationSpawning.EquipStartingGear(mob, rule.Gear);
            foreach (var (item, count) in chosen.OrderByDescending(x => x.Item.EquipFirst))
            {
                // Старую одежду удаляем, иначе она упадёт на пол
                ClearReplacedSlots(mob, item.Gear);
                for (var i = 0; i < count; i++)
                {
                    _stationSpawning.EquipStartingGear(mob, item.Gear);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"ДМ арена: не удалось выдать снаряжение игроку {session.Name}: {e}");
            QueueDel(mob);
            return;
        }

        var tdPlayer = EnsureComp<DmArenaPlayerComponent>(mob);
        tdPlayer.RuleEntity = ruleEntity;

        if (originalBody is { Valid: true } body && !HasComp<DmArenaOriginalBodyComponent>(body))
        {
            var marker = EnsureComp<DmArenaOriginalBodyComponent>(body);
            if (mindComp.UserId is { } ownerId)
                marker.Owner = ownerId;

        }

        if (!_tempMind.TrySwapTempMind(session, mob))
        {
            QueueDel(mob);
            return;
        }

        rule.Players.Add(GetNetEntity(mob));

        _activeEuis.Remove(session);

        BroadcastPlayerCount(rule);
    }

    private void ClearReplacedSlots(EntityUid mob, ProtoId<StartingGearPrototype> gear)
    {
        foreach (var slot in _prototype.Index(gear).Equipment.Keys)
        {
            if (!_inventory.TryGetSlotEntity(mob, slot, out var old))
                continue;

            _inventory.TryUnequip(mob, slot, silent: true, force: true);
            QueueDel(old);
        }
    }

    private void OnEnabledChanged(bool value)
    {
        _enabled = value;

        if (value)
        {
            BroadcastPlayerCount();
            return;
        }

        foreach (var eui in _activeEuis.Values)
        {
            eui.Close();
        }
        _activeEuis.Clear();

        var query = EntityQueryEnumerator<DmArenaPlayerComponent>();
        var players = new List<Entity<DmArenaPlayerComponent>>();
        while (query.MoveNext(out var uid, out var player))
        {
            players.Add((uid, player));
        }

        foreach (var player in players)
        {
            LeaveDmArena(player);
        }

        BroadcastPlayerCount();
    }

    private void CleanUpPlayer(Entity<DmArenaPlayerComponent> ent, DmArenaRuleComponent rule, bool playSound, SoundPathSpecifier sound)
    {
        rule.Players.Remove(GetNetEntity(ent));

        if (playSound && _transform.TryGetMapOrGridCoordinates(ent, out var deathCoords))
        {
            var name = Identity.Entity(ent, EntityManager);
            _popup.PopupCoordinates(Loc.GetString("dm-arena-leave-01", ("user", name)),
                deathCoords.Value,
                PopupType.LargeCaution);
            var filter = Filter.Pvs(deathCoords.Value, 1, EntityManager, _playerManager);
            var audioParams = new AudioParams().WithVolume(3);
            _audio.PlayStatic(sound, filter, deathCoords.Value, true, audioParams);
        }

        TryComp<ActorComponent>(ent, out var actor);

        ClearOriginalBodyMarker(ent);
        _tempMind.TryRestoreAsGhost(ent);
        QueueDel(ent);
        BroadcastPlayerCount(rule);

        if (actor != null && _enabled)
            RaiseNetworkEvent(new DmArenaExitOfferEvent(), actor.PlayerSession.Channel);
    }

    private void OnSuicideAttempt(Entity<DmArenaPlayerComponent> ent, ref SuicideGhostEvent args)
    {
        if (ent.Comp.RuleEntity == null
            || !TryComp<DmArenaRuleComponent>(ent.Comp.RuleEntity.Value, out var rule)
            || args.Victim != ent.Owner
            )
            return;

        CreditKill(ent, rule);
        GhostDomePlayer(ent, rule);
        args.Handled = true;
    }

    private void OnGhostAttempt(GhostAttemptHandleEvent args)
    {
        if (args.Mind.CurrentEntity is not { } entity
            || !TryComp<DmArenaPlayerComponent>(entity, out var tdPlayer)
            || tdPlayer.RuleEntity == null
            || !TryComp<DmArenaRuleComponent>(tdPlayer.RuleEntity.Value, out var rule))
            return;

        CreditKill((entity, tdPlayer), rule);
        GhostDomePlayer((entity, tdPlayer), rule);
        args.Handled = true;
        args.Result = true;
    }

    private void CreditKill(Entity<DmArenaPlayerComponent> victim, DmArenaRuleComponent rule, EntityUid? killer = null)
    {
        victim.Comp.Deaths++;

        _mind.TryGetMind(victim, out _, out var deadMind);
        if (deadMind?.UserId is { } deadUserId)
        {
            rule.Deaths.TryGetValue(deadUserId, out var existingDeaths);
            rule.Deaths[deadUserId] = existingDeaths + 1;
        }

        killer ??= victim.Comp.LastAttacker;
        if (killer is not { } killerUid || !TryComp<DmArenaPlayerComponent>(killerUid, out var killerComp))
            return;

        killerComp.Kills++;

        if (_mind.TryGetMind(killerUid, out _, out var killerMind) && killerMind.UserId is { } killerUserId)
        {
            rule.Kills.TryGetValue(killerUserId, out var existingKills);
            rule.Kills[killerUserId] = existingKills + 1;
        }

        if (_refillOnKill)
            RefillAmmo(killerUid);
    }

    private void GhostDomePlayer(
        Entity<DmArenaPlayerComponent> ent,
        DmArenaRuleComponent rule,
        bool playSound = true,
        SoundPathSpecifier? sound = null)
    {
        sound ??= new SoundPathSpecifier("/Audio/Effects/pop_high.ogg");
        CleanUpPlayer(ent, rule, playSound, sound);
    }


    private void LeaveDmArena(Entity<DmArenaPlayerComponent> ent)
    {
        if (ent.Comp.RuleEntity == null
            || !TryComp<DmArenaRuleComponent>(ent.Comp.RuleEntity.Value, out var rule))
            return;

        rule.Players.Remove(GetNetEntity(ent));
        ClearOriginalBodyMarker(ent);
        _tempMind.TryRestoreAsGhost(ent);
        QueueDel(ent);

        BroadcastPlayerCount(rule);
    }

    private void OnOriginalBodyStateChanged(Entity<DmArenaOriginalBodyComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is MobState.Dead or MobState.Invalid || args.OldMobState == MobState.Alive)
            return;

        if (!_playerManager.TryGetSessionById(ent.Comp.Owner, out var session)
            || session.AttachedEntity is not { Valid: true })
            return;

        RaiseNetworkEvent(new DmArenaRevivalOfferEvent(), session.Channel);
    }

    private void OnRevivalAccept(DmArenaRevivalAcceptEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.AttachedEntity is not { Valid: true } currentEntity
            || !TryComp<DmArenaPlayerComponent>(currentEntity, out var tdPlayer)
            || !TryComp<TemporaryMindComponent>(currentEntity, out var tempMind))
            return;

        if (!TryComp<MindComponent>(tempMind.OriginalMind, out var origMind))
            return;

        var originalBody = origMind.OwnedEntity;
        if (originalBody == null || !Exists(originalBody)
            || !TryComp<MobStateComponent>(originalBody, out var mobState)
            || mobState.CurrentState == MobState.Dead)
            return;

        if (tdPlayer.RuleEntity != null
            && TryComp<DmArenaRuleComponent>(tdPlayer.RuleEntity.Value, out var rule))
        {
            rule.Players.Remove(GetNetEntity(currentEntity));
            BroadcastPlayerCount(rule);
        }

        _tempMind.TryRestoreToOriginalBody(currentEntity);
        RemComp<DmArenaOriginalBodyComponent>(originalBody.Value);
        QueueDel(currentEntity);
    }

    private void SweepLooseItems(DmArenaRuleComponent rule)
    {
        if (rule.ArenaMap is not { } map)
            return;

        var items = EntityQueryEnumerator<ItemComponent, TransformComponent>();
        while (items.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map)
                MarkForDespawn(uid, rule.SweepDespawnTime, checkContainer: true);
        }

        var puddles = EntityQueryEnumerator<PuddleComponent, TransformComponent>();
        while (puddles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map)
                MarkForDespawn(uid, rule.SweepDespawnTime);
        }
    }

    private void OnOriginalBodyExamined(Entity<DmArenaOriginalBodyComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (TryComp<MobStateComponent>(ent, out var mobState) && mobState.CurrentState == MobState.Dead)
            args.PushMarkup($"[color=yellow]{Loc.GetString("comp-mind-examined-dead-and-ssd", ("ent", ent))}[/color]");
        else
            args.PushMarkup($"[color=yellow]{Loc.GetString("comp-mind-examined-ssd", ("ent", ent))}[/color]");
    }

    private void OnPlayerDetached(Entity<DmArenaPlayerComponent> ent, ref PlayerDetachedEvent args)
    {
        if (args.Player.Status != SessionStatus.Disconnected)
            return;

        // Разум мог быть уже очищен, ищем по UserId
        var query = EntityQueryEnumerator<DmArenaOriginalBodyComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Owner == args.Player.UserId)
            {
                RemComp<DmArenaOriginalBodyComponent>(uid);
                break;
            }
        }

        if (ent.Comp.RuleEntity == null
            || !TryComp<DmArenaRuleComponent>(ent.Comp.RuleEntity.Value, out var rule))
            return;

        rule.Players.Remove(GetNetEntity(ent));
        QueueDel(ent);
        BroadcastPlayerCount(rule);
    }

    private void ClearOriginalBodyMarker(EntityUid tempBody)
    {
        if (TryComp<TemporaryMindComponent>(tempBody, out var temp)
            && TryComp<MindComponent>(temp.OriginalMind, out var origMind)
            && origMind.OwnedEntity is { } originalBody)
            RemComp<DmArenaOriginalBodyComponent>(originalBody);
    }

    private void OnDespawnPickedUp(Entity<TimedDespawnComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (HasComp<DmArenaPlayerComponent>(args.Container.Owner))
            RemComp<TimedDespawnComponent>(ent);
    }

    private void MarkForDespawn(EntityUid uid, float lifetime, bool checkContainer = false)
    {
        if (HasComp<TimedDespawnComponent>(uid))
            return;

        if (checkContainer && _container.IsEntityInContainer(uid))
            return;

        EnsureComp<TimedDespawnComponent>(uid).Lifetime = lifetime;
    }

    private EntityCoordinates? GetRandomSpawnPoint(DmArenaRuleComponent rule)
    {
        if (rule.ArenaMap == null)
            return null;

        var spawns = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out _, out var spawn, out var xform))
        {
            if (spawn.SpawnType != SpawnPointType.LateJoin)
                continue;

            if (xform.GridUid is not { } grid || !rule.ArenaGrids.Contains(grid))
                continue;

            spawns.Add(xform.Coordinates);
        }

        if (spawns.Count == 0)
        {
            var fallbackQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (fallbackQuery.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid is not { } grid || !rule.ArenaGrids.Contains(grid))
                    continue;

                spawns.Add(xform.Coordinates);
            }
        }

        return spawns.Count > 0 ? _random.Pick(spawns) : null;
    }

    private void BroadcastPlayerCount()
    {
        if (_ruleEntity != null && TryComp<DmArenaRuleComponent>(_ruleEntity.Value, out var rule))
            BroadcastPlayerCount(rule);
        else
            BroadcastPlayerCount(0);
    }

    private void BroadcastPlayerCount(DmArenaRuleComponent rule)
    {
        BroadcastPlayerCount(rule.Players.Count);
    }

    private void BroadcastPlayerCount(int count)
    {
        var ev = new DmArenaPlayerCountEvent(count, _enabled);
        foreach (var session in _playerManager.Sessions)
        {
            RaiseNetworkEvent(ev, session.Channel);
        }
    }

    public DmArenaLoadoutEuiState GetLoadoutState(DmArenaRuleComponent rule, NetUserId userId)
    {
        var items = new List<DmArenaShopOption>();
        var sets = new List<DmArenaSetOption>();
        for (var i = 0; i < rule.ShopItems.Count; i++)
        {
            var item = rule.ShopItems[i];
            var gear = _prototype.Index(item.Gear);
            items.Add(new DmArenaShopOption
            {
                Index = i,
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
            });
        }

        foreach (var set in rule.Sets)
        {
            var option = new DmArenaSetOption
            {
                Name = Loc.GetString(set.Name),
                Description = set.Description is { } setDescription ? Loc.GetString(setDescription) : string.Empty,
            };

            foreach (var gearId in set.Items)
            {
                var index = rule.ShopItems.FindIndex(x => x.Gear == gearId);
                if (index < 0)
                    continue;

                option.Items.Add(index);
                option.Cost += rule.ShopItems[index].Cost;
            }

            if (option.Items.Count > 0 && option.Cost <= rule.Budget)
                sets.Add(option);
        }

        rule.Presets.TryGetValue(userId, out var preset);
        return new DmArenaLoadoutEuiState(items, sets, rule.Players.Count, rule.Budget, preset, rule.Gear);
    }

    public void SavePreset(DmArenaRuleComponent rule, NetUserId userId, List<int> items)
    {
        if (TryGetValidItems(rule, items, out _))
            rule.Presets[userId] = new List<int>(items);
    }

    private bool TryGetValidItems(DmArenaRuleComponent rule, List<int> indexes, out List<(DmArenaShopItem Item, int Count)> items)
    {
        items = new List<(DmArenaShopItem, int)>();
        var total = 0;
        var slots = new HashSet<string>();
        var ammoTypes = new HashSet<string>();
        var hands = 0;

        foreach (var group in indexes.GroupBy(x => x))
        {
            var index = group.Key;
            if (index < 0 || index >= rule.ShopItems.Count)
                return false;

            var item = rule.ShopItems[index];
            var count = group.Count();
            var gear = _prototype.Index(item.Gear);

            // Повторно берутся только расходники
            if (count > item.MaxCount || count > 1 && (gear.Equipment.Count > 0 || gear.Inhand.Count > 0))
                return false;

            // Нельзя два предмета в один слот и больше двух в руки
            foreach (var slot in gear.Equipment.Keys)
            {
                if (!slots.Add(slot))
                    return false;
            }

            if (item.AmmoType != null)
                ammoTypes.Add(item.AmmoType);

            hands += gear.Inhand.Count;
            total += item.Cost * count;
            items.Add((item, count));
        }

        // Боеприпасы только к выбранному оружию
        foreach (var (item, _) in items)
        {
            if (item.AmmoFor != null && item.AmmoFor != "base" && !ammoTypes.Contains(item.AmmoFor))
                return false;
        }

        return total <= rule.Budget && hands <= 2;
    }

    private void RefillAmmo(EntityUid killer)
    {
        var toCheck = new Queue<EntityUid>();
        toCheck.Enqueue(killer);

        while (toCheck.Count > 0)
        {
            var current = toCheck.Dequeue();

            if (!TryComp<ContainerManagerComponent>(current, out var containerManager))
                continue;

            foreach (var container in _container.GetAllContainers(current, containerManager))
            {
                var inGun = container.ID is "gun_magazine" or "gun_chamber" or "revolver-ammo";

                foreach (var contained in container.ContainedEntities)
                {
                    toCheck.Enqueue(contained);

                    if (!inGun && TryComp<BallisticAmmoProviderComponent>(contained, out var ballistic))
                        RefillBallistic((contained, ballistic));

                    if (!inGun && (HasComp<BatteryAmmoProviderComponent>(contained)))
                        RefillBattery(contained);

                    if (!inGun && TryComp<RevolverAmmoProviderComponent>(contained, out var revolver))
                        RefillRevolver((contained, revolver));
                }
            }
        }
    }

    private void RefillBallistic(Entity<BallisticAmmoProviderComponent> ent)
    {
        _gun.RefillBallisticAmmo(ent);
    }

    private void RefillBattery(EntityUid uid)
    {
        var getCharge = new GetChargeEvent();
        RaiseLocalEvent(uid, ref getCharge);

        if (getCharge.MaxCharge <= 0)
            return;

        var delta = getCharge.MaxCharge - getCharge.CurrentCharge;
        if (delta <= 0)
            return;

        var change = new ChangeChargeEvent(delta);
        RaiseLocalEvent(uid, ref change);
    }

    private void RefillRevolver(Entity<RevolverAmmoProviderComponent> ent)
    {
        for (var i = 0; i < ent.Comp.AmmoSlots.Count; i++)
        {
            if (ent.Comp.AmmoSlots[i] is { } ammoEnt)
            {
                _container.Remove(ammoEnt, ent.Comp.AmmoContainer);
                QueueDel(ammoEnt);
                ent.Comp.AmmoSlots[i] = null;
            }

            if (i < ent.Comp.Chambers.Length)
                ent.Comp.Chambers[i] = true;
        }

        Dirty(ent);
    }
}
