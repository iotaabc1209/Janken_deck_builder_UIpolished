using System.Collections.Generic;
using UnityEngine;
using TMPro;
using RpsBuild.Core;

public sealed class ResolveOutcomeRowView : MonoBehaviour
{
    [Header("Order matters: 0..6")]
    [SerializeField] private List<TMP_Text> outcomeTexts = new();

    [Header("Letters")]
    [SerializeField] private string winChar = "W";
    [SerializeField] private string loseChar = "L";
    [SerializeField] private string tieChar = "D";

    [Header("Colors (Win/Tie/Lose)")]
    [SerializeField] private Color winColor = new Color(0.36f, 0.54f, 0.89f, 1f);   // 青寄り
    [SerializeField] private Color tieColor = new Color(0.20f, 0.75f, 0.55f, 1f);   // 緑寄り
    [SerializeField] private Color loseColor = new Color(0.89f, 0.35f, 0.35f, 1f);  // 赤寄り

    public void Show(IReadOnlyList<RpsOutcome> outcomes)
    {
        if (outcomes == null) return;

        for (int i = 0; i < outcomeTexts.Count; i++)
        {
            var t = outcomeTexts[i];
            if (t == null) continue;

            if (i < outcomes.Count)
            {
                t.gameObject.SetActive(true);

                switch (outcomes[i])
                {
                    case RpsOutcome.Win:
                        t.text = winChar;
                        t.color = winColor;
                        break;

                    case RpsOutcome.Lose:
                        t.text = loseChar;
                        t.color = loseColor;
                        break;

                    default: // Tie
                        t.text = tieChar;
                        t.color = tieColor;
                        break;
                }
            }
            else
            {
                t.gameObject.SetActive(false);
            }
        }
    }
}
