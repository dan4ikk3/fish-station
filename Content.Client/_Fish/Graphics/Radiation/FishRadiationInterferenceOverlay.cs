using Content.Shared._Fish.Radiation;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Fish.Graphics.Radiation;

/// <summary>
/// Помехи и затемнение экрана, растущие вместе с облучением локального игрока.
/// </summary>
public sealed partial class FishRadiationInterferenceOverlay : Overlay
{
    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "FishRadiationInterference";

    private const float StartRads = 5f; // Ниже этого уровня помех нет.
    private const float FullRads = 15f; // С этого уровня помехи максимальны, источники в игре редко дают больше.
    private const float FadeSpeed = 0.3f; // Изменение силы эффекта в секунду, чтобы частицы нарастали плавно.

    private readonly ShaderInstance _shader;
    private float _strength;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public FishRadiationInterferenceOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototype.Index(Shader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var target = 0f;
        if (_player.LocalEntity is { } player && _entity.TryGetComponent(player, out FishRadiationExposureComponent? exposure))
            target = Math.Clamp((exposure.Level - StartRads) / (FullRads - StartRads), 0f, 1f);

        var step = FadeSpeed * (float) _timing.FrameTime.TotalSeconds;
        _strength += Math.Clamp(target - _strength, -step, step);

        return _strength > 0f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var reduced = _configuration.GetCVar(CCVars.ReducedMotion);
        var time = (float) _timing.RealTime.TotalSeconds;

        _shader.SetParameter("strength", _strength);
        _shader.SetParameter("frame", reduced ? 0f : MathF.Floor(time * 12f) % 256f);
        _shader.SetParameter("patchFrame", reduced ? 0f : MathF.Floor(time * 3f) % 256f);

        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldBounds, Color.White);
        args.WorldHandle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _shader.Dispose();
        base.DisposeBehavior();
    }
}
