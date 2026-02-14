using UnityEngine;
using UnityEngine.UI;
using RpsBuild.Core;
using TMPro;

public sealed class ForcedFirstToggleView : MonoBehaviour
{
    [SerializeField] private RunPresenter presenter;
    [SerializeField] private GaugeBarView gaugeBarView;
    [SerializeField] private AdjustPanelView adjustView;


    [Header("Frames")]
    [SerializeField] private Image frameGu;
    [SerializeField] private Image frameChoki;
    [SerializeField] private Image framePa;
    [SerializeField] private Sprite frameOnSprite;
    [SerializeField] private Sprite frameOffSprite;

    [Header("Forced Count Texts (optional)")]
    [SerializeField] private TMP_Text forcedNumberGuText;
    [SerializeField] private TMP_Text forcedNumberChokiText;
    [SerializeField] private TMP_Text forcedNumberPaText;


    public void OnClickGu()    => Toggle(RpsColor.Gu);
    public void OnClickChoki() => Toggle(RpsColor.Choki);
    public void OnClickPa()    => Toggle(RpsColor.Pa);

    private void Toggle(RpsColor c)
    {
        Debug.Log("[ForcedClick] {c}");
        // Adjust中：ドラフトを切り替える（B1）
        if (adjustView != null && adjustView.isActiveAndEnabled)
        {
            bool ok = adjustView.ToggleDraftForcedFirst(c);
            if (ok) RefreshVisual();
            return;
        }


        // （もしAdjust以外でも使うなら）従来のRun直叩きは残す
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        if (run.ReservedForcedFirst == c)
        {
            run.TryCancelReservedForcedFirst();
            RefreshVisual();
            return;
        }

        if (!run.TryReserveForcedFirst(c)) return;
        RefreshVisual();
    }



    public void RefreshVisual()
    {
        // Adjust中：ドラフト枠
        if (adjustView != null && adjustView.isActiveAndEnabled)
        {
            int gu = adjustView.GetDraftForcedCount(RpsColor.Gu);
            int ch = adjustView.GetDraftForcedCount(RpsColor.Choki);
            int pa = adjustView.GetDraftForcedCount(RpsColor.Pa);

            SetFrame(frameGu,    gu > 0);
            SetFrame(frameChoki, ch > 0);
            SetFrame(framePa,    pa > 0);

            SetForcedNumber(forcedNumberGuText,    gu);
            SetForcedNumber(forcedNumberChokiText, ch);
            SetForcedNumber(forcedNumberPaText,    pa);
            return;

        }

        // 従来：Runの枠
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        SetFrame(frameGu,    run.ReservedForcedFirst == RpsColor.Gu);
        SetFrame(frameChoki, run.ReservedForcedFirst == RpsColor.Choki);
        SetFrame(framePa,    run.ReservedForcedFirst == RpsColor.Pa);
    }

    private void OnEnable()
    {
        if (adjustView != null) adjustView.OnDraftChanged += RefreshVisual;
        RefreshVisual();
    }

    private void OnDisable()
    {
        if (adjustView != null) adjustView.OnDraftChanged -= RefreshVisual;
    }



    // AdjustPanelView の _gaugeAdd/_gaugeSub に直接アクセスできないので、ここは「最小差分」用の暫定。
    // → 次の返信で “AdjustPanelView に GetGaugeDraftDelta(color) を1個追加” してきれいにします。
    private int adjustViewGaugeDelta(RpsColor c)
    {
        // いったん 0 返しだと表示がズレるので、次のステップで AdjustPanelView 側に公開関数を追加する前提。
        return 0;
    }


    private void SetFrame(Image img, bool on)
    {
        if (img == null) return;
        img.sprite = on ? frameOnSprite : frameOffSprite;
        img.color = Color.white;
    }

    private void SetForcedNumber(TMP_Text t, int n)
    {
        if (t == null) return;
        t.text = (n > 0) ? $"{n}枚確定" : "";
    }


}
