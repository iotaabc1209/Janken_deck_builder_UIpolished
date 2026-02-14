// Assets/Scripts/Unity/UnityRng.cs
using UnityEngine;
using RpsBuild.Core;

public sealed class UnityRng : IRng
{
    public int Range(int minInclusive, int maxExclusive)
        => Random.Range(minInclusive, maxExclusive);

    public float Value01()
        => Random.value;
}
