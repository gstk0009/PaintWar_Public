using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class GuideBookUI : MonoBehaviour
{
    [Serializable]
    public class GuideSpread
    {
        [Header("왼쪽 페이지")]
        public Sprite leftPage;

        [Header("오른쪽 페이지")]
        public Sprite rightPage;
    }

    // =========================================================
    // GUIDE PANEL
    // =========================================================

    [Header("Guide Panel")]
    [SerializeField]
    private GameObject guidePanel;

    // =========================================================
    // NORMAL PAGES
    // =========================================================

    [Header("Normal Pages")]
    [SerializeField]
    private Image leftImage;

    [SerializeField]
    private Image rightImage;

    // =========================================================
    // FLIP PAGE
    // =========================================================

    [Header("Flip Page")]
    [SerializeField]
    private RectTransform flipPage;

    [Tooltip("실제로 페이지 넘김에 사용하는 Image")]
    [SerializeField]
    private Image flipFront;

    [Tooltip("현재 방식에서는 사용하지 않음. 자동으로 비활성화됩니다.")]
    [SerializeField]
    private Image flipBack;

    // =========================================================
    // BUTTONS
    // =========================================================

    [Header("Buttons")]
    [SerializeField]
    private Button nextButton;

    [SerializeField]
    private Button prevButton;

    [SerializeField]
    private Button closeButton;

    // =========================================================
    // PAGE DATA
    // =========================================================

    [Header("Guide Pages")]
    [SerializeField]
    private GuideSpread[] spreads;

    // =========================================================
    // ANIMATION
    // =========================================================

    [Header("Flip Animation")]
    [SerializeField]
    [Min(0.1f)]
    private float flipDuration = 0.5f;

    [SerializeField]
    private AnimationCurve flipCurve =
        AnimationCurve.EaseInOut(
            0f,
            0f,
            1f,
            1f
        );

    // =========================================================
    // OPTIONS
    // =========================================================

    [Header("Options")]
    [SerializeField]
    private bool resetToFirstPageOnOpen = true;

    // =========================================================
    // RUNTIME
    // =========================================================

    private int currentSpreadIndex = 0;
    private bool isFlipping = false;

    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        if (nextButton != null)
        {
            nextButton.onClick.AddListener(NextPage);
        }

        if (prevButton != null)
        {
            prevButton.onClick.AddListener(PreviousPage);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseGuide);
        }

        ResetFlipPage();
    }

    private void Start()
    {
        currentSpreadIndex = 0;

        RefreshPages();
    }

    private void OnDestroy()
    {
        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(NextPage);
        }

        if (prevButton != null)
        {
            prevButton.onClick.RemoveListener(PreviousPage);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseGuide);
        }
    }

    // =========================================================
    // OPEN
    // =========================================================

    public void OpenGuide()
    {
        if (guidePanel == null)
        {
            Debug.LogWarning(
                "[GuideBookUI] GuidePanel이 연결되지 않았습니다."
            );

            return;
        }

        if (spreads == null ||
            spreads.Length == 0)
        {
            Debug.LogWarning(
                "[GuideBookUI] Guide Spread가 없습니다."
            );

            return;
        }

        if (resetToFirstPageOnOpen)
        {
            currentSpreadIndex = 0;
        }

        isFlipping = false;

        ResetFlipPage();

        guidePanel.SetActive(true);

        RefreshPages();

        SetButtonsVisible(true);
    }

    // =========================================================
    // CLOSE
    // =========================================================

    public void CloseGuide()
    {
        // 페이지가 넘어가는 도중에는 닫을 수 없음
        if (isFlipping)
            return;

        if (guidePanel == null)
            return;

        guidePanel.SetActive(false);
    }

    // =========================================================
    // NEXT
    // =========================================================

    public void NextPage()
    {
        if (isFlipping)
            return;

        if (!HasValidPages())
            return;

        // 마지막 페이지
        if (currentSpreadIndex >=
            spreads.Length - 1)
        {
            return;
        }

        NextPageAsync().Forget();
    }

    private async UniTaskVoid NextPageAsync()
    {
        isFlipping = true;

        // =====================================================
        // 페이지 넘기는 동안 버튼 전부 숨김
        // =====================================================

        SetButtonsVisible(false);

        int nextIndex =
            currentSpreadIndex + 1;

        // =====================================================
        // FLIP PAGE 준비
        // =====================================================

        flipPage.gameObject.SetActive(true);

        // Back은 사용하지 않음
        if (flipBack != null)
        {
            flipBack.gameObject.SetActive(false);
        }

        flipFront.gameObject.SetActive(true);

        // =====================================================
        // 1단계
        //
        // 현재 오른쪽 페이지를
        // 오른쪽 -> 책 중앙으로 접는다.
        // =====================================================

        SetupFlipPageRight();

        flipPage.localScale =
            Vector3.one;

        // 넘기는 종이 = 현재 오른쪽 페이지
        flipFront.sprite =
            spreads[currentSpreadIndex]
                .rightPage;

        /*
         * FlipPage 아래에는
         * 다음 오른쪽 페이지를 미리 깔아둔다.
         *
         * 현재 오른쪽 페이지가 접히면서
         * 다음 오른쪽 페이지가 보이게 됨.
         */
        rightImage.sprite =
            spreads[nextIndex]
                .rightPage;

        // 1 -> 0
        await AnimateFlipScale(
            1f,
            0f
        );

        // =====================================================
        // 2단계
        //
        // 페이지가 완전히 접혀서
        // 안 보이는 순간 Sprite 변경
        // =====================================================

        flipFront.sprite =
            spreads[nextIndex]
                .leftPage;

        /*
         * 이제 다음 왼쪽 페이지가
         * 책 중앙 -> 왼쪽으로 펼쳐져야 함.
         */
        SetupFlipPageLeft();

        // Pivot 변경 후에도 0 상태 확실히 유지
        flipPage.localScale =
            new Vector3(
                0f,
                1f,
                1f
            );

        // =====================================================
        // 3단계
        //
        // 책 중앙 -> 왼쪽으로 펼침
        // =====================================================

        await AnimateFlipScale(
            0f,
            1f
        );

        // =====================================================
        // 완료
        // =====================================================

        currentSpreadIndex =
            nextIndex;

        RefreshPages();

        ResetFlipPage();

        isFlipping = false;

        // 버튼 다시 표시
        SetButtonsVisible(true);
    }

    // =========================================================
    // PREVIOUS
    // =========================================================

    public void PreviousPage()
    {

        if (isFlipping) { return; }

        if (spreads == null || spreads.Length == 0) { return; }

        if (currentSpreadIndex <= 0) { return; }

        PreviousPageAsync().Forget();
    }

    private async UniTaskVoid PreviousPageAsync()
    {
        isFlipping = true;

        // =====================================================
        // 페이지 넘기는 동안 버튼 전부 숨김
        // =====================================================

        SetButtonsVisible(false);

        int previousIndex =
            currentSpreadIndex - 1;

        // =====================================================
        // FLIP PAGE 준비
        // =====================================================

        flipPage.gameObject.SetActive(true);

        if (flipBack != null)
        {
            flipBack.gameObject.SetActive(false);
        }

        flipFront.gameObject.SetActive(true);

        // =====================================================
        // 1단계
        //
        // 현재 왼쪽 페이지를
        // 왼쪽 -> 책 중앙으로 접는다.
        // =====================================================

        SetupFlipPageLeft();

        flipPage.localScale =
            Vector3.one;

        // 넘기는 종이 = 현재 왼쪽 페이지
        flipFront.sprite =
            spreads[currentSpreadIndex]
                .leftPage;

        /*
         * FlipPage 밑에는
         * 이전 왼쪽 페이지를 미리 깔아둔다.
         *
         * 현재 왼쪽이 접히면서
         * 이전 왼쪽 페이지가 보인다.
         */
        leftImage.sprite =
            spreads[previousIndex]
                .leftPage;

        // 1 -> 0
        await AnimateFlipScale(
            1f,
            0f
        );

        // =====================================================
        // 2단계
        //
        // 완전히 안 보이는 순간
        // 이전 Spread의 오른쪽 페이지로 변경
        // =====================================================

        flipFront.sprite =
            spreads[previousIndex]
                .rightPage;

        /*
         * 이제 이전 오른쪽 페이지가
         * 책 중앙 -> 오른쪽으로 펼쳐져야 함.
         */
        SetupFlipPageRight();

        // Pivot 변경 후 Scale 0 유지
        flipPage.localScale =
            new Vector3(
                0f,
                1f,
                1f
            );

        // =====================================================
        // 3단계
        //
        // 책 중앙 -> 오른쪽으로 펼침
        // =====================================================

        await AnimateFlipScale(
            0f,
            1f
        );

        // =====================================================
        // 완료
        // =====================================================

        currentSpreadIndex =
            previousIndex;

        RefreshPages();

        ResetFlipPage();

        isFlipping = false;

        // 버튼 다시 표시
        SetButtonsVisible(true);
    }

    // =========================================================
    // FLIP ANIMATION
    // =========================================================

    private async UniTask AnimateFlipScale(
        float from,
        float to)
    {
        float halfDuration =
            flipDuration * 0.5f;

        float elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / halfDuration
                );

            float curvedT =
                flipCurve.Evaluate(t);

            float scaleX =
                Mathf.Lerp(
                    from,
                    to,
                    curvedT
                );

            flipPage.localScale =
                new Vector3(
                    scaleX,
                    1f,
                    1f
                );

            await UniTask.Yield(
                PlayerLoopTiming.Update
            );
        }

        // 마지막 값 정확하게 보정
        flipPage.localScale =
            new Vector3(
                to,
                1f,
                1f
            );
    }

    // =========================================================
    // FLIP PAGE - RIGHT
    // =========================================================

    /// <summary>
    /// FlipPage를 오른쪽 페이지로 사용.
    ///
    /// Pivot:
    /// 왼쪽 끝 = 책 중앙
    ///
    /// 화면:
    ///
    ///      책 중앙
    ///         ↓
    /// --------|████████
    ///         ↑
    ///       Pivot
    /// </summary>
    private void SetupFlipPageRight()
    {
        if (flipPage == null)
            return;

        // 부모의 정확한 중앙
        flipPage.anchorMin =
            new Vector2(
                0.5f,
                0.5f
            );

        flipPage.anchorMax =
            new Vector2(
                0.5f,
                0.5f
            );

        // 왼쪽 끝을 책 중앙에 고정
        flipPage.pivot =
            new Vector2(
                0f,
                0.5f
            );

        flipPage.anchoredPosition =
            Vector2.zero;
    }

    // =========================================================
    // FLIP PAGE - LEFT
    // =========================================================

    /// <summary>
    /// FlipPage를 왼쪽 페이지로 사용.
    ///
    /// Pivot:
    /// 오른쪽 끝 = 책 중앙
    ///
    /// 화면:
    ///
    /// ████████|--------
    ///         ↑
    ///       Pivot
    /// </summary>
    private void SetupFlipPageLeft()
    {
        if (flipPage == null)
            return;

        // 부모의 정확한 중앙
        flipPage.anchorMin =
            new Vector2(
                0.5f,
                0.5f
            );

        flipPage.anchorMax =
            new Vector2(
                0.5f,
                0.5f
            );

        // 오른쪽 끝을 책 중앙에 고정
        flipPage.pivot =
            new Vector2(
                1f,
                0.5f
            );

        flipPage.anchoredPosition =
            Vector2.zero;
    }

    // =========================================================
    // PAGE REFRESH
    // =========================================================

    private void RefreshPages()
    {
        if (!HasValidPages())
            return;

        currentSpreadIndex =
            Mathf.Clamp(
                currentSpreadIndex,
                0,
                spreads.Length - 1
            );

        if (leftImage != null)
        {
            leftImage.sprite =
                spreads[currentSpreadIndex]
                    .leftPage;
        }

        if (rightImage != null)
        {
            rightImage.sprite =
                spreads[currentSpreadIndex]
                    .rightPage;
        }

        RefreshButtonState();
    }

    // =========================================================
    // BUTTON VISIBILITY
    // =========================================================

    /// <summary>
    /// 페이지 Flip 중에는 버튼을
    /// 아예 화면에서 숨긴다.
    /// </summary>
    private void SetButtonsVisible(
        bool visible)
    {
        /*
         * 다시 보이게 만들기 전에
         * 현재 페이지에 맞춰 interactable 갱신.
         */
        if (visible)
        {
            RefreshButtonState();
        }

        if (nextButton != null)
        {
            nextButton.gameObject
                .SetActive(visible);
        }

        if (prevButton != null)
        {
            prevButton.gameObject
                .SetActive(visible);
        }

        if (closeButton != null)
        {
            closeButton.gameObject
                .SetActive(visible);
        }
    }

    // =========================================================
    // BUTTON STATE
    // =========================================================

    private void RefreshButtonState()
    {
        if (prevButton != null)
        {
            // 첫 페이지에서는 Previous 사용 불가
            prevButton.interactable =
                currentSpreadIndex > 0;
        }

        if (nextButton != null)
        {
            // 마지막 페이지에서는 Next 사용 불가
            nextButton.interactable =
                HasValidPages() &&
                currentSpreadIndex <
                spreads.Length - 1;
        }

        if (closeButton != null)
        {
            closeButton.interactable = true;
        }
    }

    // =========================================================
    // VALIDATE
    // =========================================================

    private bool HasValidPages()
    {
        return spreads != null &&
               spreads.Length > 0;
    }

    // =========================================================
    // RESET FLIP
    // =========================================================

    private void ResetFlipPage()
    {
        if (flipPage == null)
            return;

        flipPage.localScale =
            Vector3.one;

        if (flipFront != null)
        {
            flipFront.gameObject
                .SetActive(true);
        }

        /*
         * 현재 방식에서는 Back을 사용하지 않음.
         * Hierarchy에 남아 있어도 상관 없음.
         */
        if (flipBack != null)
        {
            flipBack.gameObject
                .SetActive(false);
        }

        flipPage.gameObject
            .SetActive(false);
    }
}