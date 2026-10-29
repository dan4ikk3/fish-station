using Content.Server.Hands.Systems;
// Fish-start: сброс задержки атаки молота при попадании
using Content.Shared.Timing.Systems;
using Content.Shared.Weapons.Melee.Events;
// Fish-end

namespace Content.Server._Sunrise.BloodCult.Juggernaut;

public sealed partial class JuggernautSystem : EntitySystem
{
    [Dependency] private HandsSystem _hands = default!;
    // Fish-start: зависимость для отката задержки молота
    [Dependency] private UseDelaySystem _useDelay = default!;
    // Fish-end

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JuggernautComponent, MapInitEvent>(OnMapInit, after: [typeof(HandsSystem)]);
        // Fish-start: отдельный маркер — нельзя второй раз подписаться на MeleeThrowOnHitComponent + MeleeHitEvent
        SubscribeLocalEvent<JuggernautHammerComponent, MeleeHitEvent>(OnHammerMeleeHit);
        // Fish-end
    }

    private void OnMapInit(Entity<JuggernautComponent> ent, ref MapInitEvent args)
    {
        var hammer = Spawn(ent.Comp.HummerSpawnId, Transform(ent).Coordinates);
        // Fish-start: выдача компонента молота джаггернаута
        EnsureComp<JuggernautHammerComponent>(hammer);
        // Fish-end
        _hands.TryForcePickupAnyHand(ent, hammer);
    }

    // Fish-start: сброс задержки атаки молота при успешном ударе владельцем-джаггернаутом
    private void OnHammerMeleeHit(EntityUid weapon, JuggernautHammerComponent component, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        if (!HasComp<JuggernautComponent>(args.User))
            return;

        _useDelay.TryResetDelay(weapon);
    }
    // Fish-end
}
