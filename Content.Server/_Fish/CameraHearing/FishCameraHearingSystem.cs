using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Shared._Fish.CameraHearing;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Mind.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.SurveillanceCamera.Components;
using Content.Shared.Wall;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Fish.CameraHearing;

/// <summary>
/// Речь рядом с улучшенной камерой или активным боргом слышат ИИ и зрители камеры, если между ними нет стены.
/// </summary>
public sealed partial class FishCameraHearingSystem : EntitySystem
{
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedChatSystem _chat = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;
    [Dependency] private SharedPowerReceiverSystem _powerReceiver = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ExamineSystemShared _examine = default!;

    private readonly HashSet<EntityUid> _listeners = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<BorgChassisComponent, ComponentInit>(OnBorgInit);
    }

    private void OnBorgInit(Entity<BorgChassisComponent> ent, ref ComponentInit args)
    {
        EnsureComp<FishCameraHearingComponent>(ent);
    }

    private void OnSpoke(EntitySpokeEvent ev)
    {
        if (!HasComp<MindContainerComponent>(ev.Source))
            return;

        var whisper = ev.ObfuscatedMessage != null;
        var sourceXform = Transform(ev.Source);
        var sourcePos = _transform.GetWorldPosition(sourceXform);
        var sourceCoords = _transform.GetMapCoordinates(sourceXform);
        var heard = false;

        _listeners.Clear();

        var query = EntityQueryEnumerator<FishCameraHearingComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var hearing, out var xform))
        {
            if (xform.MapID != sourceXform.MapID || !IsWorking(uid))
                continue;

            var range = whisper ? hearing.WhisperRange : hearing.Range;

            if ((_transform.GetWorldPosition(xform) - sourcePos).Length() > range ||
                IsBlockedByWall(uid, xform, sourceCoords, hearing.WallMountFrontOffset))
                continue;

            heard = true;

            if (TryComp<SurveillanceCameraComponent>(uid, out var camera))
                _listeners.UnionWith(camera.ActivePvsViewers);
        }

        if (!heard)
            return;

        var cores = EntityQueryEnumerator<StationAiCoreComponent>();
        while (cores.MoveNext(out var core, out var coreComp))
        {
            if (coreComp.Remote && _stationAi.TryGetHeld((core, coreComp), out var held))
                _listeners.Add(held.Value);
        }

        var directRange = whisper ? SharedChatSystem.WhisperMuffledRange : SharedChatSystem.VoiceRange;

        foreach (var listener in _listeners)
        {
            if (listener == ev.Source ||
                !TryComp<ActorComponent>(listener, out var actor) ||
                _transform.InRange((listener, Transform(listener)), (ev.Source, sourceXform), directRange))
                continue;

            Send(ev.Source, ev.Message, whisper, actor.PlayerSession);
        }
    }

    private bool IsWorking(EntityUid uid)
    {
        if (TryComp<BorgChassisComponent>(uid, out var borg) && !borg.Active)
            return false;

        return !TryComp<SurveillanceCameraComponent>(uid, out var camera) ||
               camera.Active && _powerReceiver.IsPowered(uid);
    }

    private bool IsBlockedByWall(EntityUid uid, TransformComponent xform, MapCoordinates source, float frontOffset)
    {
        var origin = _transform.GetMapCoordinates(xform);

        if (TryComp<WallMountComponent>(uid, out var mount))
        {
            var front = (_transform.GetWorldRotation(xform) + mount.Direction).RotateVec(new Vector2(0f, -1f));
            origin = origin.Offset(front * frontOffset);
        }

        return !_examine.InRangeUnOccluded(origin, source, 0f, null);
    }

    private void Send(EntityUid source, string message, bool whisper, ICommonSession session)
    {
        var nameEv = new TransformSpeakerNameEvent(source, MetaData(source).EntityName);
        RaiseLocalEvent(source, nameEv);

        var name = FormattedMessage.EscapeText(nameEv.VoiceName);
        var content = FormattedMessage.EscapeText(message);
        string wrapped;

        if (whisper)
        {
            wrapped = Loc.GetString("chat-manager-entity-whisper-wrap-message", ("entityName", name), ("message", content));
        }
        else
        {
            var speech = nameEv.SpeechVerb != null && ProtoMan.Resolve(nameEv.SpeechVerb, out var verbProto)
                ? verbProto
                : _chat.GetSpeechVerb(source, message);

            wrapped = Loc.GetString(speech.Bold ? "chat-manager-entity-say-bold-wrap-message" : "chat-manager-entity-say-wrap-message",
                ("entityName", name),
                ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
                ("fontType", speech.FontId),
                ("fontSize", speech.FontSize),
                ("message", content));
        }

        _chatManager.ChatMessageToOne(whisper ? ChatChannel.Whisper : ChatChannel.Local, message, wrapped, source, false, session.Channel);
    }
}
