using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RpsBuild.Core;

public sealed class HudPlayerStatsView : MonoBehaviour
{
    [Header("Life (Hearts)")]
    [SerializeField] private Image heart1;
    [SerializeField] private Image heart2;
    [SerializeField] private Image heart3;

    [Header("Score")]
    [SerializeField] private TMP_Text scoreValueText;  // "Score：N"

    private Image[] _hearts;

    private void Awake()
    {
        _hearts = new[] { heart1, heart2, heart3 };
    }

    public void Render(RunState run)
    {
        if (run == null)
        {
            SetHearts(0);

            if (scoreValueText != null)
                scoreValueText.text = "Score：0";

            return;
        }

        int livesLeft = Mathf.Max(0, run.Tuning.MaxMiss - run.MissCount);
        SetHearts(livesLeft);

        if (scoreValueText != null)
            scoreValueText.text = $"Score：{run.Score}";
    }

    private void SetHearts(int lives)
    {
        for (int i = 0; i < _hearts.Length; i++)
        {
            var img = _hearts[i];
            if (img == null) continue;

            bool on = i < lives;
            img.color = on
                ? Color.white
                : new Color(1f, 1f, 1f, 0.25f);
        }
    }
}
