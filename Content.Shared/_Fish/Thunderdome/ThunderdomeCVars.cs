using Robust.Shared.Configuration;

namespace Content.Shared._Fish.Thunderdome;

[CVarDefs]
public sealed class ThunderdomeCVars
{
    public static readonly CVarDef<bool> ThunderdomeEnabled =
        CVarDef.Create("thunderdome.enabled", true, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<bool> ThunderdomeRefill =
        CVarDef.Create("thunderdome.refill", true, CVar.SERVER | CVar.REPLICATED);
}
