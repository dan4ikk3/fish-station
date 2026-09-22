using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Server.Telephone;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Holopad;
using Content.Shared.Mobs;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Power;
using Content.Shared.Telephone;
using Robust.Shared.GameObjects;
using Content.Shared.Damage.Systems;

namespace Content.Server.Holopad;

/// <summary>
/// Реализует «транслирующие» голопады: пользователь передатчика видит мир от лица голограммы
/// на приёмнике, оставаясь при этом в собственном теле и теряя возможность двигаться.
/// Направление вызова жёстко ограничено «передатчик → приёмник», и у передатчика может быть
/// только один активный приёмник одновременно.
/// </summary>
public sealed class RemoteHolopadSystem : EntitySystem
{
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly TelephoneSystem _telephone = default!;
    [Dependency] private readonly SharedHolopadSystem _holopad = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TelephoneCallAttemptEvent>(OnCallAttempt);

        SubscribeLocalEvent<RemoteHolopadTransmitterComponent, RemoteHolopadStopBroadcastMessage>(OnStopBroadcast);

        SubscribeLocalEvent<RemoteHolopadTransmitterComponent, ComponentShutdown>(OnTransmitterShutdown);
        SubscribeLocalEvent<RemoteHolopadTransmitterComponent, PowerChangedEvent>(OnTransmitterPower);

        SubscribeLocalEvent<RemoteViewerFrozenComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
        SubscribeLocalEvent<RemoteViewerFrozenComponent, ComponentShutdown>(OnFrozenShutdown);

