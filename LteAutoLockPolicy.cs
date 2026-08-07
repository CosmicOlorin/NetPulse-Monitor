namespace NetPulseMonitor;

internal static class LteAutoLockPolicy
{
    private const double ReliabilityMarginPerHour = 0.05;

    public static bool CanAttempt(AppSettings settings, DateTime nowUtc)
    {
        if (settings.AutomaticCellLockChangesToday >=
            settings.AutomaticCellLockMaxChangesPerDay)
            return false;
        return !settings.LastAutomaticCellLockUtc.HasValue ||
               nowUtc - settings.LastAutomaticCellLockUtc.Value.ToUniversalTime() >=
               TimeSpan.FromMinutes(settings.AutomaticCellLockMinimumDwellMinutes);
    }

    public static bool IsMeaningfullyBetter(
        LteCellRecommendation candidate,
        LteCellRecommendation current)
    {
        if (candidate.DisconnectionsPerHour + ReliabilityMarginPerHour <
            current.DisconnectionsPerHour)
            return true;
        if (current.DisconnectionsPerHour + ReliabilityMarginPerHour <
            candidate.DisconnectionsPerHour)
            return false;

        double candidateDown = candidate.AverageDownloadMbps ?? 0;
        double currentDown = current.AverageDownloadMbps ?? 0;
        if (candidateDown - currentDown >= 5 &&
            candidateDown >= currentDown * 1.15)
            return true;
        if (currentDown - candidateDown >= 5 &&
            currentDown >= candidateDown * 1.15)
            return false;

        double downloadTieMargin = Math.Max(3, currentDown * 0.05);
        if (Math.Abs(candidateDown - currentDown) > downloadTieMargin)
            return false;

        double candidateUp = candidate.AverageUploadMbps ?? 0;
        double currentUp = current.AverageUploadMbps ?? 0;
        return candidateUp - currentUp >= 2 &&
               candidateUp >= currentUp * 1.20;
    }
}
