using UnityEngine;

[DisallowMultipleComponent]
public sealed class SkillMarkerOrbitAnimator : MonoBehaviour
{
    [SerializeField] private Transform core;
    [SerializeField] private float coreDegreesPerSecond = 42f;

    private void Update()
    {
        if (core != null)
        {
            core.Rotate(
                0f,
                coreDegreesPerSecond * Time.deltaTime,
                0f,
                Space.Self);
        }
    }
}
