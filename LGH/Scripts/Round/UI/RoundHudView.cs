using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 매치 타이머와 참가자별 점령량 막대를 표시하는 런타임 HUD입니다.
// 매치 타이머와 참가자별 점령 현황을 표시하는 HUD입니다.
// 씬에 "Occupation HUD", "Round Timer HUD"가 있으면 재사용하고, 없으면 런타임에 자동 생성합니다.
public class RoundHudView : MonoBehaviour
{
    private static readonly int BaseColorProperty =
        Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty =
        Shader.PropertyToID("_Color");

    [Header("Size")]
    [SerializeField] private Vector2 landscapeScoreSize = new Vector2(250f, 124f);
    [SerializeField] private Vector2 portraitScoreSize = new Vector2(220f, 116f);
    [SerializeField] private Vector2 timerSize = new Vector2(160f, 48f);
    [SerializeField] private float landscapeBarHeight = 24f;
    [SerializeField] private float portraitBarHeight = 22f;

    private static readonly Dictionary<PaintOwner, Color> OwnerColors = new Dictionary<PaintOwner, Color>
    {
        { PaintOwner.Player1, new Color(1f, 0.035f, 0.02f, 1f) },
        { PaintOwner.Player2, new Color(0.015f, 0.16f, 1f, 1f) },
        { PaintOwner.Player3, new Color(1f, 0.78f, 0.02f, 1f) },
        { PaintOwner.Player4, new Color(0.06f, 0.72f, 0.015f, 1f) }
    };

    [SerializeField] private RectTransform scoreRoot;
    [SerializeField] private RectTransform timerRoot;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private PlayerHUDBar[] scoreBars;

    private readonly List<PlayerHUDBar> bars = new List<PlayerHUDBar>();
    private readonly List<GameParticipant> sortedParticipants = new List<GameParticipant>(4);
    private bool isLandscape;
    private bool controlsHudLayout;
    private int displayedSecond = int.MinValue;
    private int displayedScoreSignature = int.MinValue;
    private float nextScoreRefreshTime;

    // HUD UI 계층을 런타임에 생성합니다.
    private void Awake()
    {
        EnsureHud();
    }

    private void OnEnable()
    {
        EnsureHud();
        SetHudActive(true);
    }

    private void OnDisable()
    {
        SetHudActive(false);
    }

    // 라운드 매니저가 준비된 뒤 화면 방향, 타이머, 점수 막대를 매 프레임 갱신합니다.
    private void Update()
    {
        if (!GameRoundManager.HasInstance)
        {
            UpdateAnchors();
            return;
        }

        UpdateAnchors();
        UpdateTimer();

        if (Time.unscaledTime >= nextScoreRefreshTime)
        {
            nextScoreRefreshTime = Time.unscaledTime + 0.1f;
            UpdateScoreBarsIfChanged();
        }
    }

    // Canvas가 없으면 만들고, 점령 현황 패널과 타이머 패널을 구성합니다.
    private void BuildHud()
    {
        // 테스트 씬에 Canvas가 없어도 HUD를 볼 수 있도록 최소 Canvas까지 자동 생성합니다.
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            // 테스트 씬에 Canvas가 없을 때도 HUD가 뜨도록 런타임 Canvas를 보조 생성합니다.
            GameObject canvasObject = new GameObject("Runtime HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        scoreRoot = CreatePanel("Occupation HUD", canvas.transform, landscapeScoreSize);
        VerticalLayoutGroup layout = scoreRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        for (int i = 0; i < 4; i++)
        {
            bars.Add(CreateBar(scoreRoot));
        }

        timerRoot = CreatePanel("Round Timer HUD", canvas.transform, timerSize);
        VerticalLayoutGroup timerLayout = timerRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        timerLayout.spacing = 0f;
        timerLayout.padding = new RectOffset(8, 8, 3, 3);
        timerLayout.childAlignment = TextAnchor.MiddleCenter;
        timerLayout.childControlWidth = true;
        timerLayout.childControlHeight = true;

        timerText = CreateText("Timer", timerRoot, "01:00", 22, TextAlignmentOptions.Center);
        controlsHudLayout = true;
        UpdateAnchors(true);
    }

    private void EnsureHud()
    {
        // Linux dedicated server처럼 화면이 없는 batch mode에서는 UI를 만들 필요가 없습니다.
        if (Application.isBatchMode)
        {
            return;
        }

        BindSerializedHudReferences();
        if (HasCompleteHudBinding())
        {
            return;
        }

        TryBindExistingHud();
        if (HasCompleteHudBinding())
        {
            // 사용자가 씬에 직접 만든 HUD 오브젝트가 있으면 그걸 우선 사용합니다.
            controlsHudLayout = false;
            return;
        }

        Debug.LogError(
            "[RoundHudView] Fixed HUD references are missing. Configure the GameScene HUD in the editor.",
            this);
        enabled = false;
    }

    private void TryBindExistingHud()
    {
        // 런타임 생성 대신 씬 오브젝트를 직접 보고 수정할 수 있게 이름 기반으로 기존 HUD를 찾습니다.
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = FindCanvasContainingHud();
        }

        if (canvas == null)
        {
            return;
        }

        scoreRoot = FindChildRect(canvas.transform, "Occupation HUD");
        timerRoot = FindChildRect(canvas.transform, "Round Timer HUD");
        bars.Clear();
        if (scoreRoot != null)
        {
            bars.AddRange(scoreRoot.GetComponentsInChildren<PlayerHUDBar>(true));
        }

        timerText = FindChildText(timerRoot, "Timer");
    }

