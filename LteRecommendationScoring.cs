namespace NetPulseMonitor;

internal static class LteRecommendationScoring
{
    public const double SinrWeight = 0.50;
    public const double RsrqWeight = 0.35;
    public const double RsrpWeight = 0.15;

    public static void AssignScores(
        IReadOnlyCollection<LteCellRecommendation> recommendations)
    {
        LteCellRecommendation[] eligible = recommendations
            .Where(item => item.IsEligible)
            .ToArray();
        if (eligible.Length == 0)
            return;

        foreach (LteCellRecommendation item in eligible)
            item.WeightedScore = CalculateRadioScore(item);
    }

    public static double CalculateScore(
        LteCellRecommendation target,
        IReadOnlyCollection<LteCellRecommendation> recommendations)
    {
        LteCellRecommendation[] eligible = recommendations
            .Where(item => item.IsEligible)
            .ToArray();
        if (!target.IsEligible || eligible.Length == 0)
            return 0;

        return HasRadioEvidence(target) ? CalculateRadioScore(target) : 0;
    }

    public static double CalculateRadioScore(LteCellRecommendation item) =>
        ScoreSinr(item.AverageSinrDb) * SinrWeight +
        ScoreRsrq(item.AverageRsrqDb) * RsrqWeight +
        ScoreRsrp(item.AverageRsrpDbm) * RsrpWeight;

    public static double ScoreSinr(double? value) => value switch
    {
        null => 0, >= 15 => Scale(value.Value, 15, 25, 90, 100),
        >= 7 => Scale(value.Value, 7, 14, 60, 85),
        >= 0 => Scale(value.Value, 0, 6, 30, 55),
        _ => Scale(value.Value, -20, 0, 0, 25)
    };

    public static double ScoreRsrq(double? value) => value switch
    {
        null => 0, >= -8 => Scale(value.Value, -8, -3, 90, 100),
        >= -12 => Scale(value.Value, -12, -9, 65, 85),
        >= -15 => Scale(value.Value, -15, -13, 40, 60),
        _ => Scale(value.Value, -25, -16, 0, 35)
    };

    public static double ScoreRsrp(double? value) => value switch
    {
        null => 0, >= -85 => Scale(value.Value, -85, -65, 95, 100),
        >= -95 => Scale(value.Value, -95, -86, 70, 90),
        >= -105 => Scale(value.Value, -105, -96, 40, 65),
        _ => Scale(value.Value, -125, -106, 0, 35)
    };

    private static double Scale(double value, double low, double high,
        double lowScore, double highScore) => lowScore +
        Math.Clamp((value - low) / (high - low), 0, 1) *
        (highScore - lowScore);

    public static bool HasRadioEvidence(LteCellRecommendation item) =>
        item.AverageSinrDb.HasValue && item.AverageRsrqDb.HasValue &&
        item.AverageRsrpDbm.HasValue;
}
