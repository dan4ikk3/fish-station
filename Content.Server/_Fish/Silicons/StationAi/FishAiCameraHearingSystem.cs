using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared.Mind.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Silicons.StationAi;
using Content.Shared.SurveillanceCamera.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Fish.Silicons.StationAi;

/// <summary>
/// ИИ в режиме полёта слышит речь в радиусе от любой работающей камеры.
/// </summary>
public sealed partial class FishAiCameraHearingSystem : EntitySystem
{
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedChatSystem _chat = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedTransformSystem _xforms = default!;

    private const float HearRange = 5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(EntitySpokeEvent ev)
    {
        if (!HasComp<MindContainerComponent>(ev.Source))
            return;

        var whisper = ev.ObfuscatedMessage != null;
        var sourceXform = Transform(ev.Source);
        var distance = GetDistanceToNearestCamera(sourceXform);

        if (distance == null || whisper && distance > SharedChatSystem.WhisperMuffledRange)
            return;

        var message = whisper && distance > SharedChatSystem.WhisperClearRange
            ? ev.ObfuscatedMessage!
            : ev.Message;

        var directRange = whisper ? SharedChatSystem.WhisperMuffledRange : SharedChatSystem.VoiceRange;
        var query = EntityQueryEnumerator<StationAiCoreComponent>();
        while (query.MoveNext(out var core, out var coreComp))
        {
            if (!coreComp.Remote ||
                !_stationAi.TryGetHeld((core, coreComp), out var held) ||
                !TryComp<ActorComponent>(held, out var actor) ||
                held == ev.Source)
                continue;

            // Рядом с самим ИИ речь и так доходит обычным путём
            if (_xforms.InRange((held.Value, Transform(held.Value)), (ev.Source, sourceXform), directRange))
                continue;

            SendToAi(ev.Source, message, actor.PlayerSession);
        }
    }

    private float? GetDistanceToNearestCamera(TransformComponent sourceXform)
    {
        var sourcePos = _xforms.GetWorldPosition(sourceXform);
        float? nearest = null;

        var query = EntityQueryEnumerator<SurveillanceCameraComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var camera, out var xform))
        {
            if (!camera.Active || xform.MapID != sourceXform.MapID || !_power.IsPowered(uid))
                continue;

            var distance = (_xforms.GetWorldPosition(xform) - sourcePos).Length();

            if (distance <= HearRange && (nearest == null || distance < nearest))
                nearest = distance;
        }

        return nearest;
    }

    private void SendToAi(EntityUid source, string message, ICommonSession session)
    {
        var nameEv = new TransformSpeakerNameEvent(source, MetaData(source).EntityName);
        RaiseLocalEvent(source, nameEv);

        var speech = nameEv.SpeechVerb != null && ProtoMan.Resolve(nameEv.SpeechVerb, out var verbProto)
            ? verbProto
            : _chat.GetSpeechVerb(source, message);

        var wrapped = Loc.GetString(speech.Bold ? "chat-telephone-message-wrap-bold" : "chat-telephone-message-wrap",
            ("color", Color.White),
            ("fontType", speech.FontId),
            ("fontSize", speech.FontSize),
            ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
            ("name", FormattedMessage.EscapeText(nameEv.VoiceName)),
            ("message", FormattedMessage.EscapeText(message)));

        _chatManager.ChatMessageToOne(ChatChannel.Local, message, wrapped, source, false, session.Channel);
    }
}
