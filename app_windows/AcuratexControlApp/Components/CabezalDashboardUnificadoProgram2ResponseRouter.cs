namespace AcuratexControlApp.Components;

public readonly record struct Program2PendingOperation(string Command, long Sequence);

public static class CabezalDashboardUnificadoProgram2ResponseRouter
{
    public static string? SelectFailedCommand(
        string rawLine,
        IEnumerable<Program2PendingOperation> pendingOperations)
    {
        string line = (rawLine ?? string.Empty).Trim();
        if (!line.StartsWith("ERR|UNI|", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Program2PendingOperation[] pending = pendingOperations
            .OrderBy(operation => operation.Sequence)
            .ToArray();
        if (pending.Length == 0)
        {
            return null;
        }

        string? explicitCommand = line
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(token => token.StartsWith("COMMAND=", StringComparison.OrdinalIgnoreCase));
        if (explicitCommand is not null)
        {
            string command = explicitCommand[8..];
            if (!command.StartsWith("uni_", StringComparison.OrdinalIgnoreCase))
            {
                command = $"uni_{command}";
            }
            return pending.FirstOrDefault(operation =>
                operation.Command.Equals(command, StringComparison.OrdinalIgnoreCase)).Command;
        }

        static bool Contains(string value, string token) =>
            value.Contains(token, StringComparison.OrdinalIgnoreCase);
        static bool StartsWithAny(string command, params string[] prefixes) =>
            prefixes.Any(prefix => command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        Func<string, bool>? family = null;
        if (Contains(line, "|STOP_TX"))
            family = command => StartsWithAny(command, "uni_stop", "uni_emergency_stop");
        else if (Contains(line, "|RUN_ALL"))
            family = command => command.Equals("uni_run_all", StringComparison.OrdinalIgnoreCase);
        else if (Contains(line, "|ALL_J_TX") || Contains(line, "|ALL_YARN_TX"))
            family = command => StartsWithAny(command, "uni_off_all", "uni_on_all");
        else if (Contains(line, "|SEQUENCE"))
            family = command => command.StartsWith("uni_sequence_", StringComparison.OrdinalIgnoreCase);
        else if (Contains(line, "|DEN_"))
            family = command => command.StartsWith("uni_den_", StringComparison.OrdinalIgnoreCase);
        else if (Contains(line, "|SIC_"))
            family = command => command.StartsWith("uni_sic_", StringComparison.OrdinalIgnoreCase);
        else if (Contains(line, "|FEET_"))
            family = command => command.StartsWith("uni_feet_", StringComparison.OrdinalIgnoreCase);
        else if (Contains(line, "|STITCH_") || Contains(line, "|S_RUN") || Contains(line, "|S_STOP"))
            family = command => StartsWithAny(command, "uni_stitch_", "uni_s_");
        else if (Contains(line, "|YARN_") || Contains(line, "|Y_RUN") || Contains(line, "|Y_STOP")
                 || Contains(line, "|Y1_") || Contains(line, "|Y2_"))
            family = command => StartsWithAny(command, "uni_yarn_", "uni_y1_", "uni_y2_", "uni_y_");
        else if (Contains(line, "|J_"))
            family = command => command.StartsWith("uni_j_", StringComparison.OrdinalIgnoreCase);
        else if (Contains(line, "|POSITION"))
            family = command => Contains(command, "_pos_") || Contains(command, "_select_");
        else if (Contains(line, "|BLOCK_PIN"))
            family = command => Contains(command, "_pin_");

        Program2PendingOperation? selected = family is null
            ? pending[0]
            : pending.Cast<Program2PendingOperation?>()
                .FirstOrDefault(operation => operation.HasValue && family(operation.Value.Command));
        return selected?.Command;
    }
}
