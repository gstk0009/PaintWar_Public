using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SampleTsunamiWaveButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private SampleTsunamiWaveSpawner spawner;

    private void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (button != null)
        {
            button.onClick.AddListener(SpawnTsunami);
        }
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(SpawnTsunami);
        }
    }

    private void SpawnTsunami()
    {
        if (spawner != null)
        {
            spawner.SpawnTsunamiAcrossMap();
        }
    }
}
