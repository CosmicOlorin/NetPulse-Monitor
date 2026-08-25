namespace NetPulseMonitor;

internal static class LteRecommendationScoring
{
    public const double SinrWeight = 0.50;
    public const double RsrqWeight = 0.35;
    public const double RsrpWeight = 0.15;
    public const double RankReliabilityWeight = 0.50;
    public const double RankDownloadWeight = 0.25;
    public const double RankUploadWeight = 0.25;

    public static void AssignScores(
        IReadOnlyCollection<LteCellRecommendation> recommendations)
    {
        double maximumDownload = recommendations
            .Where(item => item.PeriodSpeedTests > 0)
            .Select(item => item.AverageDownloadMbps ?? 0)
            .DefaultIfEmpty(0)
            .Max();
        double maximumUpload = recommendations
            .Where(item => item.PeriodSpeedTests > 0)
            .Select(item => item.AverageUploadMbps ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        foreach (LteCellRecommendation item in recommendations)
        {
            bool radioAvailable =
                (item.PeriodHasRadioEvidence || item.IsEligible) &&
                HasRadioEvidence(item);
            item.RadioScore = radioAvailable ? CalculateRadioScore(item) : 0;
            bool reliabilityAvailable = item.PeriodReliabilityScore.HasValue;
            bool downloadAvailable = item.PeriodSpeedTests > 0 &&
                                     item.AverageDownloadMbps.HasValue;
            bool uploadAvailable = item.PeriodSpeedTests > 0 &&
                                   item.AverageUploadMbps.HasValue;
            double reliabilityScore = item.PeriodReliabilityScore ?? 0;
            double downloadScore = downloadAvailable && maximumDownload > 0
                ? 100D * item.AverageDownloadMbps!.Value / maximumDownload
                : 0;
            double uploadScore = uploadAvailable && maximumUpload > 0
                ? 100D * item.AverageUploadMbps!.Value / maximumUpload
                : 0;

            // Rank deliberately excludes RF: RF remains a separate diagnostic
            // score. The three Rank weights always remain fixed. Missing evidence is a
            // zero component; it is never hidden by re-normalizing the remaining
            // weights to 100%.
            item.WeightedScore =
                reliabilityScore * RankReliabilityWeight +
                downloadScore * RankDownloadWeight +
                uploadScore * RankUploadWeight;
            item.HasRankingEvidence = reliabilityAvailable || downloadAvailable ||
                                      uploadAvailable;
        }
    }

    public static double CalculateScore(
        LteCellRecommendation target,
        IReadOnlyCollection<LteCellRecommendation> recommendations)
    {
        AssignScores(recommendations);
        return target.HasRankingEvidence ? target.WeightedScore : 0;
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
