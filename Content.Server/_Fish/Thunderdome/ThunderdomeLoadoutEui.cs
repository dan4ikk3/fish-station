using Content.Shared._Fish.Thunderdome;
using Content.Server.EUI;
using Content.Shared.Eui;
using Robust.Shared.Player;

namespace Content.Server._Fish.Thunderdome;

public sealed class ThunderdomeLoadoutEui : BaseEui
{
    private readonly ThunderdomeRuleSystem _thunderdomeSystem;
    private readonly IEntityManager _entManager;
    private readonly EntityUid _ruleEntity;
    private readonly ICommonSession _session;

    public ThunderdomeLoadoutEui(ThunderdomeRuleSystem thunderdomeSystem, EntityUid ruleEntity, ICommonSession session)
    {
        _thunderdomeSystem = thunderdomeSystem;
        _entManager = IoCManager.Resolve<IEntityManager>();
        _ruleEntity = ruleEntity;
        _session = session;
    }

    public override EuiStateBase GetNewState()
    {
        if (!_entManager.TryGetComponent<ThunderdomeRuleComponent>(_ruleEntity, out var rule))
            return new ThunderdomeLoadoutEuiState(new List<ThunderdomeShopOption>(), 0, 0, null, string.Empty);

        return _thunderdomeSystem.GetLoadoutState(rule, _session.UserId);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case ThunderdomeLoadoutSelectedMessage selected:
                _thunderdomeSystem.SpawnPlayer(_session, _ruleEntity, selected.Items);
                Close();
                break;
            case ThunderdomePresetSaveMessage preset:
                if (_entManager.TryGetComponent<ThunderdomeRuleComponent>(_ruleEntity, out var rule))
                    _thunderdomeSystem.SavePreset(rule, _session.UserId, preset.Items);
                StateDirty();
                break;
        }
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }
}
