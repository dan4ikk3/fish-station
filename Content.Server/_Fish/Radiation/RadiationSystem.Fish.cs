using Content.Shared._Fish.Radiation;
using Robust.Shared.Player;

namespace Content.Server.Radiation.Systems;

public sealed partial class RadiationSystem
{
    [Dependency] private EntityQuery<FishRadiationExposureComponent> _fishExposureQuery = default!;

    private void UpdateFishExposure(EntityUid uid, float rads)
    {
        var level = MathF.Round(rads);

        if (!_fishExposureQuery.TryComp(uid, out var exposure))
        {
            // Помехи нужны только тем, кого видит игрок.
            if (level <= 0 || !HasComp<ActorComponent>(uid))
                return;

            exposure = EnsureComp<FishRadiationExposureComponent>(uid);
        }

        if (exposure.Level == level)
            return;

        exposure.Level = level;
        Dirty(uid, exposure);
    }
}
