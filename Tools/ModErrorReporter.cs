using Serilog;

namespace DeadCellsMultiplayerMod.Tools
{
    internal static class ModErrorReporter
    {
        public static void SafeLogException(ILogger? logger, Exception ex, string context)
        {
            if (logger != null)
                logger.Warning(ex, "[DCCM] {Context}", context);
        }

        public static void ReportBootstrapError(string context, Exception ex)
        {
            if (ModEntry.Instance?.Logger != null)
                ModEntry.Instance.Logger.Error(ex, "[DCCM] Bootstrap error: {Context}", context);
        }

        public static void ReportSyncError(string context, string details)
        {
            if (ModEntry.Instance?.Logger != null)
                ModEntry.Instance.Logger.Warning("[DCCM] Sync error: {Context} - {Details}", context, details);
        }
    }
}
