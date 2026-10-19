using Content.Shared._Fish.DmArena;
using Content.Server.EUI;
using Content.Shared.Eui;
using Robust.Shared.Player;

namespace Content.Server._Fish.DmArena;

public sealed class DmArenaLoadoutEui : BaseEui
{
    private readonly DmArenaRuleSystem _dmArenaSystem;
    private readonly IEntityManager _entManager;
    private readonly EntityUid _ruleEntity;
    private readonly ICommonSession _session;

    public DmArenaLoadoutEui(DmArenaRuleSystem dmArenaSystem, EntityUid ruleEntity, ICommonSession session)
    {
        _dmArenaSystem = dmArenaSystem;
        _entManager = IoCManager.Resolve<IEntityManager>();
        _ruleEntity = ruleEntity;
        _session = session;
    }

    public override EuiStateBase GetNewState()
    {
        if (!_entManager.TryGetComponent<DmArenaRuleComponent>(_ruleEntity, out var rule))
            return new DmArenaLoadoutEuiState(new List<DmArenaShopOption>(), new List<DmArenaSetOption>(), 0, 0, null, string.Empty);

        return _dmArenaSystem.GetLoadoutState(rule, _session.UserId);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case DmArenaLoadoutSelectedMessage selected:
                _dmArenaSystem.SpawnPlayer(_session, _ruleEntity, selected.Items);
                Close();
                break;
            case DmArenaPresetSaveMessage preset:
                if (_entManager.TryGetComponent<DmArenaRuleComponent>(_ruleEntity, out var rule))
                    _dmArenaSystem.SavePreset(rule, _session.UserId, preset.Items);
                StateDirty();
                break;
        }
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        _dmArenaSystem.OnEuiClosed(_session, this);
    }
}
