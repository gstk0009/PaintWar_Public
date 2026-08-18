using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SampleMeteorStrikeButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private SampleMeteorStrikeSpawner spawner;

    private void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (button != null)
        {
            button.onClick.AddListener(SpawnMeteor);
        }
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(SpawnMeteor);
        }
    }

    private void SpawnMeteor()
    {
        if (spawner != null)
        {
            spawner.TriggerMeteorEventNow();
        }
    }
}
