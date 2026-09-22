using Content.Server._Fish.EyeRedirect;
using Content.Server.Popups;
using Content.Server.Telephone;
using Content.Shared._Fish.Holopad;
using Content.Shared._Fish.Movement;
using Content.Shared.Actions;
using Content.Shared.Mobs;
using Content.Shared.Power;
using Content.Shared.Telephone;
using Robust.Server.GameObjects;
using Robust.Shared.Localization;

namespace Content.Server._Fish.Holopad;

/// <summary>
/// Серверная часть one-way голопада: принудительный звонок с передатчика, на время звонка перенос
/// обзора на приёмник (EyeRedirect) и заморозка звонящего (Frozen), выдача действия «Прервать вещание»
/// и возврат всего обратно, когда звонок закончился любым способом.
///
/// В HolopadSystem из этой системы вызываются только <see cref="TryStartCall"/> и
/// <see cref="IsListedFor"/>; завершение звонка отслеживается по событиям телефона без хуков.
/// </summary>
public sealed class OneWayHolopadSystem : SharedOneWayHolopadSystem
{
    [Dependency] private readonly EyeRedirectSystem _eyeRedirect = default!;
    [Dependency] private readonly TelephoneSystem _telephone = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _xform = default!;

    private const float UpdateInterval = 1f;
    private float _updateTimer;

    public override void Initialize()
    {
        base.Initialize();

        // Завершение сеанса привязано к состоянию телефона, поэтому работает при любом способе прервать
        // звонок: кнопка в окне голопада, действие, отбой на приёмнике, потеря питания
        SubscribeLocalEvent<OneWayHolopadComponent, TelephoneStateChangeEvent>(OnTelephoneStateChange);
        SubscribeLocalEvent<OneWayHolopadComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<OneWayHolopadComponent, ComponentShutdown>(OnHolopadShutdown);

        SubscribeLocalEvent<OneWayHolopadCallerComponent, OneWayHolopadEndCallActionEvent>(OnEndCallAction);
        SubscribeLocalEvent<OneWayHolopadCallerComponent, MobStateChangedEvent>(OnCallerMobStateChanged);
        SubscribeLocalEvent<OneWayHolopadCallerComponent, ComponentShutdown>(OnCallerShutdown);
    }

    #region: Хуки для HolopadSystem

    /// <summary>
    /// Хук для HolopadSystem.OnHolopadStartNewCall. Возвращает true, если вызов относится к one-way сети
    /// и полностью обработан здесь (штатный вызов делать не нужно), и false для обычного голопада.
    /// </summary>
    public bool TryStartCall(EntityUid source, EntityUid receiver, EntityUid user)
    {
        if (!TryComp<OneWayHolopadComponent>(source, out var oneWay))
            return false;

        // Дальше вызов всегда считаем обработанным: штатный код не должен звонить в обход правил сети
        if (!oneWay.IsTransmitter ||
            !TryComp<OneWayHolopadComponent>(receiver, out var receiverOneWay) ||
            !CanCall(oneWay, receiverOneWay))
            return true;

        if (!TryComp<TelephoneComponent>(source, out var sourceTelephone) ||
            !TryComp<TelephoneComponent>(receiver, out var receiverTelephone))
            return true;

        var sourceEnt = new Entity<TelephoneComponent>(source, sourceTelephone);
        var receiverEnt = new Entity<TelephoneComponent>(receiver, receiverTelephone);

        // С этого передатчика уже идёт звонок
        if (oneWay.ActiveUser is { } activeUser)
        {
            if (_telephone.IsTelephoneEngaged(sourceEnt) && !TerminatingOrDeleted(activeUser))
                return true;

            // Хвост от прошлого звонка (сущность удалили и т.п.) — чистим и звоним заново
            EndSession(new Entity<OneWayHolopadComponent>(source, oneWay));
        }

        // Звонящий уже участвует в другом звонке
        if (HasComp<OneWayHolopadCallerComponent>(user))
            return true;

        var options = new TelephoneCallOptions
        {
            ForceConnect = true, // звонящий сам включает вещание, ответ не требуется
            IgnoreRange = true, // передатчик на ЦК, приёмник на станции — это разные карты
            MuteSource = oneWay.MuteCaller,
        };

        _telephone.CallTelephone(sourceEnt, receiverEnt, user, options);

        if (!_telephone.IsSourceConnectedToReceiver(sourceEnt, receiverEnt))
        {
            _popup.PopupEntity(Loc.GetString("fish-one-way-holopad-connection-failed"), source, user);
            return true;
        }

        BeginSession(new Entity<OneWayHolopadComponent>(source, oneWay), receiver, user);
        return true;
    }

    /// <summary>
    /// Хук для HolopadSystem.UpdateUIState: показывать ли <paramref name="receiver"/> в списке контактов
    /// <paramref name="source"/>. Обычные голопады видят друг друга как раньше, one-way — только приёмники
    /// своего канала и только в сторону «передатчик → приёмник».
    /// </summary>
    public bool IsListedFor(EntityUid source, EntityUid receiver)
    {
        TryComp<OneWayHolopadComponent>(source, out var sourceOneWay);
        TryComp<OneWayHolopadComponent>(receiver, out var receiverOneWay);

        // Обычная сеть — как раньше
        if (sourceOneWay == null && receiverOneWay == null)
            return true;

        return sourceOneWay != null && receiverOneWay != null && CanCall(sourceOneWay, receiverOneWay);
    }

    #endregion

    #region: Сеанс звонка

