namespace Content.Shared._Fish.CameraHearing;

/// <summary>
/// Сущность слышит речь рядом с собой и передаёт её ИИ и зрителям своей камеры.
/// </summary>
[RegisterComponent]
public sealed partial class FishCameraHearingComponent : Component
{
    [DataField]
    public float Range = 5f;

    [DataField]
    public float WhisperRange = 2f;

    /// <summary>
    /// Насколько вперёд от закреплённой камеры считается точкой слуха, чтобы стена не глушила звук.
    /// </summary>
    [DataField]
    public float FrontOffset = 0.75f;
}
