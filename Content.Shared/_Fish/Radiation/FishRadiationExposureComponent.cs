using Robust.Shared.GameStates;

namespace Content.Shared._Fish.Radiation;

/// <summary>
/// Текущий уровень облучения игрока для клиентских помех на экране.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FishRadiationExposureComponent : Component
{
    /// <summary>Облучение в рад/с, округлённое до целых, чтобы не рассылать лишние обновления.</summary>
    [DataField, AutoNetworkedField]
    public float Level;
}
