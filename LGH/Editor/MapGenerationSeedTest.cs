#if UNITY_EDITOR
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MapGenerationSeedTest
{
    [MenuItem("PaintWar/Tests/Run 10,000 Symmetric Map Seeds")]
    public static void RunTenThousandFromMenu()
    {
        UnityEngine.Debug.Log(Run(10000));
    }

    public static string Run(int seedCount)
    {
        return RunRange(1, seedCount);
    }

    public static string RunRange(int firstSeed, int seedCount)
    {
        seedCount = Mathf.Max(1, seedCount);
        MapGenerationConfig config = MapGenerationConfig.CreateDefault();
        SymmetricMapGenerator generator = new SymmetricMapGenerator();
        Stopwatch stopwatch = Stopwatch.StartNew();
        int invalid = 0;
        int fallback = 0;
        int totalAttempts = 0;
        int maxAttempts = 0;
        int maxCenterVariance = 0;
        int maxNearestItemGap = 0;
        string firstFailure = string.Empty;

        int endExclusive = firstSeed + seedCount;
        for (int seed = firstSeed; seed < endExclusive; seed++)
        {
            MapGenerationLayout layout = generator.Generate(seed, config);
            MapGenerationReport report = layout.Report;
            totalAttempts += report.Attempts;
            maxAttempts = Mathf.Max(maxAttempts, report.Attempts);
            maxCenterVariance = Mathf.Max(maxCenterVariance, Mathf.CeilToInt(report.CenterDistanceVariancePercent));
            maxNearestItemGap = Mathf.Max(maxNearestItemGap,
                report.MaxNearestItemDistance - report.MinNearestItemDistance);
            if (report.UsedFallback) fallback++;
            if (!report.IsValid)
            {
                invalid++;
                if (firstFailure.Length == 0) firstFailure = string.Join(" | ", report.FailureReasons.ToArray());
            }
        }

        stopwatch.Stop();
        StringBuilder result = new StringBuilder(512);
        result.Append("[MapGenerationSeedTest] range=").Append(firstSeed).Append("..").Append(endExclusive - 1)
            .Append(" seeds=").Append(seedCount)
            .Append(" valid=").Append(seedCount - invalid)
            .Append(" invalid=").Append(invalid)
            .Append(" hardValidationRate=").Append(((seedCount - invalid) * 100f / seedCount).ToString("0.###")).Append('%')
            .Append(" fallback=").Append(fallback)
            .Append(" avgAttempts=").Append((totalAttempts / (float)seedCount).ToString("0.###"))
            .Append(" maxAttempts=").Append(maxAttempts)
            .Append(" maxCenterVariance=").Append(maxCenterVariance).Append('%')
            .Append(" maxNearestItemGap=").Append(maxNearestItemGap)
            .Append(" elapsedMs=").Append(stopwatch.ElapsedMilliseconds);
        if (firstFailure.Length > 0) result.Append(" firstFailure=").Append(firstFailure);
        return result.ToString();
    }
}
#endif