    private void BeginSession(Entity<OneWayHolopadComponent> ent, EntityUid receiver, EntityUid user)
    {
        ent.Comp.ActiveUser = user;
        ent.Comp.ActiveReceiver = receiver;

        var caller = EnsureComp<OneWayHolopadCallerComponent>(user);
        caller.Transmitter = ent.Owner;

        // Кнопка завершения: обзор перенесён на приёмник, окно голопада может быть не под рукой
        _actions.AddAction(user, ref caller.EndCallActionEntity, ent.Comp.EndCallAction);

        _eyeRedirect.Redirect(user, receiver);
        EnsureComp<FrozenComponent>(user);
    }

    /// <summary>
    /// Снимает с звонящего всё, что мы на него повесили. Безопасно вызывать повторно.
    /// </summary>
    private void EndSession(Entity<OneWayHolopadComponent> ent)
    {
        if (ent.Comp.ActiveUser is not { } user)
            return;

        // Обнуляем до снятия компонентов, чтобы OnCallerShutdown не полез закрывать звонок ещё раз
        ent.Comp.ActiveUser = null;
        ent.Comp.ActiveReceiver = null;

        ReleaseCaller(user);
    }

    private void ReleaseCaller(EntityUid user)
    {
        if (TerminatingOrDeleted(user))
            return;

        _eyeRedirect.RemoveRedirect(user);
        RemComp<FrozenComponent>(user);

        if (!TryComp<OneWayHolopadCallerComponent>(user, out var caller))
            return;

        _actions.RemoveAction(user, caller.EndCallActionEntity);
        RemComp<OneWayHolopadCallerComponent>(user);
    }

    /// <summary>
    /// Завершает звонок передатчика: кладёт трубку на обеих сторонах и снимает сеанс.
    /// </summary>
    private void EndCall(Entity<OneWayHolopadComponent> ent)
    {
        if (TryComp<TelephoneComponent>(ent, out var telephone))
            _telephone.EndTelephoneCalls((ent.Owner, telephone));

        // Страховка на случай, если событие смены состояния телефона не пришло
        EndSession(ent);
    }

    private void EndCallOf(Entity<OneWayHolopadCallerComponent> caller)
    {
        if (TryComp<OneWayHolopadComponent>(caller.Comp.Transmitter, out var oneWay) &&
            oneWay.ActiveUser == caller.Owner)
        {
            EndCall(new Entity<OneWayHolopadComponent>(caller.Comp.Transmitter, oneWay));
            return;
        }

        // Передатчик пропал или уже не считает эту сущность звонящим — просто отпускаем её
        ReleaseCaller(caller.Owner);
    }

    #endregion

    #region: События

    private void OnTelephoneStateChange(Entity<OneWayHolopadComponent> ent, ref TelephoneStateChangeEvent args)
    {
        if (args.NewState is TelephoneState.Idle or TelephoneState.EndingCall)
            EndSession(ent);
    }

    private void OnPowerChanged(Entity<OneWayHolopadComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered)
            return;

        // Обесточили передатчик или приёмник — звонок с их участием заканчиваем
        var query = AllEntityQuery<OneWayHolopadComponent>();
        while (query.MoveNext(out var uid, out var oneWay))
        {
            if (oneWay.ActiveUser == null)
                continue;

            if (uid == ent.Owner || oneWay.ActiveReceiver == ent.Owner)
                EndCall(new Entity<OneWayHolopadComponent>(uid, oneWay));
        }
    }

    private void OnHolopadShutdown(Entity<OneWayHolopadComponent> ent, ref ComponentShutdown args)
    {
        EndSession(ent);
    }

    private void OnEndCallAction(Entity<OneWayHolopadCallerComponent> ent, ref OneWayHolopadEndCallActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        EndCallOf(ent);
    }

    private void OnCallerMobStateChanged(Entity<OneWayHolopadCallerComponent> ent, ref MobStateChangedEvent args)
    {
        // Звонящий упал в крит или умер — он уже не может стоять у голопада
        if (args.NewMobState != MobState.Alive)
            EndCallOf(ent);
    }

    private void OnCallerShutdown(Entity<OneWayHolopadCallerComponent> ent, ref ComponentShutdown args)
    {
        // Штатный путь (EndSession) обнуляет ActiveUser до снятия компонента, и сюда мы попадаем только
        // когда звонящего удалили или компонент убрали «снаружи» — тогда закрываем и сам звонок
        if (!TryComp<OneWayHolopadComponent>(ent.Comp.Transmitter, out var oneWay) ||
            oneWay.ActiveUser != ent.Owner)
            return;

        EndCall(new Entity<OneWayHolopadComponent>(ent.Comp.Transmitter, oneWay));
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateTimer += frameTime;

        if (_updateTimer < UpdateInterval)
            return;

        _updateTimer -= UpdateInterval;

        var query = AllEntityQuery<OneWayHolopadComponent, TelephoneComponent>();
        while (query.MoveNext(out var uid, out var oneWay, out var telephone))
        {
            if (oneWay.ActiveUser is not { } user)
                continue;

            var ent = new Entity<OneWayHolopadComponent>(uid, oneWay);

            // Звонок уже закончился, а мы этого не заметили
            if (!_telephone.IsTelephoneEngaged((uid, telephone)))
            {
                EndSession(ent);
                continue;
            }

            // Звонящего удалили или унесли от передатчика, либо пропал приёмник
            if (TerminatingOrDeleted(user) ||
                oneWay.ActiveReceiver is not { } receiver ||
                TerminatingOrDeleted(receiver) ||
                !_xform.InRange((user, Transform(user)), (uid, Transform(uid)), telephone.ListeningRange))
            {
                EndCall(ent);
            }
        }
    }
}
