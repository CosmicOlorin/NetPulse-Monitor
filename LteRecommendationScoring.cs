namespace NetPulseMonitor;

internal static class LteRecommendationScoring
{
    public const double DisconnectionWeight = 0.40;
    public const double DownloadWeight = 0.50;
    public const double UploadWeight = 0.10;

    public static void AssignScores(
        IReadOnlyCollection<LteCellRecommendation> recommendations)
    {
        LteCellRecommendation[] eligible = recommendations
            .Where(item => item.IsEligible)
            .ToArray();
        if (eligible.Length == 0)
            return;

        double maximumDownload = eligible.Max(item => item.AverageDownloadMbps ?? 0);
        double maximumUpload = eligible.Max(item => item.AverageUploadMbps ?? 0);

        foreach (LteCellRecommendation item in eligible)
        {
            double reliability = CalculateReliability(item.DisconnectionsPerHour);
            double download = NormalizeSpeed(
                item.AverageDownloadMbps ?? 0,
                maximumDownload);
            double upload = NormalizeSpeed(
                item.AverageUploadMbps ?? 0,
                maximumUpload);
            item.WeightedScore =
                reliability * DisconnectionWeight +
                download * DownloadWeight +
                upload * UploadWeight;
        }
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

        double maximumDownload = eligible.Max(item => item.AverageDownloadMbps ?? 0);
        double maximumUpload = eligible.Max(item => item.AverageUploadMbps ?? 0);
        return
            CalculateReliability(target.DisconnectionsPerHour) * DisconnectionWeight +
            NormalizeSpeed(
                target.AverageDownloadMbps ?? 0,
                maximumDownload) * DownloadWeight +
            NormalizeSpeed(
                target.AverageUploadMbps ?? 0,
                maximumUpload) * UploadWeight;
    }

    private static double NormalizeSpeed(double value, double maximum) =>
        maximum <= double.Epsilon ? 100 : Math.Clamp(value / maximum * 100, 0, 100);

    private static double CalculateReliability(double disconnectionsPerHour) =>
        100 / (1 + Math.Max(0, disconnectionsPerHour));
}
