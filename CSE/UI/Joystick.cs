using UnityEngine;
using UnityEngine.EventSystems;

public class Joystick : MonoBehaviour, IDragHandler, IPointerUpHandler, IPointerDownHandler
{
    public static Joystick Instance { get; private set; }

    public RectTransform joystickBG;
    public RectTransform joystickHandle;
    [SerializeField] private bool snapToEightDirections = false;
    [SerializeField] private float snapDeadZone = 0.2f;
    private Vector2 inputVector;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 pos;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(joystickBG, eventData.position, null, out pos))
        {
            float inputRadius = Mathf.Min(joystickBG.rect.width, joystickBG.rect.height) * 0.5f;
            inputVector = pos / inputRadius;
            if (inputVector.magnitude > 1f)
            {
                inputVector = inputVector.normalized;
            }

            if (snapToEightDirections)
            {
                inputVector = SnapToEightDirection(inputVector);
            }

            float handleRadius = Mathf.Max(joystickHandle.rect.width, joystickHandle.rect.height) * 0.5f;
            float travelRadius = Mathf.Max(0f, inputRadius - handleRadius);
            joystickHandle.anchoredPosition = inputVector * travelRadius;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
        => OnDrag(eventData);

    public void OnPointerUp(PointerEventData eventData)
    {
        inputVector = Vector2.zero;
        joystickHandle.anchoredPosition = Vector2.zero;
    }

    public float Horizontal => inputVector.x;
    public float Vertical => inputVector.y;

    private Vector2 SnapToEightDirection(Vector2 input)
    {
        float magnitude = Mathf.Clamp01(input.magnitude);
        if (magnitude < snapDeadZone)
        {
            return Vector2.zero;
        }

        float angle = Mathf.Atan2(input.y, input.x);
        float snappedAngle = Mathf.Round(angle / (Mathf.PI / 4f)) * (Mathf.PI / 4f);
        return new Vector2(Mathf.Cos(snappedAngle), Mathf.Sin(snappedAngle)) * magnitude;
    }
}
