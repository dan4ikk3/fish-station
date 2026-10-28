using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Holopad;
using Content.Shared.Speech.Components;
using Content.Shared.Telephone;
using Robust.Server.Player;

namespace Content.Server._Fish.Holopad;

/// <summary>
/// Передаёт LOOC собеседникам на другом конце звонка по голопаду.
/// </summary>
public sealed partial class FishHolopadLoocSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedTransformSystem _xforms = default!;

    // LOOC отправляется сразу после попытки, поэтому метка живёт один тик
    private readonly HashSet<EntityUid> _loocSources = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InGameOocMessageAttemptEvent>(OnOocAttempt);
        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandRecipients);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _loocSources.Clear();
    }

    private void OnOocAttempt(ref InGameOocMessageAttemptEvent ev)
    {
        if (ev.Type == InGameOOCChatType.Looc && ev.Session.AttachedEntity is { } source)
            _loocSources.Add(source);
    }

    private void OnExpandRecipients(ExpandICChatRecipientsEvent ev)
    {
        if (!_loocSources.Remove(ev.Source))
            return;

        var sourceXform = Transform(ev.Source);
        var query = EntityQueryEnumerator<HolopadComponent, TelephoneComponent, ActiveListenerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var telephone, out var listener, out var xform))
        {
            if (telephone.Muted || !_xforms.InRange((ev.Source, sourceXform), (uid, xform), listener.Range))
                continue;

            foreach (var linked in telephone.LinkedTelephones)
            {
                if (HasComp<HolopadComponent>(linked))
                    AddRecipientsNear(ev, linked);
            }
        }
    }

    private void AddRecipientsNear(ExpandICChatRecipientsEvent ev, EntityUid holopad)
    {
        var holopadXform = Transform(holopad);

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player)
                continue;

            var playerXform = Transform(player);

            if (playerXform.MapID != holopadXform.MapID)
                continue;

            var distance = (_xforms.GetWorldPosition(playerXform) - _xforms.GetWorldPosition(holopadXform)).Length();

            if (distance <= ev.VoiceRange)
                ev.Recipients.TryAdd(session, new ChatSystem.ICChatRecipientData(distance, false));
        }
    }
}