    private void BindSerializedHudReferences()
    {
        if (scoreRoot != null)
        {
            bars.Clear();
            if (scoreBars != null && scoreBars.Length > 0)
            {
                for (int i = 0; i < scoreBars.Length; i++)
                {
                    if (scoreBars[i] != null)
                    {
                        bars.Add(scoreBars[i]);
                    }
                }
            }

            if (bars.Count == 0)
            {
                bars.AddRange(scoreRoot.GetComponentsInChildren<PlayerHUDBar>(true));
            }

            SortBarsByVisualOrder();
        }

        if (timerRoot != null)
        {
            if (timerText == null)
            {
                timerText = FindChildText(timerRoot, "Timer");
            }

        }
    }

    private bool HasCompleteHudBinding()
    {
        return scoreRoot != null &&
               timerRoot != null &&
               timerText != null &&
               bars.Count > 0;
    }

    private void SortBarsByVisualOrder()
    {
        bars.Sort(CompareBarsByVisualOrder);
    }

    private static int CompareBarsByVisualOrder(PlayerHUDBar left, PlayerHUDBar right)
    {
        if (left == right)
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        Transform leftTransform = left.transform;
        Transform rightTransform = right.transform;
        if (leftTransform.parent == rightTransform.parent)
        {
            return leftTransform.GetSiblingIndex().CompareTo(rightTransform.GetSiblingIndex());
        }

        RectTransform leftRect = leftTransform as RectTransform;
        RectTransform rightRect = rightTransform as RectTransform;
        if (leftRect != null && rightRect != null)
        {
            return rightRect.position.y.CompareTo(leftRect.position.y);
        }

        return string.CompareOrdinal(left.name, right.name);
    }

    private Canvas FindCanvasContainingHud()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
            {
                continue;
            }

