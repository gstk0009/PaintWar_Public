using UnityEngine;

[DisallowMultipleComponent]
public sealed class FinalBlackHoleVfx : MonoBehaviour
{
    [SerializeField] private Transform innerRing;
    [SerializeField] private Transform outerRing;
    [SerializeField, Min(0f)] private float innerRotationSpeed = 210f;
    [SerializeField, Min(0f)] private float outerRotationSpeed = 135f;
    [SerializeField, Min(0f)] private float pulseAmount = 0.08f;
    [SerializeField, Min(0.1f)] private float pulseSpeed = 4f;

    private Vector3 baseScale = Vector3.one;
    private Camera gameplayCamera;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        baseScale = transform.localScale;
        gameplayCamera = Camera.main;
        FaceCamera();
    }

    private void Update()
    {
        float delta = Time.unscaledDeltaTime;
        if (innerRing != null)
        {
            innerRing.Rotate(Vector3.up, innerRotationSpeed * delta, Space.Self);
        }

        if (outerRing != null)
        {
            outerRing.Rotate(Vector3.up, -outerRotationSpeed * delta, Space.Self);
        }

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
        transform.localScale = baseScale * pulse;
        FaceCamera();
    }

    private void FaceCamera()
    {
        if (gameplayCamera == null)
        {
            return;
        }

        transform.rotation = Quaternion.FromToRotation(
            Vector3.up,
            -gameplayCamera.transform.forward);
    }
}
