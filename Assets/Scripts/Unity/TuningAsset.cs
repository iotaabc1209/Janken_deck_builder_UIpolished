// Assets/Scripts/Unity/TuningAsset.cs
using UnityEngine;
using RpsBuild.Core;

[CreateAssetMenu(menuName = "RpsBuild/TuningAsset")]
public sealed class TuningAsset : ScriptableObject
{
    public int handCount = 7;
    public int loseThresholdExclusive = 3;
    public int maxMiss = 3;

    [Header("Gauge")]
    public float gaugeMax = 1f;
    public float gainScale = 1f;      // LinearGaugeGainFormula の係数
    public float gainDenominator = 10f;

    [Header("Environment Archetype Weights")]
    public float heavy = 0.60f;
    public float balance = 0.25f;
    public float twinTop = 0.15f;

    [Header("Environment Weight Assignment")]
    public bool shuffleEnvWeightsAcrossArchetypes = true;

    [Header("Enemy Main Color Rule")]
    public bool uniqueMainAcrossArchetypes = true;

    [Header("Adjust Points")]
    public int initialPoints = 10;
    public int pointsPerClear = 1;

    [Header("Gauge Buy (per 1 point)")]
    public float gaugeBuyAmount = 0.5f;


    public Tuning ToTuning()
    {
        return new Tuning
        {
            HandCount = handCount,
            LoseThresholdExclusive = loseThresholdExclusive,
            MaxMiss = maxMiss,
            GaugeMax = gaugeMax,
            UniqueMainAcrossArchetypes = uniqueMainAcrossArchetypes,
            EnvWeights = new ArchetypeWeights { Heavy = heavy, Balance = balance, TwinTop = twinTop },
            ShuffleEnvWeightsAcrossArchetypes = shuffleEnvWeightsAcrossArchetypes,

            InitialPoints = initialPoints,
            PointsPerClear = pointsPerClear,
            GaugeBuyAmount = gaugeBuyAmount
        };
    }


    public LinearGaugeGainFormula CreateGainFormula()
    {
        return new LinearGaugeGainFormula
        {
            NumeratorScale = gainScale,
            Denominator = gainDenominator
        };
    }
}
