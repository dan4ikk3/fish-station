using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared._Fish.CameraHearing;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Mind.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.SurveillanceCamera.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._Fish.CameraHearing;

/// <summary>
/// Речь рядом с улучшенной камерой или активным боргом слышат ИИ и зрители камеры, если между ними нет стены.
/// </summary>
public sealed partial class FishCameraHearingSystem : EntitySystem
{
    [Dependency] private SharedStationAiSystem _stationAi = default!;
    [Dependency] private SharedPowerReceiverSystem _powerReceiver = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ExamineSystemShared _examine = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandRecipients);
        SubscribeLocalEvent<BorgChassisComponent, ComponentInit>(OnBorgInit);
    }

    private void OnBorgInit(Entity<BorgChassisComponent> ent, ref ComponentInit args)
    {
        EnsureComp<FishCameraHearingComponent>(ent);
    }

    private void OnExpandRecipients(ExpandICChatRecipientsEvent ev)
    {
        if (!HasComp<MindContainerComponent>(ev.Source))
            return;

        var whisper = ev.VoiceRange <= SharedChatSystem.WhisperMuffledRange;
        var sourceXform = Transform(ev.Source);
        var sourcePos = _transform.GetWorldPosition(sourceXform);
        var sourceCoords = _transform.GetMapCoordinates(sourceXform);
        var viewers = new List<(EntityUid Viewer, float Range)>();
        float? nearest = null;

        var query = EntityQueryEnumerator<FishCameraHearingComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var hearing, out var xform))
        {
            if (xform.MapID != sourceXform.MapID || !IsWorking(uid))
                continue;

            var distance = (_transform.GetWorldPosition(xform) - sourcePos).Length();

            if (distance > (whisper ? hearing.WhisperRange : hearing.Range) ||
                IsBlockedByWall(xform, sourceCoords, hearing.FrontOffset))
                continue;

            nearest = nearest == null ? distance : Math.Min(nearest.Value, distance);

            if (!TryComp<SurveillanceCameraComponent>(uid, out var camera))
                continue;

            foreach (var viewer in camera.ActivePvsViewers)
            {
                viewers.Add((viewer, distance));
            }
        }

        var cores = EntityQueryEnumerator<StationAiCoreComponent>();
        while (cores.MoveNext(out var core, out var coreComp))
        {
            if (!coreComp.Remote ||
                !_stationAi.TryGetHeld((core, coreComp), out var held) ||
                !TryComp<ActorComponent>(held, out var actor) ||
                held == ev.Source)
                continue;

            var session = actor.PlayerSession;

            // Глаз ИИ слышит речь в радиусе, но не сквозь стены.
            if (session.AttachedEntity is { } eye &&
                ev.Recipients.ContainsKey(session) &&
                !_examine.InRangeUnOccluded(eye, sourceCoords, 0f))
                ev.Recipients.Remove(session);

            // Через камеру ИИ видит речь только облачком, шёпот приходит в чат.
            if (nearest != null)
                ev.Recipients.TryAdd(session, new ChatSystem.ICChatRecipientData(nearest.Value, false, !whisper));
        }

        foreach (var (viewer, range) in viewers)
        {
            if (TryComp<ActorComponent>(viewer, out var actor))
                ev.Recipients.TryAdd(actor.PlayerSession, new ChatSystem.ICChatRecipientData(range, false));
        }
    }

    private bool IsWorking(EntityUid uid)
    {
        if (TryComp<BorgChassisComponent>(uid, out var borg) && !borg.Active)
            return false;

        return !TryComp<SurveillanceCameraComponent>(uid, out var camera) ||
               camera.Active && _powerReceiver.IsPowered(uid);
    }

    private bool IsBlockedByWall(TransformComponent xform, MapCoordinates source, float frontOffset)
    {
        var origin = _transform.GetMapCoordinates(xform);

        // Настенная камера стоит внутри стены, поэтому слух считается от точки перед ней.
        if (xform.Anchored)
        {
            var front = _transform.GetWorldRotation(xform).RotateVec(new Vector2(0f, -1f));
            origin = origin.Offset(front * frontOffset);
        }

        return !_examine.InRangeUnOccluded(origin, source, 0f, null);
    }
}
