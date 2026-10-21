using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Offline comparison harness for joystick direction filtering. The authored test scene
// supplies the real player prefab, joystick, and UI; this component only binds input
// and updates values which change during play.
public sealed class PlayerMovementFeelTestController : MonoBehaviour
{
    private static readonly int IsRunHash = Animator.StringToHash("isRun");

    [Header("Authored Scene References")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Animator animator;
    [SerializeField] private Joystick joystick;
    [SerializeField] private Button fourDirectionButton;
    [SerializeField] private Button eightDirectionButton;
    [SerializeField] private Button freeDirectionButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private TMP_Text selectedModeLabel;
    [SerializeField] private TMP_Text telemetryLabel;

    [Header("Shared Comparison Settings")]
    [SerializeField, Min(0f)] private float moveSpeed = 7f;
    [SerializeField, Range(0f, 0.9f)] private float radialDeadZone = 0.16f;
    [SerializeField, Range(0f, 20f)] private float directionHysteresisDegrees = 7f;
    [SerializeField, Min(0f)] private float rotationSharpness = 12f;
    [SerializeField] private Vector3 resetPosition = new Vector3(0f, 0.12f, 0f);

    [Header("Initial Mode")]
    [SerializeField] private MovementDirectionMode initialMode = MovementDirectionMode.EightDirections;

    private MovementDirectionMode mode;
    private int retainedSector;
    private bool hasRetainedSector;
    private Vector2 lastRawInput;
    private Vector2 lastFilteredInput;
    private int directionChangeCount;
    private float nextTelemetryRefreshAt;

    private void OnEnable()
    {
        fourDirectionButton.onClick.AddListener(SelectFourDirections);
        eightDirectionButton.onClick.AddListener(SelectEightDirections);
        freeDirectionButton.onClick.AddListener(SelectFreeDirection);
        resetButton.onClick.AddListener(ResetPlayer);
        SetMode(initialMode);
        ResetPlayer();
    }

    private void OnDisable()
    {
        fourDirectionButton.onClick.RemoveListener(SelectFourDirections);
        eightDirectionButton.onClick.RemoveListener(SelectEightDirections);
        freeDirectionButton.onClick.RemoveListener(SelectFreeDirection);
        resetButton.onClick.RemoveListener(ResetPlayer);
    }

    private void Update()
    {
        HandleKeyboardShortcuts();

        Vector2 rawInput = ReadInput();
        Vector2 deadZoneInput = ApplyRadialDeadZone(rawInput);
        Vector2 filteredInput = FilterDirection(deadZoneInput);

        if (filteredInput.sqrMagnitude > 0.0001f)
        {
            Vector3 direction = GetCameraRelativeDirection(filteredInput);
            Vector3 before = playerRoot.position;
            characterController.Move(direction * (moveSpeed * Time.deltaTime));

            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            float blend = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
            playerRoot.rotation = Quaternion.Slerp(playerRoot.rotation, targetRotation, blend);
            animator.SetBool(IsRunHash, (playerRoot.position - before).sqrMagnitude > 0.000001f);
        }
        else
        {
            animator.SetBool(IsRunHash, false);
        }

        if (lastFilteredInput.sqrMagnitude > 0.01f && filteredInput.sqrMagnitude > 0.01f &&
            Vector2.Angle(lastFilteredInput, filteredInput) > 1f)
        {
            directionChangeCount++;
        }

        lastRawInput = rawInput;
        lastFilteredInput = filteredInput;
        if (Time.unscaledTime >= nextTelemetryRefreshAt)
        {
            nextTelemetryRefreshAt = Time.unscaledTime + 0.1f;
            UpdateTelemetry();
        }
    }

    private Vector2 ReadInput()
    {
        Vector2 joystickInput = new Vector2(joystick.Horizontal, joystick.Vertical);
        if (joystickInput.sqrMagnitude > 0.0001f)
        {
            return Vector2.ClampMagnitude(joystickInput, 1f);
        }

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                  (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
                  (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
        return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
#else
        return Vector2.ClampMagnitude(
            new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
            1f);
#endif
    }

    private Vector2 ApplyRadialDeadZone(Vector2 input)
    {
        float magnitude = input.magnitude;
        if (magnitude <= radialDeadZone)
        {
            return Vector2.zero;
        }

        float rescaledMagnitude = Mathf.InverseLerp(radialDeadZone, 1f, magnitude);
        return input * (rescaledMagnitude / magnitude);
    }

    private Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        Vector3 screenRight = cameraTransform.right;
        screenRight.y = 0f;
        screenRight.Normalize();

        // Project the camera's visual up axis onto the floor. In a quarter-view camera
        // this is diagonal in world space, but it is straight up on the player's screen.
        Vector3 screenUp = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
        screenUp.Normalize();

        Vector3 worldDirection = screenRight * input.x + screenUp * input.y;
        return worldDirection.sqrMagnitude > 0.0001f
            ? worldDirection.normalized * input.magnitude
            : Vector3.zero;
    }

    private Vector2 FilterDirection(Vector2 input)
    {
        if (input.sqrMagnitude <= 0.0001f)
        {
            hasRetainedSector = false;
            return Vector2.zero;
        }

        if (mode == MovementDirectionMode.Free)
        {
            hasRetainedSector = false;
            return input;
        }

        int sectorCount = mode == MovementDirectionMode.FourDirections ? 4 : 8;
        float sectorSize = 360f / sectorCount;
        float angle = Mathf.Repeat(Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg, 360f);
        int nearestSector = Mathf.RoundToInt(angle / sectorSize) % sectorCount;

        if (!hasRetainedSector)
        {
            retainedSector = nearestSector;
            hasRetainedSector = true;
        }
        else
        {
            float retainedAngle = retainedSector * sectorSize;
            float distanceFromRetained = Mathf.Abs(Mathf.DeltaAngle(retainedAngle, angle));
            if (distanceFromRetained > sectorSize * 0.5f + directionHysteresisDegrees)
            {
                retainedSector = nearestSector;
            }
        }

        float snappedRadians = retainedSector * sectorSize * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(snappedRadians), Mathf.Sin(snappedRadians));
        return direction * input.magnitude;
    }

    private void HandleKeyboardShortcuts()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.digit1Key.wasPressedThisFrame) SelectFourDirections();
        if (keyboard.digit2Key.wasPressedThisFrame) SelectEightDirections();
        if (keyboard.digit3Key.wasPressedThisFrame) SelectFreeDirection();
        if (keyboard.rKey.wasPressedThisFrame) ResetPlayer();
#else
        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectFourDirections();
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectEightDirections();
        if (Input.GetKeyDown(KeyCode.Alpha3)) SelectFreeDirection();
        if (Input.GetKeyDown(KeyCode.R)) ResetPlayer();
#endif
    }

    private void SelectFourDirections() => SetMode(MovementDirectionMode.FourDirections);
    private void SelectEightDirections() => SetMode(MovementDirectionMode.EightDirections);
    private void SelectFreeDirection() => SetMode(MovementDirectionMode.Free);

    private void SetMode(MovementDirectionMode nextMode)
    {
        mode = nextMode;
        hasRetainedSector = false;
        directionChangeCount = 0;

        string modeName = mode == MovementDirectionMode.FourDirections
            ? "4 DIRECTION"
            : mode == MovementDirectionMode.EightDirections ? "8 DIRECTION" : "FREE ANALOG";
        selectedModeLabel.text = "SELECTED: " + modeName;

        SetButtonInteractable(fourDirectionButton, mode != MovementDirectionMode.FourDirections);
        SetButtonInteractable(eightDirectionButton, mode != MovementDirectionMode.EightDirections);
        SetButtonInteractable(freeDirectionButton, mode != MovementDirectionMode.Free);
    }

    private static void SetButtonInteractable(Button button, bool interactable)
    {
        button.interactable = interactable;
    }

    private void ResetPlayer()
    {
        characterController.enabled = false;
        playerRoot.SetPositionAndRotation(resetPosition, Quaternion.identity);
        characterController.enabled = true;
        animator.SetBool(IsRunHash, false);
        directionChangeCount = 0;
        hasRetainedSector = false;
        nextTelemetryRefreshAt = 0f;
    }

    private void UpdateTelemetry()
    {
        telemetryLabel.text =
            $"RAW  {lastRawInput.x:+0.00;-0.00;0.00}, {lastRawInput.y:+0.00;-0.00;0.00}" +
            $"    OUTPUT  {lastFilteredInput.x:+0.00;-0.00;0.00}, {lastFilteredInput.y:+0.00;-0.00;0.00}" +
            $"    DIRECTION CHANGES  {directionChangeCount}";
    }
}
