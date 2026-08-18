using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD에서 참가자 한 명의 점령량, 이름, 탈락 상태를 한 줄 막대로 표시합니다.
/// </summary>
public class PlayerHUDBar : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI countText;
    [Header("Gameplay Emoji")]
    [SerializeField] private Image emojiImage;
    [SerializeField] private float emojiGap = 8f;

    private bool referencesResolved;
    private PaintOwner emojiOwner = PaintOwner.Neutral;

    public PaintOwner BoundOwner { get; private set; } = PaintOwner.Neutral;

    public RectTransform BubbleAnchor
    {
        get
        {
            EnsureReferences();
            RectTransform rowRect = transform as RectTransform;
            if (rowRect != null)
            {
                return rowRect;
            }

            return labelText != null ? labelText.rectTransform : null;
        }
    }

    /// <summary>
    /// Keeps the participant row and emoji anchor alive while hiding only the
    /// live occupation values during the final countdown.
    /// </summary>
    public void SetOccupationValuesVisible(bool visible)
    {
        EnsureReferences();
        if (fillImage != null)
        {
            fillImage.gameObject.SetActive(visible);
        }

        if (countText != null)
        {
            countText.gameObject.SetActive(visible);
        }
    }

    private void Awake()
    {
        EnsureReferences();
    }

    /// <summary>
    /// RoundHudView가 런타임에 만든 UI 컴포넌트 참조를 이 막대에 연결합니다.
    /// </summary>
    public void Initialize(Image fill, TextMeshProUGUI label, TextMeshProUGUI count)
    {
        fillImage = fill;
        labelText = label;
        countText = count;
        referencesResolved = true;
    }

    /// <summary>
    /// 참가자 상태를 읽어 막대 채움량, 이름, 점령 수, 탈락 표시를 갱신합니다.
    /// </summary>
    public void UpdateBar(GameParticipant participant, int territoryTargetCount, Color color)
    {
        EnsureReferences();

        if (participant == null)
        {
            BoundOwner = PaintOwner.Neutral;
            HideEmojiImmediate();
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        BoundOwner = participant.Owner;
        if (emojiOwner != PaintOwner.Neutral && emojiOwner != BoundOwner)
        {
            HideEmojiImmediate();
        }

        int claimed = participant.CurrentTileCount;
        float ratio = territoryTargetCount > 0 ? Mathf.Clamp01(claimed / (float)territoryTargetCount) : 0f;
        Color displayColor = participant.IsEliminated ? new Color(0.45f, 0.45f, 0.45f, 0.9f) : color;
        if (!participant.IsEliminated)
        {
            // Keep the exact tile hue while softening opaque, high-saturation UI blocks.
            displayColor.a = 0.86f;
        }

        if (fillImage != null)
        {
            fillImage.fillAmount = ratio;
            fillImage.color = displayColor;
        }

        string displayName = participant.DisplayName;
        if (participant.IsEliminated)
        {
            displayName = "<s>" + displayName + "</s>";
        }

        if (labelText != null)
        {
            labelText.text = displayName;
            labelText.color = participant.IsEliminated ? Color.gray : Color.white;
        }

        if (countText != null)
        {
            countText.SetText("{0} / {1}", claimed, territoryTargetCount);
            countText.color = ratio < 0.15f ? Color.white : Color.black;
        }

        PositionEmojiAfterLabel();
    }

    public void ShowEmoji(PaintOwner owner, Sprite sprite)
    {
        EnsureReferences();
        if (owner == PaintOwner.Neutral || sprite == null || emojiImage == null)
        {
            return;
        }

        emojiOwner = owner;
        if (emojiImage.sprite != sprite)
        {
            emojiImage.sprite = sprite;
        }

        if (!emojiImage.gameObject.activeSelf)
        {
            emojiImage.gameObject.SetActive(true);
        }

        if (!emojiImage.enabled)
        {
            emojiImage.enabled = true;
        }

        PositionEmojiAfterLabel();
    }

    public void HideEmoji(PaintOwner owner)
    {
        if (owner != PaintOwner.Neutral && emojiOwner != owner)
        {
            return;
        }

        HideEmojiImmediate();
    }

    private void EnsureReferences()
    {
        if (referencesResolved)
        {
            return;
        }

        if (fillImage == null)
        {
            Transform fill = transform.Find("Fill");
            fillImage = fill != null ? fill.GetComponent<Image>() : null;
        }

        if (labelText == null)
        {
            Transform label = transform.Find("Label");
            labelText = label != null ? label.GetComponent<TextMeshProUGUI>() : null;
        }

        if (countText == null)
        {
            Transform count = transform.Find("Count");
            countText = count != null ? count.GetComponent<TextMeshProUGUI>() : null;
        }

        if (emojiImage == null)
        {
            Transform emoji = transform.Find("Gameplay Emoji Icon");
            emojiImage = emoji != null ? emoji.GetComponent<Image>() : null;
        }

        Image background = GetComponent<Image>();
        if (background != null && background == fillImage)
        {
            fillImage = null;
            return;
        }

        if (background != null)
        {
            background.color = new Color(0f, 0f, 0f, 0.42f);
            background.raycastTarget = false;
        }

        referencesResolved = true;
    }

    private void PositionEmojiAfterLabel()
    {
        if (emojiImage == null ||
            labelText == null ||
            countText == null ||
            emojiOwner == PaintOwner.Neutral ||
            !emojiImage.gameObject.activeSelf)
        {
            return;
        }

        RectTransform rowRect = transform as RectTransform;
        RectTransform labelRect = labelText.rectTransform;
        RectTransform countRect = countText.rectTransform;
        RectTransform emojiRect = emojiImage.rectTransform;
        if (rowRect == null || labelRect == null || countRect == null || emojiRect == null)
        {
            return;
        }

        float rowWidth = rowRect.rect.width;
        float labelLeft = rowWidth * labelRect.anchorMin.x + labelRect.offsetMin.x;
        float countLeft = rowWidth * countRect.anchorMin.x + countRect.offsetMin.x;
        float emojiWidth = emojiRect.rect.width > 0f ? emojiRect.rect.width : emojiRect.sizeDelta.x;
        float preferredNameWidth = labelText.GetPreferredValues(
            labelText.text,
            Mathf.Max(0f, labelRect.rect.width),
            Mathf.Max(0f, labelRect.rect.height)).x;
        float maxEmojiX = Mathf.Max(labelLeft, countLeft - emojiWidth - emojiGap);
        float emojiX = Mathf.Min(labelLeft + preferredNameWidth + emojiGap, maxEmojiX);

        emojiRect.anchorMin = new Vector2(0f, 0.5f);
        emojiRect.anchorMax = new Vector2(0f, 0.5f);
        emojiRect.pivot = new Vector2(0f, 0.5f);
        emojiRect.anchoredPosition = new Vector2(emojiX, 0f);
    }

    private void HideEmojiImmediate()
    {
        emojiOwner = PaintOwner.Neutral;
        if (emojiImage == null)
        {
            return;
        }

        emojiImage.enabled = false;
        emojiImage.sprite = null;
        if (emojiImage.gameObject.activeSelf)
        {
            emojiImage.gameObject.SetActive(false);
        }
    }
}
