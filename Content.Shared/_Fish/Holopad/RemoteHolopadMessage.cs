using Robust.Shared.Serialization;

namespace Content.Shared.Holopad;

/// <summary>
/// Принудительно завершить трансляцию. Обрабатывается только
/// на стороне трансмиттера у ресивера такого действия нет.
/// </summary>
[Serializable, NetSerializable]
public sealed class RemoteHolopadStopBroadcastMessage : BoundUserInterfaceMessage
{
}

/// <summary>
/// Войти в звонок увидеть окружение и заморозиться на время просмотра.
/// </summary>
[Serializable, NetSerializable]
public sealed class RemoteHolopadJoinViewMessage : BoundUserInterfaceMessage
{
}
