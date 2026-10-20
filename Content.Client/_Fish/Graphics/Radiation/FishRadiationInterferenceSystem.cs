using Robust.Client.Graphics;

namespace Content.Client._Fish.Graphics.Radiation;

public sealed partial class FishRadiationInterferenceSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    private FishRadiationInterferenceOverlay _interference = default!;

    public override void Initialize()
    {
        base.Initialize();
        _interference = new();
        _overlay.AddOverlay(_interference);
    }

    public override void Shutdown()
    {
        _overlay.RemoveOverlay(_interference);
        _interference.Dispose();
        base.Shutdown();
    }
}
