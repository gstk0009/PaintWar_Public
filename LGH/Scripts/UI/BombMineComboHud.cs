using TMPro;
using UnityEngine;

/// <summary>
/// Displays the last bomb-mine chain combo started by the local player.
/// Scene layout and text styling are authored in the Unity scene.
/// </summary>
public sealed class BombMineComboHud : MonoBehaviour
{
    [SerializeField] private TMP_Text comboText;
    [SerializeField, Min(0.1f)] private float visibleSeconds = 2f;
    [SerializeField] private bool hideSingleExplosion = false;

    private float hideAt;
    private int displayedCombo = int.MinValue;

    private void OnEnable()
    {
        BombMine.ChainResolved += OnBombMineChainResolved;
        HideImmediate();
    }

    private void OnDisable()
    {
        BombMine.ChainResolved -= OnBombMineChainResolved;
    }

    private void Update()
    {
        if (comboText == null ||
            !comboText.gameObject.activeSelf ||
            Time.unscaledTime < hideAt)
        {
            return;
        }

        comboText.gameObject.SetActive(false);
    }

    private void OnBombMineChainResolved(
        PaintOwner owner,
        int comboDepth,
        Vector2Int rootIndex)
    {
        if (comboText == null ||
            !PlayerStoredItemController.TryGetLocalPaintOwner(out PaintOwner localOwner) ||
            owner != localOwner ||
            (hideSingleExplosion && comboDepth <= 1))
        {
            return;
        }

        if (displayedCombo != comboDepth)
        {
            comboText.text = "Mine Combo x" + comboDepth;
            displayedCombo = comboDepth;
        }

        hideAt = Time.unscaledTime + visibleSeconds;
        comboText.gameObject.SetActive(true);
    }

    private void HideImmediate()
    {
        displayedCombo = int.MinValue;
        hideAt = 0f;
        if (comboText != null)
        {
            comboText.gameObject.SetActive(false);
        }
    }
}
