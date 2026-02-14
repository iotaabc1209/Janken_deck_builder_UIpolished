using UnityEngine;
using UnityEngine.UI;
using RpsBuild.Core;
using System.Collections;
using System.Collections.Generic;

public sealed class ReservedSlotsView : MonoBehaviour
{
    [Header("Slots (7)")]
    [SerializeField] private Image[] slots;

    [Header("Sprites")]
    [SerializeField] private Sprite guSprite;
    [SerializeField] private Sprite chokiSprite;
    [SerializeField] private Sprite paSprite;

    [Header("Empty")]
    [SerializeField] private Sprite emptySprite;          // 無ければnullでOK
    [Range(0f, 1f)]
    [SerializeField] private float emptyAlpha = 0.15f;    // 空スロットの薄さ

    // ===== Tutorial Demo =====
    [Header("Tutorial Demo")]
    [SerializeField] private float demoStepDelay = 0.35f;
    [SerializeField] private float popScale = 1.15f;
    [SerializeField] private float popDuration = 0.12f;

    private Coroutine _demoCo;

    public void Render(IReadOnlyList<RpsColor> order)
    {
        int n = (order != null) ? order.Count : 0;

        for (int i = 0; i < slots.Length; i++)
        {
            var img = slots[i];
            if (img == null) continue;

            if (i < n)
            {
                img.enabled = true;
                img.sprite = ToSprite(order[i]);
                img.color = Color.white;
            }
            else
            {
                img.enabled = true;
                img.sprite = emptySprite; // nullでもOK
                img.color = new Color(1f, 1f, 1f, emptyAlpha);
            }
        }
    }

    // ★追加：チュートリアル用（表示だけデモ）
    public void PlayTutorialDemo_Reserved()
    {
        StopTutorialDemo();
        _demoCo = StartCoroutine(TutorialDemoCoroutine());
    }

    public void StopTutorialDemo()
    {
        if (_demoCo != null) StopCoroutine(_demoCo);
        _demoCo = null;

        // スケールを戻す（デモ途中で止めた時の保険）
        ResetSlotScales();
    }

    private IEnumerator TutorialDemoCoroutine()
    {
        // 0個
        Render(new List<RpsColor>());
        yield return new WaitForSeconds(demoStepDelay);

        // 1個
        Render(new List<RpsColor> { RpsColor.Gu });
        yield return PopSlot(0);
        yield return new WaitForSeconds(demoStepDelay);

        // 2個
        Render(new List<RpsColor> { RpsColor.Gu, RpsColor.Choki });
        yield return PopSlot(1);
        yield return new WaitForSeconds(demoStepDelay);

        // 3個
        Render(new List<RpsColor> { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa });
        yield return PopSlot(2);
        yield return new WaitForSeconds(demoStepDelay);

        // お好みで終了（このまま残してOK）
        ResetSlotScales();
        _demoCo = null;
    }

    private IEnumerator PopSlot(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length) yield break;
        var img = slots[index];
        if (img == null) yield break;

        var tr = img.rectTransform;
        if (tr == null) yield break;

        Vector3 baseScale = Vector3.one;
        tr.localScale = baseScale;

        // 伸びる
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, popDuration);
            float s = Mathf.Lerp(1f, popScale, t);
            tr.localScale = baseScale * s;
            yield return null;
        }

        // 戻る
        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, popDuration);
            float s = Mathf.Lerp(popScale, 1f, t);
            tr.localScale = baseScale * s;
            yield return null;
        }

        tr.localScale = baseScale;
    }

    private void ResetSlotScales()
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            slots[i].rectTransform.localScale = Vector3.one;
        }
    }

    private Sprite ToSprite(RpsColor c)
    {
        return c switch
        {
            RpsColor.Gu => guSprite,
            RpsColor.Choki => chokiSprite,
            _ => paSprite
        };
    }
}
