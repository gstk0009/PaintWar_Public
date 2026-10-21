using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 파밍 구역 경계의 시각 요소와 차단 콜라이더만 생성합니다.
// 라운드 규칙은 GameRoundManager에 남겨두며, 서버 빌드에서는 이 뷰를 교체하거나 끌 수 있습니다.
public class FarmingBoundaryView : MonoBehaviour
{
    [SerializeField] private bool showBoundary = true;
    [SerializeField] private bool showGlassPane = false;
    [SerializeField] private float boundaryHeight = 5.2f;
    [SerializeField] private float boundaryThickness = 0.045f;
    [SerializeField] private float boundaryColliderThickness = 0.5f;
    [SerializeField] private float boundaryBaseMarkerHeight = 0.055f;
    [SerializeField] private float boundaryBaseMarkerWidth = 0.62f;
    [SerializeField] private float boundaryBaseMarkerSurfaceOffset = 0.5f;
    [SerializeField] private float boundaryBaseMarkerSurfacePadding = 0.018f;
    [SerializeField] private Material boundaryMarkerMaterial;
    [SerializeField] private Material boundaryBaseMaterial;
    [SerializeField] private Material glassPaneMaterial;
    [SerializeField, Min(0.05f)] private float releaseEffectSeconds = 0.22f;

    private GameObject boundaryRoot;
    private readonly Stack<GameObject> visualPool = new Stack<GameObject>();
    private readonly Stack<GameObject> colliderPool = new Stack<GameObject>();
    private readonly List<GameObject> activeVisuals = new List<GameObject>();
    private readonly List<GameObject> activeColliders = new List<GameObject>();
    private Coroutine releaseRoutine;
    private bool isVisible;

    // 기존 경계 오브젝트를 지우고 새 경계 선분 목록에 맞춰 벽, 콜라이더, 강조선을 다시 만듭니다.
    public void Rebuild(IReadOnlyList<BoundarySegment> segments, TilePaintManager tileManager)
    {
        Clear();

        if (!showBoundary || tileManager == null || segments == null || segments.Count == 0)
        {
            return;
        }

        if (boundaryRoot == null)
        {
            boundaryRoot = new GameObject("Farming Boundary Visuals");
            boundaryRoot.transform.SetParent(transform, false);
        }
        boundaryRoot.SetActive(true);
        isVisible = true;

        for (int i = 0; i < segments.Count; i++)
        {
            CreateBoundaryWall(segments[i], tileManager);
        }
    }

    // 파밍 페이즈 여부에 따라 경계 루트 오브젝트의 표시 상태를 바꿉니다.
    public void SetVisible(bool isVisible)
    {
        if (boundaryRoot == null)
        {
            this.isVisible = isVisible;
            return;
        }

        if (this.isVisible == isVisible) return;
        this.isVisible = isVisible;
        if (releaseRoutine != null)
        {
            StopCoroutine(releaseRoutine);
            releaseRoutine = null;
        }

        if (showBoundary && isVisible)
        {
            boundaryRoot.SetActive(true);
            return;
        }

        for (int i = 0; i < activeColliders.Count; i++)
            if (activeColliders[i] != null) activeColliders[i].SetActive(false);
        if (boundaryRoot.activeSelf && activeVisuals.Count > 0 && gameObject.activeInHierarchy)
            releaseRoutine = StartCoroutine(PlayReleaseEffect());
        else
            boundaryRoot.SetActive(false);
    }

