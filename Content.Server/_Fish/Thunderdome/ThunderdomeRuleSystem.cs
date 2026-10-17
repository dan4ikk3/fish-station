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
using Content.Shared._Fish.Thunderdome;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Dataset;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Preferences;
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

namespace Content.Server._Fish.Thunderdome;

public sealed class ThunderdomeRuleSystem : EntitySystem
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

    private static readonly EntProtoId RulePrototype = "ThunderdomeRule";
    private static readonly ProtoId<LocalizedDatasetPrototype> ThunderdomeNameSuffixDataset = "NamesOperationPrefix";
    private EntityUid? _ruleEntity;
    private bool _refillOnKill;

    private readonly Dictionary<ICommonSession, ThunderdomeLoadoutEui> _activeEuis = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, ThunderdomeCVars.ThunderdomeRefill, value => _refillOnKill = value, true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundEnding);
        SubscribeLocalEvent<ThunderdomeRuleComponent, RuleLoadedGridsEvent>(OnGridsLoaded);
        SubscribeNetworkEvent<ThunderdomeJoinRequestEvent>(OnJoinRequest);
        SubscribeNetworkEvent<ThunderdomeLeaveRequestEvent>(OnLeaveRequest);
        SubscribeLocalEvent<ThunderdomePlayerComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ThunderdomeOriginalBodyComponent, MobStateChangedEvent>(OnOriginalBodyStateChanged);
        SubscribeNetworkEvent<ThunderdomeRevivalAcceptEvent>(OnRevivalAccept);
        SubscribeNetworkEvent<ThunderdomeExitChoiceEvent>(OnExitChoice);
        SubscribeLocalEvent<ThunderdomePlayerComponent, SuicideGhostEvent>(OnSuicideAttempt);
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<ThunderdomeArenaProtectedComponent, BeforeDamageChangedEvent>(OnArenaEntityDamage);
        SubscribeLocalEvent<TimedDespawnComponent, EntGotInsertedIntoContainerMessage>(OnDespawnPickedUp);
        SubscribeLocalEvent<ThunderdomeOriginalBodyComponent, ExaminedEvent>(OnOriginalBodyExamined);
        SubscribeLocalEvent<ThunderdomePlayerComponent, PlayerDetachedEvent>(OnPlayerDetached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_ruleEntity != null && TryComp<ThunderdomeRuleComponent>(_ruleEntity.Value, out var rule) && rule.Active)
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
        if (_ruleEntity != null)
            return;

        if (!_ticker.StartGameRule(RulePrototype, out var ruleEntity))
            return;

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

        if (TryComp<ThunderdomeRuleComponent>(_ruleEntity.Value, out var rule))
        {
            var query = EntityQueryEnumerator<ThunderdomePlayerComponent>();
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

        var bodyQuery = EntityQueryEnumerator<ThunderdomeOriginalBodyComponent>();
        while (bodyQuery.MoveNext(out var uid, out _))
        {
            RemComp<ThunderdomeOriginalBodyComponent>(uid);
        }

        _ruleEntity = null;
    }

    private void OnGridsLoaded(Entity<ThunderdomeRuleComponent> ent, ref RuleLoadedGridsEvent args)
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
            if (xform.MapID == map && !HasComp<ThunderdomePlayerComponent>(uid))
                EnsureComp<ThunderdomeArenaProtectedComponent>(uid);
        }
    }

    private static void OnArenaEntityDamage(Entity<ThunderdomeArenaProtectedComponent> ent, ref BeforeDamageChangedEvent args)
    {
        args.Cancelled = true;
    }

    private void OnJoinRequest(ThunderdomeJoinRequestEvent ev, EntitySessionEventArgs args)
    {
        TryOpenLoadout(args.SenderSession);
    }

    private void TryOpenLoadout(ICommonSession session)
    {
        if (!_cfg.GetCVar(ThunderdomeCVars.ThunderdomeEnabled))
            return;

        EnsureRule();

        if (_ruleEntity == null
            || !TryComp<ThunderdomeRuleComponent>(_ruleEntity.Value, out var rule)
            || !rule.Active)
            return;

        if (session.AttachedEntity is not { Valid: true } ghostEntity
            || !HasComp<GhostComponent>(ghostEntity)
            || HasComp<ThunderdomePlayerComponent>(ghostEntity))
            return;

        if (_activeEuis.TryGetValue(session, out var existingEui))
        {
            existingEui.Close();
            _activeEuis.Remove(session);
        }

        var eui = new ThunderdomeLoadoutEui(this, _ruleEntity.Value, session);
        _euiManager.OpenEui(eui, session);
        _activeEuis[session] = eui;
    }

    private void OnExitChoice(ThunderdomeExitChoiceEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.AttachedEntity is not { Valid: true } ghost
            || !HasComp<GhostComponent>(ghost)
            || HasComp<ThunderdomePlayerComponent>(ghost))
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

    private void OnLeaveRequest(ThunderdomeLeaveRequestEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.AttachedEntity is not { Valid: true } entity
            || !TryComp<ThunderdomePlayerComponent>(entity, out var tdPlayer))
            return;

        LeaveThunderdome((entity, tdPlayer));
    }

    private void OnMobStateChanged(Entity<ThunderdomePlayerComponent> ent, ref MobStateChangedEvent args)
    {
        if (ent.Comp.RuleEntity == null
            || !TryComp<ThunderdomeRuleComponent>(ent.Comp.RuleEntity.Value, out var rule))
            return;

        if (args.NewMobState == MobState.Critical && args.Origin is { } attacker && HasComp<ThunderdomePlayerComponent>(attacker))
            ent.Comp.LastAttacker = attacker;

        if (args.NewMobState != MobState.Dead)
            return;

        CreditKill(ent, rule, args.Origin);
        GhostDomePlayer(ent, rule, playSound: false);
    }

    public void SpawnPlayer(ICommonSession session, EntityUid ruleEntity, List<int> items)
    {
        if (!TryComp<ThunderdomeRuleComponent>(ruleEntity, out var rule)
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
            var dataset = _prototype.Index(ThunderdomeNameSuffixDataset);
            var suffix = _loc.GetString(_random.Pick(dataset.Values));
            profile = selectedProfile.WithName($"{suffix} {selectedProfile.Name}");
        }

        var originalBody = mindComp.OwnedEntity != ghostEntity ? mindComp.OwnedEntity : null;

        var mob = _stationSpawning.SpawnPlayerMob(spawnCoords.Value, null, profile, null);
        _stationSpawning.EquipStartingGear(mob, rule.Gear);
        foreach (var item in chosen.OrderByDescending(x => x.EquipFirst))
        {
            _stationSpawning.EquipStartingGear(mob, item.Gear);
        }

        var tdPlayer = EnsureComp<ThunderdomePlayerComponent>(mob);
        tdPlayer.RuleEntity = ruleEntity;

        if (originalBody is { Valid: true } body && !HasComp<ThunderdomeOriginalBodyComponent>(body))
        {
            var marker = EnsureComp<ThunderdomeOriginalBodyComponent>(body);
            if (mindComp.UserId is { } ownerId)
                marker.Owner = ownerId;

        }

        if (!_tempMind.TrySwapTempMind(session, mob))
            return;

        rule.Players.Add(GetNetEntity(mob));

        _activeEuis.Remove(session);

        BroadcastPlayerCount(rule);
    }

    private void CleanUpPlayer(Entity<ThunderdomePlayerComponent> ent, ThunderdomeRuleComponent rule, bool playSound, SoundPathSpecifier sound)
    {
        rule.Players.Remove(GetNetEntity(ent));

        if (playSound && _transform.TryGetMapOrGridCoordinates(ent, out var deathCoords))
        {
            var name = Identity.Entity(ent, EntityManager);
            _popup.PopupCoordinates(Loc.GetString("thunderdome-leave-01", ("user", name)),
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

        if (actor != null)
            RaiseNetworkEvent(new ThunderdomeExitOfferEvent(), actor.PlayerSession.Channel);
    }

    private void OnSuicideAttempt(Entity<ThunderdomePlayerComponent> ent, ref SuicideGhostEvent args)
    {
        if (ent.Comp.RuleEntity == null
            || !TryComp<ThunderdomeRuleComponent>(ent.Comp.RuleEntity.Value, out var rule)
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
            || !TryComp<ThunderdomePlayerComponent>(entity, out var tdPlayer)
            || tdPlayer.RuleEntity == null
            || !TryComp<ThunderdomeRuleComponent>(tdPlayer.RuleEntity.Value, out var rule))
            return;

        CreditKill((entity, tdPlayer), rule);
        GhostDomePlayer((entity, tdPlayer), rule);
        args.Handled = true;
        args.Result = true;
    }

    private void CreditKill(Entity<ThunderdomePlayerComponent> victim, ThunderdomeRuleComponent rule, EntityUid? killer = null)
    {
        victim.Comp.Deaths++;
        victim.Comp.CurrentStreak = 0;

        _mind.TryGetMind(victim, out _, out var deadMind);
        if (deadMind?.UserId is { } deadUserId)
        {
            rule.Deaths.TryGetValue(deadUserId, out var existingDeaths);
            rule.Deaths[deadUserId] = existingDeaths + 1;
        }

        killer ??= victim.Comp.LastAttacker;
        if (killer is not { } killerUid || !TryComp<ThunderdomePlayerComponent>(killerUid, out var killerComp))
            return;

        killerComp.Kills++;
        killerComp.CurrentStreak++;

        if (_mind.TryGetMind(killerUid, out _, out var killerMind) && killerMind.UserId is { } killerUserId)
        {
            rule.Kills.TryGetValue(killerUserId, out var existingKills);
            rule.Kills[killerUserId] = existingKills + 1;
            CheckKillStreak((killerUid, killerComp), rule);
        }

        if (_refillOnKill)
            RefillAmmo(killerUid);
    }

    private void GhostDomePlayer(
        Entity<ThunderdomePlayerComponent> ent,
        ThunderdomeRuleComponent rule,
        bool playSound = true,
        SoundPathSpecifier? sound = null)
    {
        sound ??= new SoundPathSpecifier("/Audio/Effects/pop_high.ogg");
        CleanUpPlayer(ent, rule, playSound, sound);
    }


    private void LeaveThunderdome(Entity<ThunderdomePlayerComponent> ent)
    {
        if (ent.Comp.RuleEntity == null
            || !TryComp<ThunderdomeRuleComponent>(ent.Comp.RuleEntity.Value, out var rule))
            return;

        rule.Players.Remove(GetNetEntity(ent));
        ClearOriginalBodyMarker(ent);
        _tempMind.TryRestoreAsGhost(ent);
        QueueDel(ent);

        BroadcastPlayerCount(rule);
    }

    private void OnOriginalBodyStateChanged(Entity<ThunderdomeOriginalBodyComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is MobState.Dead or MobState.Invalid || args.OldMobState == MobState.Alive)
            return;

        if (!_playerManager.TryGetSessionById(ent.Comp.Owner, out var session)
            || session.AttachedEntity is not { Valid: true })
            return;

        RaiseNetworkEvent(new ThunderdomeRevivalOfferEvent(), session.Channel);
    }

    private void OnRevivalAccept(ThunderdomeRevivalAcceptEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.AttachedEntity is not { Valid: true } currentEntity
            || !TryComp<ThunderdomePlayerComponent>(currentEntity, out var tdPlayer)
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
            && TryComp<ThunderdomeRuleComponent>(tdPlayer.RuleEntity.Value, out var rule))
        {
            rule.Players.Remove(GetNetEntity(currentEntity));
            BroadcastPlayerCount(rule);
        }

        _tempMind.TryRestoreToOriginalBody(currentEntity);
        RemComp<ThunderdomeOriginalBodyComponent>(originalBody.Value);
        QueueDel(currentEntity);
    }

    private void SweepLooseItems(ThunderdomeRuleComponent rule)
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

    private void OnOriginalBodyExamined(Entity<ThunderdomeOriginalBodyComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (TryComp<MobStateComponent>(ent, out var mobState) && mobState.CurrentState == MobState.Dead)
            args.PushMarkup($"[color=yellow]{Loc.GetString("comp-mind-examined-dead-and-ssd", ("ent", ent))}[/color]");
        else
            args.PushMarkup($"[color=yellow]{Loc.GetString("comp-mind-examined-ssd", ("ent", ent))}[/color]");
    }

    private void OnPlayerDetached(Entity<ThunderdomePlayerComponent> ent, ref PlayerDetachedEvent args)
    {
        if (args.Player.Status != SessionStatus.Disconnected)
            return;

        // TemporaryMindSystem may have already cleaned up, so search by UserId
        var query = EntityQueryEnumerator<ThunderdomeOriginalBodyComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Owner == args.Player.UserId)
            {
                RemComp<ThunderdomeOriginalBodyComponent>(uid);
                break;
            }
        }

        if (ent.Comp.RuleEntity == null
            || !TryComp<ThunderdomeRuleComponent>(ent.Comp.RuleEntity.Value, out var rule))
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
            RemComp<ThunderdomeOriginalBodyComponent>(originalBody);
    }

    private void OnDespawnPickedUp(Entity<TimedDespawnComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (HasComp<ThunderdomePlayerComponent>(args.Container.Owner))
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

    private EntityCoordinates? GetRandomSpawnPoint(ThunderdomeRuleComponent rule)
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

    private void CheckKillStreak(Entity<ThunderdomePlayerComponent> killer, ThunderdomeRuleComponent rule)
    {
        var streak = killer.Comp.CurrentStreak;
        if (streak < 3 || streak > 12)
            return;

        var name = Identity.Name(killer, EntityManager);
        if (!_loc.TryGetString($"thunderdome-streak-{streak}", out var message, ("player", name)))
            return;

        var ev = new ThunderdomeAnnouncementEvent(message);
        foreach (var netEntity in rule.Players)
        {
            if (!TryGetEntity(netEntity, out var playerEntity))
                continue;

            RaiseNetworkEvent(ev, playerEntity.Value);
        }
    }

    private void BroadcastPlayerCount(ThunderdomeRuleComponent rule)
    {
        var ev = new ThunderdomePlayerCountEvent(rule.Players.Count);
        foreach (var session in _playerManager.Sessions)
        {
            RaiseNetworkEvent(ev, session.Channel);
        }
    }

    public ThunderdomeLoadoutEuiState GetLoadoutState(ThunderdomeRuleComponent rule, NetUserId userId)
    {
        var items = new List<ThunderdomeShopOption>();
        for (var i = 0; i < rule.ShopItems.Count; i++)
        {
            var item = rule.ShopItems[i];
            items.Add(new ThunderdomeShopOption
            {
                Index = i,
                Name = Loc.GetString(item.Name),
                Description = item.Description is { } description ? Loc.GetString(description) : string.Empty,
                Category = Loc.GetString(item.Category),
                SpritePrototype = item.Sprite,
                GearPrototype = item.Gear,
                Cost = item.Cost,
                EquipFirst = item.EquipFirst,
            });
        }

        rule.Presets.TryGetValue(userId, out var preset);
        return new ThunderdomeLoadoutEuiState(items, rule.Players.Count, rule.Budget, preset, rule.Gear);
    }

    public void SavePreset(ThunderdomeRuleComponent rule, NetUserId userId, List<int> items)
    {
        if (TryGetValidItems(rule, items, out _))
            rule.Presets[userId] = new List<int>(items);
    }

    private static bool TryGetValidItems(ThunderdomeRuleComponent rule, List<int> indexes, out List<ThunderdomeShopItem> items)
    {
        items = new List<ThunderdomeShopItem>();
        var total = 0;

        foreach (var index in indexes.Distinct())
        {
            if (index < 0 || index >= rule.ShopItems.Count)
                return false;

            var item = rule.ShopItems[index];
            total += item.Cost;
            items.Add(item);
        }

        return total <= rule.Budget;
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