        SubscribeLocalEvent<RemoteViewerFrozenComponent, DamageChangedEvent>(OnViewerDamaged);
        SubscribeLocalEvent<RemoteViewerFrozenComponent, PullAttemptEvent>(OnViewerPulled);
        SubscribeLocalEvent<RemoteViewerFrozenComponent, MobStateChangedEvent>(OnViewerMobState);
    }

    #region: Публичное API для хуков из HolopadSystem

    /// <summary>
    /// Разрешён ли звонок/видимость source → receiver с учётом remote-сети. Если хотя бы одна
    /// из сторон участвует в remote-сети, разрешено только направление «передатчик → приёмник»;
    /// если ни одна не участвует — ограничений нет (обычные голопады работают как раньше).
    /// </summary>
    public bool CanCall(EntityUid source, EntityUid receiver)
    {
        var sourceIsTransmitter = HasComp<RemoteHolopadTransmitterComponent>(source);
        var sourceIsReceiver = HasComp<RemoteHolopadTransmitterComponent>(source);
        var receiverIsTransmitter = HasComp<RemoteHolopadTransmitterComponent>(receiver);
        var receiverIsReceiver = HasComp<RemoteHolopadTransmitterComponent>(receiver);

        if (!sourceIsTransmitter && !sourceIsReceiver && !receiverIsTransmitter && !receiverIsReceiver)
            return true;

        return sourceIsTransmitter && receiverIsReceiver;
    }

    /// <summary>
    /// Хук для HolopadSystem.UpdateUIState — показывать ли receiver в списке контактов source.
    /// </summary>
    public bool IsListedFor(EntityUid source, EntityUid receiver) => CanCall(source, receiver);

    #endregion

    #region: Изоляция сети

    /// <summary>
    /// Разрешает звонок только в направлении «передатчик → приёмник» и не даёт передатчику
    /// накопить больше одного активного приёмника (иначе выбор «текущего» в Update станет
    /// неоднозначным при широковещательном вызове).
    /// </summary>
    private void OnCallAttempt(ref TelephoneCallAttemptEvent ev)
    {
        if (!CanCall(ev.Source, ev.Receiver))
        {
            ev.Cancelled = true;
            return;
        }

        if (HasComp<RemoteHolopadTransmitterComponent>(ev.Source) &&
            TryComp<TelephoneComponent>(ev.Source, out var sourceTelephone) &&
            sourceTelephone.LinkedTelephones.Count > 0)
        {
            ev.Cancelled = true;
        }
    }

    #endregion

    #region: Основная логика

    /// <summary>
    /// Каждый тик синхронизирует заморозку и цель взгляда зрителя передатчика с текущим
    /// состоянием звонка и голограммой на приёмнике.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RemoteHolopadTransmitterComponent, HolopadComponent, TelephoneComponent>();
        while (query.MoveNext(out var uid, out var transmitter, out var holopad, out var telephone))
        {
            if (telephone.CurrentState != TelephoneState.InCall || holopad.User == null)
            {
                Cleanup((uid, transmitter));
                continue;
            }

            var viewer = holopad.User.Value.Owner;
            var receiver = telephone.LinkedTelephones.FirstOrDefault().Owner;

            if (receiver == default ||
                !TryComp<HolopadComponent>(receiver, out var receiverHolo) ||
                receiverHolo.Hologram == null)
                continue;

            var hologram = receiverHolo.Hologram.Value.Owner;
            var viewerChanged = transmitter.ViewerBody != viewer;

            if (viewerChanged)
            {
                if (transmitter.ViewerBody is { } old && Exists(old))
                    Unfreeze(old);

                transmitter.ViewerBody = viewer;
                EnsureComp<RemoteViewerFrozenComponent>(viewer);
                _blocker.UpdateCanMove(viewer);
                Dirty(uid, transmitter);
            }

            // Fish: переназначаем цель взгляда при смене ЗРИТЕЛЯ, а не только при смене голограммы —
            // иначе новый зритель мог остаться без цели, если голограмма не поменялась
            if (viewerChanged || transmitter.RemoteHologram != hologram)
            {
                if (TryComp<EyeComponent>(viewer, out var eye))
                    _eye.SetTarget(viewer, hologram, eye);

                transmitter.RemoteHologram = hologram;
                transmitter.ReceiverHolopad = receiver;
                Dirty(uid, transmitter);
            }
        }
    }

    /// <summary>
    /// Снимает заморозку и цель взгляда с текущего зрителя передатчика и обнуляет состояние.
    /// </summary>
    private void Cleanup(Entity<RemoteHolopadTransmitterComponent> ent)
    {
        if (ent.Comp.ViewerBody is { } viewer && Exists(viewer))
            Unfreeze(viewer);

        if (ent.Comp.ViewerBody == null &&
            ent.Comp.RemoteHologram == null &&
            ent.Comp.ReceiverHolopad == null)
            return;

        ent.Comp.ViewerBody = null;
        ent.Comp.RemoteHologram = null;
        ent.Comp.ReceiverHolopad = null;
        Dirty(ent);
    }

    /// <summary>
    /// Возвращает зрителю собственный взгляд и снимает заморозку.
    /// </summary>
    private void Unfreeze(EntityUid viewer)
    {
        if (TryComp<EyeComponent>(viewer, out var eye))
            _eye.SetTarget(viewer, null, eye);

        RemComp<RemoteViewerFrozenComponent>(viewer);
        _blocker.UpdateCanMove(viewer);
    }

    #endregion

    #region: Остановка трансляции (OnEvent -> Try -> Can -> Do)

    /// <summary>
    /// Точка входа от BUI-сообщения — делегирует в TryStopBroadcast.
    /// </summary>
    private void OnStopBroadcast(Entity<RemoteHolopadTransmitterComponent> ent, ref RemoteHolopadStopBroadcastMessage args)
    {
        TryStopBroadcast(ent, args.Actor);
    }

    /// <summary>
    /// Пытается остановить трансляцию от лица actor. Возвращает false, если запрос отклонён
    /// (нет активного звонка или голопад заблокирован управлением другого пользователя).
    /// </summary>
    public bool TryStopBroadcast(Entity<RemoteHolopadTransmitterComponent> ent, EntityUid actor)
    {
        if (!CanStopBroadcast(ent, actor))
            return false;

        DoStopBroadcast(ent);
        return true;
    }

    /// <summary>
    /// Fish: проверка авторизации — раньше отсутствовала, что позволяло постороннему рядом
    /// с голопадом оборвать чужой звонок во время блокировки управления (ControlLockoutOwner).
    /// </summary>
    private bool CanStopBroadcast(Entity<RemoteHolopadTransmitterComponent> ent, EntityUid actor)
    {
        if (!TryComp<TelephoneComponent>(ent, out var telephone) ||
            telephone.CurrentState != TelephoneState.InCall)
            return false;

        if (!TryComp<HolopadComponent>(ent, out var holopad))
            return false;

        return !_holopad.IsHolopadControlLocked((ent.Owner, holopad), actor);
    }

    /// <summary>
    /// Непосредственно завершает звонок. Вызывается только после успешной CanStopBroadcast.
    /// </summary>
    private void DoStopBroadcast(Entity<RemoteHolopadTransmitterComponent> ent)
    {
        if (TryComp<TelephoneComponent>(ent, out var tel))
            _telephone.EndTelephoneCalls((ent.Owner, tel));
    }

    #endregion

    #region: Аварийные выходы

    /// <summary>
    /// Передатчик удалён — сбрасываем состояние его зрителя.
    /// </summary>
    private void OnTransmitterShutdown(Entity<RemoteHolopadTransmitterComponent> ent, ref ComponentShutdown args)
    {
        Cleanup(ent);
    }

    /// <summary>
    /// Передатчик обесточен — завершаем звонок штатным путём (Cleanup сработает через
    /// смену состояния телефона).
    /// </summary>
    private void OnTransmitterPower(Entity<RemoteHolopadTransmitterComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered && TryComp<TelephoneComponent>(ent, out var tel))
            _telephone.EndTelephoneCalls((ent.Owner, tel));
    }

    #endregion

    #region: Заморозка

    /// <summary>
    /// Блокирует движение, пока висит RemoteViewerFrozenComponent.
    /// </summary>
    private void OnUpdateCanMove(Entity<RemoteViewerFrozenComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;
        args.Cancel();
    }

    /// <summary>
    /// Компонент сняли — пересчитываем возможность движения.
    /// </summary>
    private void OnFrozenShutdown(Entity<RemoteViewerFrozenComponent> ent, ref ComponentShutdown args)
    {
        _blocker.UpdateCanMove(ent.Owner);
    }

    #endregion

    #region: Прерывания

    /// <summary>
    /// Зритель получил урон — трансляция прерывается (не должен спокойно стоять под обстрелом).
    /// </summary>
    private void OnViewerDamaged(Entity<RemoteViewerFrozenComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            Interrupt(ent.Owner);
    }

    /// <summary>
    /// Зрителя пытаются утащить — трансляция прерывается.
    /// </summary>
    private void OnViewerPulled(Entity<RemoteViewerFrozenComponent> ent, ref PullAttemptEvent args)
    {
        Interrupt(ent.Owner);
    }

    /// <summary>
    /// Зритель потерял сознание/умер — трансляция прерывается.
    /// </summary>
    private void OnViewerMobState(Entity<RemoteViewerFrozenComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Interrupt(ent.Owner);
    }

    /// <summary>
    /// Находит передатчик, чьим зрителем является viewer, и завершает его звонок.
    /// </summary>
    private void Interrupt(EntityUid viewer)
    {
        var query = EntityQueryEnumerator<RemoteHolopadTransmitterComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ViewerBody != viewer)
                continue;

            if (TryComp<TelephoneComponent>(uid, out var tel))
                _telephone.EndTelephoneCalls((uid, tel));
            break;
        }
    }

    #endregion
}
