using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared.Mind.Components;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Fish.Silicons.StationAi;

/// <summary>
/// ИИ в режиме полёта слышит речь рядом со своим глазом, если это место видят камеры.
/// </summary>
public sealed partial class FishAiCameraHearingSystem : EntitySystem
{
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedChatSystem _chat = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;
    [Dependency] private StationAiVisionSystem _vision = default!;
    [Dependency] private SharedTransformSystem _xforms = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private EntityQuery<BroadphaseComponent> _broadphaseQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;

    private const float HearRange = 6f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(EntitySpokeEvent ev)
    {
        if (!HasComp<MindContainerComponent>(ev.Source))
            return;

        var query = EntityQueryEnumerator<StationAiCoreComponent>();
        while (query.MoveNext(out var core, out var coreComp))
        {
            if (!coreComp.Remote || coreComp.RemoteEntity is not { } eye)
                continue;

            if (!_stationAi.TryGetHeld((core, coreComp), out var held) ||
                !TryComp<ActorComponent>(held, out var actor))
                continue;

            if (held == ev.Source)
                continue;

            TryHear(ev, eye, held.Value, actor.PlayerSession);
        }
    }

    private void TryHear(EntitySpokeEvent ev, EntityUid eye, EntityUid held, ICommonSession session)
    {
        var whisper = ev.ObfuscatedMessage != null;

        // Рядом с самим ИИ речь и так доходит обычным путём
        var directRange = whisper ? SharedChatSystem.WhisperMuffledRange : SharedChatSystem.VoiceRange;
        if (_xforms.InRange((held, Transform(held)), (ev.Source, Transform(ev.Source)), directRange))
            return;

        var sourceXform = Transform(ev.Source);
        var eyeXform = Transform(eye);

        if (sourceXform.MapID != eyeXform.MapID)
            return;

        var distance = (_xforms.GetWorldPosition(sourceXform) - _xforms.GetWorldPosition(eyeXform)).Length();
        var range = whisper ? Math.Min(HearRange, SharedChatSystem.WhisperMuffledRange) : HearRange;

        if (distance > range || !IsSeenByCameras(ev.Source, sourceXform))
            return;

        var message = whisper && distance > SharedChatSystem.WhisperClearRange
            ? ev.ObfuscatedMessage!
            : ev.Message;

        SendToAi(ev.Source, message, session);
    }

    private bool IsSeenByCameras(EntityUid source, TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid ||
            !_broadphaseQuery.TryComp(gridUid, out var broadphase) ||
            !_gridQuery.TryComp(gridUid, out var grid))
            return false;

        var tile = _maps.LocalToTile(gridUid, grid, xform.Coordinates);
        return _vision.IsAccessible((gridUid, broadphase, grid), tile);
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
