using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyEmojiBubble : MonoBehaviour
{
    [SerializeField] private LobbyEmojiCatalog catalog;
    [SerializeField] private GameObject bubbleRoot;
    [SerializeField] private Image emojiImage;
    [SerializeField] private float visibleSeconds = 2.2f;

    private Coroutine hideCoroutine;
    private Camera cachedCamera;

    private void Awake()
    {
        if (catalog == null)
        {
            catalog = LobbyEmojiCatalog.LoadDefault();
        }

        HideImmediate();
    }

    private void LateUpdate()
    {
        if (bubbleRoot == null || !bubbleRoot.activeSelf)
        {
            return;
        }

        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
        }

        if (cachedCamera != null)
        {
            bubbleRoot.transform.rotation = cachedCamera.transform.rotation;
        }
    }

    public void Show(string emojiId)
    {
        if (catalog == null)
        {
            catalog = LobbyEmojiCatalog.LoadDefault();
        }

        if (bubbleRoot == null ||
            emojiImage == null ||
            catalog == null ||
            !catalog.TryGetSprite(emojiId, out Sprite sprite))
        {
            return;
        }

        emojiImage.sprite = sprite;
        emojiImage.preserveAspect = true;
        bubbleRoot.SetActive(true);

        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
        }

        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    public void HideImmediate()
    {
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }

        if (bubbleRoot != null)
        {
            bubbleRoot.SetActive(false);
        }
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, visibleSeconds));
        hideCoroutine = null;
        if (bubbleRoot != null)
        {
            bubbleRoot.SetActive(false);
        }
    }
}