            if (FindChildRect(canvas.transform, "Occupation HUD") != null &&
                FindChildRect(canvas.transform, "Round Timer HUD") != null)
            {
                return canvas;
            }
        }

        return null;
    }

    // 반투명 배경 이미지를 가진 HUD 패널 RectTransform을 생성합니다.
    private RectTransform CreatePanel(string objectName, Transform parent, Vector2 size)
    {
        GameObject panel = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.sizeDelta = size;

        Image image = panel.GetComponent<Image>();
        image.color = new Color(0.05f, 0.06f, 0.07f, 0.72f);
        image.raycastTarget = false;

        return rect;
    }

    // 참가자 한 명의 점령량을 표시할 막대 UI를 만들고 PlayerHUDBar와 연결합니다.
    private PlayerHUDBar CreateBar(RectTransform parent)
    {
        GameObject row = new GameObject("Player HUD Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(PlayerHUDBar), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        LayoutElement layoutElement = row.GetComponent<LayoutElement>();
        layoutElement.preferredHeight = landscapeBarHeight;

        Image background = row.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.42f);
        background.raycastTarget = false;

        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);

        GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObject.transform.SetParent(row.transform, false);
        Image fill = fillObject.GetComponent<Image>();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 0f;
        fill.raycastTarget = false;

        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = CreateText("Label", rowRect, "P1", 17, TextAlignmentOptions.Left);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(0.72f, 1f);
        labelRect.offsetMin = new Vector2(8f, 0f);
        labelRect.offsetMax = new Vector2(0f, 0f);

        TextMeshProUGUI count = CreateText("Count", rowRect, "0", 19, TextAlignmentOptions.Right);
        RectTransform countRect = count.GetComponent<RectTransform>();
        countRect.anchorMin = new Vector2(0.7f, 0f);
        countRect.anchorMax = new Vector2(1f, 1f);
        countRect.offsetMin = new Vector2(0f, 0f);
        countRect.offsetMax = new Vector2(-8f, 0f);

        PlayerHUDBar bar = row.GetComponent<PlayerHUDBar>();
        bar.Initialize(fill, label, count);
        return bar;
    }

    // 지정한 부모 아래에 TextMeshPro 텍스트를 생성하고 기본 표시 옵션을 설정합니다.
    private TextMeshProUGUI CreateText(string objectName, RectTransform parent, string text, int fontSize, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = textObject.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return tmp;
    }

    // 화면 가로/세로 비율에 따라 HUD 패널 위치와 크기를 다시 배치합니다.
    private void UpdateAnchors(bool force = false)
    {
        if (!controlsHudLayout || scoreRoot == null || timerRoot == null)
        {
            return;
        }

        bool nextLandscape = Screen.width > Screen.height;
        if (!force && nextLandscape == isLandscape)
        {
            return;
        }

        isLandscape = nextLandscape;

        if (isLandscape)
        {
            // LGH 테스트 버튼 영역을 피해 점령 현황은 우상단 버튼 아래, 타이머는 상단 중앙에 둡니다.
            AnchorTopCenter(timerRoot, new Vector2(0f, -16f));
            scoreRoot.sizeDelta = landscapeScoreSize;
            timerRoot.sizeDelta = timerSize;
            SetBarHeight(landscapeBarHeight);
            AnchorTopRight(scoreRoot, new Vector2(-16f, -66f));
        }
        else
        {
            scoreRoot.sizeDelta = portraitScoreSize;
            timerRoot.sizeDelta = timerSize;
            SetBarHeight(portraitBarHeight);
            AnchorTopLeft(scoreRoot, new Vector2(16f, -16f));
            AnchorTopCenter(timerRoot, new Vector2(0f, -16f));
        }
    }

    // 점령 현황 막대들의 높이를 화면 방향에 맞춰 변경합니다.
    private void SetBarHeight(float height)
    {
        for (int i = 0; i < bars.Count; i++)
        {
            LayoutElement layoutElement = bars[i].GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.preferredHeight = height;
            }
        }
    }

    // RectTransform을 화면 좌상단 기준 위치에 고정합니다.
    private void AnchorTopLeft(RectTransform rect, Vector2 anchoredPosition)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
    }

    // RectTransform을 화면 우상단 기준 위치에 고정합니다.
    private void AnchorTopRight(RectTransform rect, Vector2 anchoredPosition)
    {
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = anchoredPosition;
    }

    // RectTransform을 화면 상단 중앙 기준 위치에 고정합니다.
    private void AnchorTopCenter(RectTransform rect, Vector2 anchoredPosition)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
    }

    // 현재 페이즈 남은 시간과 페이즈 이름을 타이머 패널에 표시합니다.
    private void UpdateTimer()
    {
        GameRoundManager roundManager = GameRoundManager.Instance;
        float remaining = roundManager.CurrentPhase == GamePhase.MatchWaiting
            ? roundManager.MatchWaitRemainingSeconds
            : roundManager.PhaseRemainingSeconds;

        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);
        int totalSeconds = minutes * 60 + seconds;
        if (timerText != null && displayedSecond != totalSeconds)
        {
            timerText.SetText("{0:00}:{1:00}", minutes, seconds);
            displayedSecond = totalSeconds;
        }

    }

    private void UpdateScoreBarsIfChanged()
    {
        IReadOnlyList<GameParticipant> participants = GameRoundManager.Instance.Participants;
        int signature = participants.Count;
        signature = signature * 397 ^
                    (TilePaintManager.HasInstance ? TilePaintManager.Instance.ActiveTileCount : 0);
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant == null) continue;
            unchecked
            {
                signature = signature * 397 ^ (int)participant.Owner;
                signature = signature * 397 ^ participant.CurrentTileCount;
                signature = signature * 397 ^ (participant.IsEliminated ? 1 : 0);
                signature = signature * 397 ^ (participant.IsBot ? 1 : 0);
            }
        }

        if (signature == displayedScoreSignature) return;
        displayedScoreSignature = signature;
        UpdateScoreBars();
    }

    // 참가자를 점령 수 순서로 정렬하고 각 HUD 막대에 최신 상태를 반영합니다.
    private void UpdateScoreBars()
    {
        // HUD는 TilePaintManager의 현재 타일 카운트를 읽어 점령 순위를 표시합니다.
        IReadOnlyList<GameParticipant> participants = GameRoundManager.Instance.Participants;
        int totalTiles = TilePaintManager.HasInstance
            ? TilePaintManager.Instance.TerritoryRules.territoryTargetCount : 500;

        // 현재 점령 수가 높은 순으로 정렬하고 탈락자는 아래로 밀어 표시합니다.
        sortedParticipants.Clear();
        for (int i = 0; i < participants.Count; i++)
        {
            if (participants[i] != null)
            {
                sortedParticipants.Add(participants[i]);
            }
        }

        sortedParticipants.Sort(CompareParticipantsForHud);
        bool hasVisibleParticipant = sortedParticipants.Count > 0;
        if (scoreRoot != null && scoreRoot.gameObject.activeSelf != hasVisibleParticipant)
        {
            scoreRoot.gameObject.SetActive(hasVisibleParticipant);
        }

        for (int i = 0; i < bars.Count; i++)
        {
            GameParticipant participant = i < sortedParticipants.Count ? sortedParticipants[i] : null;
            Color color = participant != null
                ? GetOwnerDisplayColor(participant.Owner)
                : Color.white;

            bars[i].UpdateBar(participant, totalTiles, color);
        }
    }

    private static Color GetOwnerDisplayColor(PaintOwner owner)
    {
        if (TilePaintManager.HasInstance)
        {
            Material material = TilePaintManager.Instance.GetOwnerMaterial(owner);
            if (material != null)
            {
                if (material.HasProperty(BaseColorProperty))
                {
                    return material.GetColor(BaseColorProperty);
                }

                if (material.HasProperty(ColorProperty))
                {
                    return material.GetColor(ColorProperty);
                }
            }
        }

        return OwnerColors.TryGetValue(owner, out Color fallback)
            ? fallback
            : Color.white;
    }

    public bool TryGetBarForOwner(PaintOwner owner, out PlayerHUDBar bar)
    {
        bar = null;
        if (owner == PaintOwner.Neutral)
        {
            return false;
        }

        EnsureHud();
        for (int i = 0; i < bars.Count; i++)
        {
            PlayerHUDBar candidate = bars[i];
            if (candidate != null &&
                candidate.isActiveAndEnabled &&
                candidate.BoundOwner == owner)
            {
                bar = candidate;
                return true;
            }
        }

        return false;
    }

    private static int CompareParticipantsForHud(GameParticipant left, GameParticipant right)
    {
        int eliminatedCompare = left.IsEliminated.CompareTo(right.IsEliminated);
        if (eliminatedCompare != 0)
        {
            return eliminatedCompare;
        }

        return right.CurrentTileCount.CompareTo(left.CurrentTileCount);
    }

    private RectTransform FindChildRect(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i].name == childName)
            {
                return rects[i];
            }
        }

        return null;
    }

    private TextMeshProUGUI FindChildText(RectTransform root, string childName)
    {
        RectTransform rect = FindChildRect(root, childName);
        return rect != null ? rect.GetComponent<TextMeshProUGUI>() : null;
    }

    private void SetHudActive(bool active)
    {
        if (scoreRoot != null)
        {
            scoreRoot.gameObject.SetActive(active);
        }

        if (timerRoot != null)
        {
            timerRoot.gameObject.SetActive(active);
        }
    }

    private void SetOccupationValuesVisible(bool visible)
    {
        for (int i = 0; i < bars.Count; i++)
        {
            if (bars[i] != null)
            {
                bars[i].SetOccupationValuesVisible(visible);
            }
        }
    }
}
