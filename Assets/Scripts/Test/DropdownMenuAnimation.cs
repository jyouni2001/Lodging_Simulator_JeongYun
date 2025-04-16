using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;

public class MenuAnimation : MonoBehaviour
{
    public RectTransform panel; // 패널 (버튼들이 포함된 부모)
    public List<RectTransform> buttons; // 버튼 리스트
    public float animationDuration = 0.3f;
    public float delayBetweenButtons = 0.05f;
    private bool isExpanded = false;
    private Tween activeTween;
    private Vector2[] defaultPositions;

    void Start()
    {
        defaultPositions = new Vector2[buttons.Count];

        for (int i = 0; i < buttons.Count; i++)
        {
            defaultPositions[i] = buttons[i].anchoredPosition;
            buttons[i].gameObject.SetActive(false);
            buttons[i].anchoredPosition = defaultPositions[i] + new Vector2(0, 50);
        }
    }

    public void ToggleMenu()
    {
        if (activeTween != null && activeTween.IsActive()) 
        {
            activeTween.Kill();
        }

        float panelTop = panel.rect.height / 2; // 패널의 최상단 Y 좌표
        float panelBottom = -panel.rect.height / 2; // 패널의 최하단 Y 좌표

        if (!isExpanded)
        {
            activeTween = DOTween.Sequence()
                .OnStart(() =>
                {
                    for (int i = 0; i < buttons.Count; i++)
                    {
                        buttons[i].gameObject.SetActive(true);
                        buttons[i].anchoredPosition = defaultPositions[i] + new Vector2(0, 50);
                    }
                })
                .AppendInterval(0.1f)
                .AppendCallback(() =>
                {
                    for (int i = 0; i < buttons.Count; i++)
                    {
                        float targetY = defaultPositions[i].y;
                        if (targetY > panelTop) targetY = panelTop; // 패널 위쪽 넘지 않도록 제한
                        if (targetY < panelBottom) targetY = panelBottom; // 패널 아래쪽 넘지 않도록 제한

                        buttons[i].DOAnchorPosY(targetY, animationDuration)
                            .SetEase(Ease.OutBounce)
                            .SetDelay(i * delayBetweenButtons);
                    }
                })
                .OnComplete(() => isExpanded = true);
        }
        else
        {
            activeTween = DOTween.Sequence()
                .AppendCallback(() =>
                {
                    for (int i = buttons.Count - 1; i >= 0; i--)
                    {
                        float targetY = defaultPositions[0].y + 50;
                        if (targetY > panelTop) targetY = panelTop;
                        if (targetY < panelBottom) targetY = panelBottom;

                        buttons[i].DOAnchorPosY(targetY, animationDuration)
                            .SetEase(Ease.InOutQuad)
                            .SetDelay((buttons.Count - 1 - i) * delayBetweenButtons);
                    }
                })
                .AppendInterval(animationDuration + (buttons.Count - 1) * delayBetweenButtons)
                .OnComplete(() =>
                {
                    foreach (var btn in buttons)
                    {
                        btn.gameObject.SetActive(false);
                        btn.anchoredPosition = defaultPositions[btn.transform.GetSiblingIndex()] + new Vector2(0, 50);
                    }
                    isExpanded = false;
                });
        }
    }
}
