namespace NetPulseMonitor;

internal static class LteAutoLockPolicy
{
    private const double MinimumScoreImprovement = 5;

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
        LteCellRecommendation current,
        IReadOnlyCollection<LteCellRecommendation> recommendations)
    {
        if (!candidate.IsEligible)
            return false;
        if (!current.IsEligible)
            return true;
        double candidateScore = LteRecommendationScoring.CalculateScore(
            candidate,
            recommendations);
        double currentScore = LteRecommendationScoring.CalculateScore(
            current,
            recommendations);
        return candidateScore >= currentScore + MinimumScoreImprovement;
    }
}