    private System.Collections.IEnumerator PlayReleaseEffect()
    {
        float elapsed = 0f;
        Vector3[] startScales = new Vector3[activeVisuals.Count];
        for (int i = 0; i < activeVisuals.Count; i++)
            startScales[i] = activeVisuals[i] != null ? activeVisuals[i].transform.localScale : Vector3.one;
        while (elapsed < releaseEffectSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float scale = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / releaseEffectSeconds));
            for (int i = 0; i < activeVisuals.Count; i++)
            {
                if (activeVisuals[i] == null) continue;
                Vector3 value = startScales[i];
                value.y *= scale;
                activeVisuals[i].transform.localScale = value;
            }
            yield return null;
        }
        boundaryRoot.SetActive(false);
        releaseRoutine = null;
    }

    // 런타임에 만든 경계 오브젝트를 제거합니다.
    private void Clear()
    {
        if (releaseRoutine != null)
        {
            StopCoroutine(releaseRoutine);
            releaseRoutine = null;
        }
        for (int i = 0; i < activeVisuals.Count; i++)
        {
            GameObject visual = activeVisuals[i];
            if (visual == null) continue;
            visual.SetActive(false);
            visualPool.Push(visual);
        }
        activeVisuals.Clear();

        for (int i = 0; i < activeColliders.Count; i++)
        {
            GameObject colliderObject = activeColliders[i];
            if (colliderObject == null) continue;
            colliderObject.SetActive(false);
            colliderPool.Push(colliderObject);
        }
        activeColliders.Clear();
    }

    // 경계 선분 하나를 시각 벽으로 만들고 이동 차단 콜라이더와 상하단 강조선을 함께 생성합니다.
    private void CreateBoundaryWall(BoundarySegment segment, TilePaintManager tileManager)
    {
        Vector3 start = FarmingZone.GridPointToWorld(segment.Start, tileManager);
        Vector3 end = FarmingZone.GridPointToWorld(segment.End, tileManager);
        Vector3 direction = end - start;
        float length = direction.magnitude;
        if (length <= 0.001f)
        {
            return;
        }

        CreateBlockingCollider(segment, tileManager);
        if (Application.isBatchMode)
        {
            return;
        }

        if (showGlassPane)
        {
            CreateGlassPane(start, end, direction.normalized, length);
        }

        CreateBoundaryMarker(start, end, direction.normalized, length);
        CreateBaseMarker(start, end, direction.normalized, length);
    }

    private void CreateBoundaryMarker(Vector3 start, Vector3 end, Vector3 forward, float length)
    {
        Vector3 midpoint = (start + end) * 0.5f;
        midpoint.y = boundaryHeight * 0.5f;

        GameObject marker = RentVisual();
        marker.name = "Farming Boundary Marker";
        marker.transform.SetParent(boundaryRoot.transform, false);
        marker.transform.position = midpoint;
        marker.transform.rotation = Quaternion.LookRotation(forward);
        marker.transform.localScale = new Vector3(boundaryColliderThickness, boundaryHeight, length);

        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            markerCollider.enabled = false;
        }

        Renderer markerRenderer = marker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            ApplyRendererMaterial(markerRenderer, boundaryMarkerMaterial);
        }
    }

    private void CreateBaseMarker(Vector3 start, Vector3 end, Vector3 forward, float length)
    {
        Vector3 midpoint = (start + end) * 0.5f;
        midpoint.y = start.y + boundaryBaseMarkerSurfaceOffset + boundaryBaseMarkerHeight * 0.5f + boundaryBaseMarkerSurfacePadding;

        GameObject marker = RentVisual();
        marker.name = "Farming Boundary Base Marker";
        marker.transform.SetParent(boundaryRoot.transform, false);
        marker.transform.position = midpoint;
        marker.transform.rotation = Quaternion.LookRotation(forward);
        marker.transform.localScale = new Vector3(boundaryBaseMarkerWidth, boundaryBaseMarkerHeight, length);

        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            markerCollider.enabled = false;
        }

        Renderer markerRenderer = marker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            ApplyRendererMaterial(markerRenderer, boundaryBaseMaterial);
        }
    }

    private void CreateGlassPane(Vector3 start, Vector3 end, Vector3 forward, float length)
    {
        Vector3 midpoint = (start + end) * 0.5f;
        midpoint.y = boundaryHeight * 0.5f;

        GameObject wall = RentVisual();
        wall.name = "Farming Boundary Glass Pane";
        wall.transform.SetParent(boundaryRoot.transform, false);
        wall.transform.position = midpoint;
        wall.transform.rotation = Quaternion.LookRotation(forward);
        wall.transform.localScale = new Vector3(boundaryThickness, boundaryHeight, length);

        Collider wallCollider = wall.GetComponent<Collider>();
        if (wallCollider != null)
        {
            wallCollider.enabled = false;
        }

        Renderer wallRenderer = wall.GetComponent<Renderer>();
        if (wallRenderer != null)
        {
            ApplyRendererMaterial(wallRenderer, glassPaneMaterial);
        }
    }

    // 플레이어가 파밍 구역 경계를 넘어가지 못하게 보이지 않는 두꺼운 콜라이더를 생성합니다.
    private void CreateBlockingCollider(BoundarySegment segment, TilePaintManager tileManager)
    {
        Vector3 start = FarmingZone.GridPointToWorld(segment.Start, tileManager);
        Vector3 end = FarmingZone.GridPointToWorld(segment.End, tileManager);
        Vector3 direction = end - start;
        float length = direction.magnitude;
        if (length <= 0.001f)
        {
            return;
        }

        Vector3 midpoint = (start + end) * 0.5f;
        midpoint.y = boundaryHeight * 0.5f;

        GameObject colliderObject = RentCollider();
        colliderObject.name = "Farming Boundary Collider";
        colliderObject.transform.SetParent(boundaryRoot.transform, false);
        colliderObject.transform.position = midpoint;
        colliderObject.transform.rotation = Quaternion.LookRotation(direction.normalized);
        colliderObject.transform.localScale = new Vector3(boundaryColliderThickness, boundaryHeight, length);
    }

    private static void ApplyRendererMaterial(Renderer targetRenderer, Material material)
    {
        targetRenderer.enabled = material != null;
        targetRenderer.sharedMaterial = material;
        targetRenderer.shadowCastingMode = ShadowCastingMode.Off;
        targetRenderer.receiveShadows = false;
    }

    private GameObject RentVisual()
    {
        GameObject visual = visualPool.Count > 0
            ? visualPool.Pop()
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.SetActive(true);
        activeVisuals.Add(visual);
        return visual;
    }

    private GameObject RentCollider()
    {
        GameObject colliderObject;
        if (colliderPool.Count > 0)
        {
            colliderObject = colliderPool.Pop();
        }
        else
        {
            colliderObject = new GameObject("Farming Boundary Collider");
            colliderObject.AddComponent<BoxCollider>();
        }

        colliderObject.SetActive(true);
        activeColliders.Add(colliderObject);
        return colliderObject;
    }
}
