using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using RpsBuild.Core;
using TMPro;


public sealed class GaugeBarView : MonoBehaviour
{
    [SerializeField] private Image guFill;
    [SerializeField] private Image chokiFill;
    [SerializeField] private Image paFill;

    [SerializeField] private Image guFrame;
    [SerializeField] private Image chokiFrame;
    [SerializeField] private Image paFrame;

    [SerializeField] private Sprite frameNormal; // 灰色枠
    [SerializeField] private Sprite frameCharged; // 黄色枠

    [SerializeField] private float chargedBlinkSpeed = 2f; // 光り方（任意）

    [SerializeField] private TMP_Text guStackText;
    [SerializeField] private TMP_Text chokiStackText;
    [SerializeField] private TMP_Text paStackText;

    // ★追加：表示用の最大値（Inspectorで調整可）
    [SerializeField] private int demoMax = 5;


    public void SetGauge(float gu, float choki, float pa, float max)
    {
        SetOne(guFill, guFrame, guStackText, gu, max);
        SetOne(chokiFill, chokiFrame, chokiStackText, choki, max);
        SetOne(paFill, paFrame, paStackText, pa, max);
    }

    private void SetOne(Image fill, Image frame, TMP_Text stackText, float value, float max)
        {
            max = Mathf.Max(0.0001f, max);

            int stacks = Mathf.FloorToInt(value / max);
            float rem = value - stacks * max;

            float t = Mathf.Clamp01(rem / max);
            if (fill != null) fill.fillAmount = t;

            if (stackText != null)
                stackText.text = (stacks > 0) ? stacks.ToString() : "";

            bool charged = value >= max - 1e-6f;

            if (frame == null) return;

            if (charged)
            {
                frame.sprite = frameCharged;
                float a = 0.75f + 0.25f * Mathf.PingPong(Time.time * chargedBlinkSpeed, 1f);
                var c = frame.color; c.a = a; frame.color = c;
            }
            else
            {
                frame.sprite = frameNormal;
                var c = frame.color; c.a = 1f; frame.color = c;
            }
        }

        // GaugeBarView.cs
        public void PlayTutorialDemo_Gauge()
        {
            StopAllCoroutines();
            StartCoroutine(TutorialGaugeDemo());
        }

        private IEnumerator TutorialGaugeDemo()
        {
            // 表示だけ初期化（実RunStateは触らない）
            SetGauge(0, 0, 0, demoMax);
            yield return new WaitForSeconds(0.2f);

            // 例：グーのゲージが増えるデモ
            for (int i = 0; i <= demoMax; i++)
            {
                SetGauge(i, 0, 0, demoMax);
                yield return new WaitForSeconds(0.05f);
            }

            // 予約を示す軽い演出（点滅など）
            yield return BlinkOnce();
        }

        private IEnumerator BlinkOnce()
        {
            // ここは見た目だけ（色変更など）
            yield return new WaitForSeconds(0.3f);
        }



}
