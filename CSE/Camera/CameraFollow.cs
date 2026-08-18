using FishNet.Object;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private bool autoFindLocalPlayer = true;
    [SerializeField] private Vector3 offset = new Vector3(0f, 9f, -11.7f);

    [Header("Follow")]
    [SerializeField] private float followSharpness = 8f;
    [SerializeField] private float lookAheadDistance = 0f;
    [SerializeField] private float lookAheadSharpness = 5f;

    [Header("Speed Feel")]
    [SerializeField] private bool enableSpeedFeel = false;
    [SerializeField] private float baseFieldOfView = 60f;
    [SerializeField] private float speedFieldOfView = 0f;
    [SerializeField] private float fieldOfViewSharpness = 4f;
    [SerializeField] private float maxMeasuredSpeed = 13f;
    [SerializeField] private float maxTiltAngle = 0f;
    [SerializeField] private float tiltSharpness = 5f;
    [SerializeField] private float speedShakeAmount = 0f;
    [SerializeField] private float speedShakeFrequency = 16f;
    [SerializeField] private float burstFieldOfView = 0f;
    [SerializeField] private float burstShakeAmount = 0f;
    [SerializeField] private float burstDecaySharpness = 5f;

    [Header("Bounds")]
    [SerializeField] private bool clampToTileMap = true;
    [SerializeField] private float mapPadding = 2f;
    [SerializeField] private bool includeOffsetInBounds = true;

    private Camera followCamera;
    private Quaternion fixedRotation;
    private Vector3 smoothedLookAhead;
    private Vector3 previousTargetPosition;
    private Vector3 targetVelocity;
    private float nextTargetSearchTime;
    private float currentTilt;
    private float burstAmount;

    public Transform Target { get { return target; } }

    private void Awake()
    {
        followCamera = GetComponent<Camera>();
        fixedRotation = transform.rotation;
        if (followCamera != null)
        {
            baseFieldOfView = followCamera.fieldOfView;
        }
    }

    private void Start()
    {
        if (target == null && autoFindLocalPlayer)
        {
            TryFindLocalPlayer();
        }

        if (target != null)
        {
            previousTargetPosition = target.position;
            SnapToTarget();
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            if (autoFindLocalPlayer && Time.unscaledTime >= nextTargetSearchTime)
            {
                nextTargetSearchTime = Time.unscaledTime + 0.25f;
                TryFindLocalPlayer();
            }

            return;
        }

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        targetVelocity = (target.position - previousTargetPosition) / deltaTime;
        previousTargetPosition = target.position;

        Vector3 flatVelocity = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
        Vector3 desiredLookAhead = lookAheadDistance > 0f && flatVelocity.sqrMagnitude > 0.01f
            ? flatVelocity.normalized * lookAheadDistance
            : Vector3.zero;
        smoothedLookAhead = Vector3.Lerp(smoothedLookAhead, desiredLookAhead, GetSharpnessT(lookAheadSharpness));

        Vector3 desiredPosition = target.position + offset + smoothedLookAhead;
        if (enableSpeedFeel)
        {
            desiredPosition += GetSpeedShake(flatVelocity.magnitude);
        }

        desiredPosition = ClampPosition(desiredPosition);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, GetSharpnessT(followSharpness));

        if (enableSpeedFeel)
        {
            UpdateRotation(flatVelocity);
            UpdateFieldOfView(flatVelocity.magnitude);
            burstAmount = Mathf.Lerp(burstAmount, 0f, GetSharpnessT(burstDecaySharpness));
        }
        else
        {
            transform.rotation = fixedRotation;
            if (followCamera != null)
            {
                followCamera.fieldOfView = baseFieldOfView;
            }
        }
    }

    public void SetTarget(Transform newTarget, bool snap)
    {
        target = newTarget;
        if (target == null)
        {
            return;
        }

        previousTargetPosition = target.position;
        smoothedLookAhead = Vector3.zero;
        if (snap)
        {
            SnapToTarget();
        }
    }

    public void ClearTarget(Transform oldTarget)
    {
        if (target == oldTarget)
        {
            target = null;
        }
    }

    public void PlaySpeedBurst(float intensity)
    {
        if (!enableSpeedFeel)
        {
            return;
        }

        burstAmount = Mathf.Clamp01(Mathf.Max(burstAmount, intensity));
    }

    private void SnapToTarget()
    {
        transform.position = ClampPosition(target.position + offset);
        transform.rotation = fixedRotation;
    }

    private void TryFindLocalPlayer()
    {
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && IsLocalPlayer(players[i]))
            {
                SetTarget(players[i].transform, true);
                return;
            }
        }
    }

    private bool IsLocalPlayer(PlayerMovement player)
    {
        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        return networkObject != null && networkObject.IsOwner;
    }

    private void UpdateRotation(Vector3 flatVelocity)
    {
        float speedRatio = Mathf.Clamp01(flatVelocity.magnitude / Mathf.Max(0.01f, maxMeasuredSpeed));
        Vector3 localVelocity = transform.InverseTransformDirection(flatVelocity);
        float desiredTilt = -Mathf.Clamp(localVelocity.x / Mathf.Max(0.01f, maxMeasuredSpeed), -1f, 1f) * maxTiltAngle * speedRatio;
        currentTilt = Mathf.Lerp(currentTilt, desiredTilt, GetSharpnessT(tiltSharpness));
        transform.rotation = fixedRotation * Quaternion.Euler(0f, 0f, currentTilt);
    }

    private void UpdateFieldOfView(float speed)
    {
        if (followCamera == null)
        {
            return;
        }

        float speedRatio = Mathf.Clamp01(speed / Mathf.Max(0.01f, maxMeasuredSpeed));
        float targetFieldOfView = baseFieldOfView + speedFieldOfView * speedRatio + burstFieldOfView * burstAmount;
        followCamera.fieldOfView = Mathf.Lerp(followCamera.fieldOfView, targetFieldOfView, GetSharpnessT(fieldOfViewSharpness));
    }

    private Vector3 GetSpeedShake(float speed)
    {
        float speedRatio = Mathf.Clamp01(speed / Mathf.Max(0.01f, maxMeasuredSpeed));
        float amount = speedShakeAmount * speedRatio + burstShakeAmount * burstAmount;
        if (amount <= 0.001f)
        {
            return Vector3.zero;
        }

        float time = Time.time * speedShakeFrequency;
        return new Vector3(
            (Mathf.PerlinNoise(time, 0.13f) - 0.5f) * amount,
            (Mathf.PerlinNoise(0.37f, time) - 0.5f) * amount,
            0f);
    }

    private Vector3 ClampPosition(Vector3 position)
    {
        if (!clampToTileMap || !TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return position;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        float offsetMarginX = includeOffsetInBounds ? Mathf.Abs(offset.x) : 0f;
        float offsetMarginZ = includeOffsetInBounds ? Mathf.Abs(offset.z) : 0f;
        float minX = tileManager.GridOrigin.x - mapPadding - offsetMarginX;
        float minZ = tileManager.GridOrigin.z - mapPadding - offsetMarginZ;
        float maxX = tileManager.GridOrigin.x + tileManager.GridSize * tileManager.TileSize + mapPadding + offsetMarginX;
        float maxZ = tileManager.GridOrigin.z + tileManager.GridSize * tileManager.TileSize + mapPadding + offsetMarginZ;
        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.z = Mathf.Clamp(position.z, minZ, maxZ);
        return position;
    }

    private float GetSharpnessT(float sharpness)
    {
        return 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * Time.deltaTime);
    }
}
