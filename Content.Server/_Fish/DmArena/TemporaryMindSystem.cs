using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Shared._Fish.DmArena;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Fish.DmArena;

/// <summary>
/// Временная подмена разума: настоящий разум игрока откладывается, а в тело арены идёт одноразовый.
/// После выхода одноразовый разум удаляется, и игрок возвращается к своему прежнему разуму.
/// </summary>
public sealed class TemporaryMindSystem : EntitySystem
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

        SubscribeLocalEvent<TemporaryMindComponent, PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerDetached(Entity<TemporaryMindComponent> ent, ref PlayerDetachedEvent args)
    {
        if (args.Player.Status != SessionStatus.Disconnected)
            return;

        CleanupDisposableMind(ent, ent.Comp);
        RemComp<TemporaryMindComponent>(ent);
    }

    public bool TrySwapTempMind(ICommonSession session, EntityUid newBody)
    {
        if (session.AttachedEntity is not { Valid: true } currentEntity)
            return false;

        if (!_mind.TryGetMind(currentEntity, out var origMindId, out var origMind))
            return false;

        var userId = origMind.UserId;
        _mind.UnVisit(origMindId, origMind);

        // Освобождаем userId для одноразового разума
        _mind.SetUserId(origMindId, null, origMind);

        var newMind = _mind.CreateMind(userId, origMind.CharacterName);
        _mind.TransferTo(newMind, newBody);
        _playerManager.SetAttachedEntity(session, newBody);

        var comp = EnsureComp<TemporaryMindComponent>(newBody);
        comp.OriginalMind = origMindId;
        comp.DisposableMind = newMind;

        return true;
    }

    public EntityUid? TryRestoreAsGhost(EntityUid temporaryBody)
    {
        if (!TryComp<TemporaryMindComponent>(temporaryBody, out var temp)
            || !TryComp<MindComponent>(temp.OriginalMind, out var origMind))
            return null;

        var coords = _transform.GetMapCoordinates(temporaryBody);

        CleanupDisposableMind(temporaryBody, temp);

        var ghost = Spawn("MobObserver", coords);
        _mind.Visit(temp.OriginalMind, ghost, origMind);

        if (TryComp<GhostComponent>(ghost, out var ghostComp))
            _ghost.SetCanReturnToBody((ghost, ghostComp), origMind.OwnedEntity is { } owned && Exists(owned));

        if (!string.IsNullOrWhiteSpace(origMind.CharacterName))
            _meta.SetEntityName(ghost, FormattedMessage.EscapeText(origMind.CharacterName));
        else if (origMind.UserId is { } userId && _playerManager.TryGetSessionById(userId, out var session))
            _meta.SetEntityName(ghost, FormattedMessage.EscapeText(session.Name));

        RemComp<TemporaryMindComponent>(temporaryBody);
        return ghost;
    }

    public bool TryRestoreToOriginalBody(EntityUid temporaryBody)
    {
        if (!TryComp<TemporaryMindComponent>(temporaryBody, out var temp)
            || !TryComp<MindComponent>(temp.OriginalMind, out var origMind)
            || origMind.OwnedEntity is not { } originalBody
            || !Exists(originalBody))
            return false;

        CleanupDisposableMind(temporaryBody, temp);

        if (origMind.UserId is { } userId && _playerManager.TryGetSessionById(userId, out var session))
            _playerManager.SetAttachedEntity(session, originalBody);

        RemComp<TemporaryMindComponent>(temporaryBody);
        return true;
    }

    private void CleanupDisposableMind(EntityUid temporaryBody, TemporaryMindComponent temp)
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
