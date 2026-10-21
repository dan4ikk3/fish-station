using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._Fish.DmArena;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Server._Fish.DmArena;

/// <summary>
/// Включает и выключает ДМ арену. Состояние хранится только на сервере, клиент его изменить не может.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class DmArenaSwitchCommand : LocalizedCommands
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    public override string Command => "dmarena";
    public override string Description => LocalizationManager.GetString("cmd-dmarena-desc");
    public override string Help => LocalizationManager.GetString("cmd-dmarena-help");

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_admin.HasAdminFlag(player, AdminFlags.Fun))
        {
            shell.WriteError(LocalizationManager.GetString("cmd-dmarena-no-access"));
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteError(LocalizationManager.GetString("shell-wrong-arguments-number"));
            return;
        }

        var current = _cfg.GetCVar(DmArenaConfigVars.DmArenaEnabled);
        var statusKey = current ? "cmd-dmarena-status-on" : "cmd-dmarena-status-off";

        if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            shell.WriteLine(LocalizationManager.GetString(statusKey));
            return;
        }

        bool? requested = args[0].ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => null,
        };

        if (requested is not { } value)
        {
            shell.WriteError(LocalizationManager.GetString("cmd-dmarena-bad-argument"));
            return;
        }

        if (value == current)
        {
            shell.WriteLine(LocalizationManager.GetString(statusKey));
            return;
        }

        _cfg.SetCVar(DmArenaConfigVars.DmArenaEnabled, value);

        var who = shell.Player?.Name ?? "server";
        _adminLog.Add(LogType.AdminCommands,
            LogImpact.High,
            $"{who} {(value ? "enabled" : "disabled")} the DM arena");

        shell.WriteLine(LocalizationManager.GetString(value ? "cmd-dmarena-enabled" : "cmd-dmarena-disabled"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                new[] { "on", "off", "status" },
                LocalizationManager.GetString("cmd-dmarena-hint"));
        }

        return CompletionResult.Empty;
    }
}
