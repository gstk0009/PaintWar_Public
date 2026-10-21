using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CSE
{
    public sealed class FootstepAudioTestController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Transform avatarRoot;
        [SerializeField] private Transform avatarVisual;
        [SerializeField] private AudioSource movementAudioSource;
        [SerializeField] private TMP_Text selectedCandidateLabel;
        [SerializeField] private TMP_Text playbackStatsLabel;
        [SerializeField] private Button[] candidateButtons;
        [SerializeField] private Renderer[] tileRenderers;

        [Header("Candidates")]
        [SerializeField] private AudioClip[] candidateClips;
        [SerializeField] private string[] candidateNames;
        [SerializeField] private Color[] candidatePaintColors;

        [Header("Game-Matched Movement")]
        [SerializeField] private int gridSize = 11;
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private float moveSpeed = 8.5f;
        [SerializeField] private float stepVolume = 0.86f;
        [SerializeField] private float pitchVariation = 0.03f;

        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock tileProperties;
        private Vector3 stepStartPosition;
        private Vector3 stepTargetPosition;
        private bool isStepping;
        private int selectedCandidate;
        private int playedCount;
        private int skippedOverlapCount;
        private float bobTime;

        private void Awake()
        {
            tileProperties = new MaterialPropertyBlock();
            BindButtons();
            SelectCandidate(0);
            PaintCurrentTile();
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }

        private void Update()
        {
            HandleCandidateShortcuts();

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                ResetTest();
            }

            if (isStepping)
            {
                UpdateGridStep();
            }
            else
            {
                Vector2Int direction = ReadHeldDirection();
                if (direction != Vector2Int.zero)
                {
                    TryBeginGridStep(direction);
                }
            }

            UpdateAvatarBob();
        }

        public void SelectCurrent()
        {
            SelectCandidate(0);
        }

        public void SelectCandidateA()
        {
            SelectCandidate(1);
        }

        public void SelectCandidateB()
        {
            SelectCandidate(2);
        }

        public void SelectCandidateC()
        {
            SelectCandidate(3);
        }

        public void SelectCandidateD()
        {
            SelectCandidate(4);
        }

        public void SelectCandidateE()
        {
            SelectCandidate(5);
        }

        public void SelectCandidateF()
        {
            SelectCandidate(6);
        }

        public void SelectCandidateG()
        {
            SelectCandidate(7);
        }

        private void BindButtons()
        {
            if (candidateButtons == null)
            {
                return;
            }

            if (candidateButtons.Length > 0) candidateButtons[0].onClick.AddListener(SelectCurrent);
            if (candidateButtons.Length > 1) candidateButtons[1].onClick.AddListener(SelectCandidateA);
            if (candidateButtons.Length > 2) candidateButtons[2].onClick.AddListener(SelectCandidateB);
            if (candidateButtons.Length > 3) candidateButtons[3].onClick.AddListener(SelectCandidateC);
            if (candidateButtons.Length > 4) candidateButtons[4].onClick.AddListener(SelectCandidateD);
            if (candidateButtons.Length > 5) candidateButtons[5].onClick.AddListener(SelectCandidateE);
            if (candidateButtons.Length > 6) candidateButtons[6].onClick.AddListener(SelectCandidateF);
            if (candidateButtons.Length > 7) candidateButtons[7].onClick.AddListener(SelectCandidateG);
        }

        private void UnbindButtons()
        {
            if (candidateButtons == null)
            {
                return;
            }

            if (candidateButtons.Length > 0) candidateButtons[0].onClick.RemoveListener(SelectCurrent);
            if (candidateButtons.Length > 1) candidateButtons[1].onClick.RemoveListener(SelectCandidateA);
            if (candidateButtons.Length > 2) candidateButtons[2].onClick.RemoveListener(SelectCandidateB);
            if (candidateButtons.Length > 3) candidateButtons[3].onClick.RemoveListener(SelectCandidateC);
            if (candidateButtons.Length > 4) candidateButtons[4].onClick.RemoveListener(SelectCandidateD);
            if (candidateButtons.Length > 5) candidateButtons[5].onClick.RemoveListener(SelectCandidateE);
            if (candidateButtons.Length > 6) candidateButtons[6].onClick.RemoveListener(SelectCandidateF);
            if (candidateButtons.Length > 7) candidateButtons[7].onClick.RemoveListener(SelectCandidateG);
        }

        private void HandleCandidateShortcuts()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
            {
                SelectCandidate(0);
            }
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
            {
                SelectCandidate(1);
            }
            else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
            {
                SelectCandidate(2);
            }
            else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame)
            {
                SelectCandidate(3);
            }
            else if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame)
            {
                SelectCandidate(4);
            }
            else if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame)
            {
                SelectCandidate(5);
            }
            else if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame)
            {
                SelectCandidate(6);
            }
            else if (keyboard.digit8Key.wasPressedThisFrame || keyboard.numpad8Key.wasPressedThisFrame)
            {
                SelectCandidate(7);
            }
        }

        private Vector2Int ReadHeldDirection()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2Int.zero;
            }

            int horizontal = 0;
            int vertical = 0;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                horizontal--;
            }
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                horizontal++;
            }
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                vertical--;
            }
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                vertical++;
            }

            if (horizontal != 0)
            {
                return new Vector2Int(horizontal, 0);
            }

            return vertical != 0 ? new Vector2Int(0, vertical) : Vector2Int.zero;
        }

        private void TryBeginGridStep(Vector2Int direction)
        {
            if (avatarRoot == null)
            {
                return;
            }

            Vector3 candidateTarget = avatarRoot.position +
                                      new Vector3(direction.x * tileSize, 0f, direction.y * tileSize);
            int halfGrid = gridSize / 2;
            int targetX = Mathf.RoundToInt(candidateTarget.x / tileSize);
            int targetZ = Mathf.RoundToInt(candidateTarget.z / tileSize);
            if (targetX < -halfGrid || targetX > halfGrid ||
                targetZ < -halfGrid || targetZ > halfGrid)
            {
                return;
            }

            stepStartPosition = avatarRoot.position;
            stepTargetPosition = candidateTarget;
            isStepping = true;
            avatarRoot.forward = new Vector3(direction.x, 0f, direction.y);
            PlayGameMatchedStep();
        }

        private void UpdateGridStep()
        {
            avatarRoot.position = Vector3.MoveTowards(
                avatarRoot.position,
                stepTargetPosition,
                moveSpeed * Time.deltaTime);

            if ((avatarRoot.position - stepTargetPosition).sqrMagnitude > 0.000004f)
            {
                return;
            }

            avatarRoot.position = stepTargetPosition;
            isStepping = false;
            PaintCurrentTile();
        }

        private void PlayGameMatchedStep()
        {
            AudioClip clip = GetSelectedClip();
            if (movementAudioSource == null || clip == null)
            {
                return;
            }

            if (movementAudioSource.isPlaying)
            {
                skippedOverlapCount++;
                UpdateStatsLabel();
                return;
            }

            movementAudioSource.clip = clip;
            movementAudioSource.pitch = Random.Range(
                1f - pitchVariation,
                1f + pitchVariation);
            movementAudioSource.volume = stepVolume *
                                         Mathf.Pow(10f, Random.Range(-1.5f, 1.5f) / 20f);
            movementAudioSource.Play();
            playedCount++;
            UpdateStatsLabel();
        }

        private void SelectCandidate(int index)
        {
            int candidateCount = candidateClips != null ? candidateClips.Length : 0;
            selectedCandidate = Mathf.Clamp(index, 0, Mathf.Max(0, candidateCount - 1));
            playedCount = 0;
            skippedOverlapCount = 0;

            if (movementAudioSource != null)
            {
                movementAudioSource.Stop();
            }

            if (selectedCandidateLabel != null)
            {
                selectedCandidateLabel.text = "SELECTED: " + GetSelectedName();
            }

            UpdateStatsLabel();
        }

        private AudioClip GetSelectedClip()
        {
            if (candidateClips == null ||
                selectedCandidate < 0 ||
                selectedCandidate >= candidateClips.Length)
            {
                return null;
            }

            return candidateClips[selectedCandidate];
        }

        private string GetSelectedName()
        {
            if (candidateNames == null ||
                selectedCandidate < 0 ||
                selectedCandidate >= candidateNames.Length)
            {
                return "알 수 없음";
            }

            return candidateNames[selectedCandidate];
        }

        private Color GetSelectedPaintColor()
        {
            if (candidatePaintColors == null ||
                selectedCandidate < 0 ||
                selectedCandidate >= candidatePaintColors.Length)
            {
                return Color.white;
            }

            return candidatePaintColors[selectedCandidate];
        }

        private void PaintCurrentTile()
        {
            if (avatarRoot == null || tileRenderers == null || gridSize <= 0)
            {
                return;
            }

            int halfGrid = gridSize / 2;
            int x = Mathf.RoundToInt(avatarRoot.position.x / tileSize) + halfGrid;
            int z = Mathf.RoundToInt(avatarRoot.position.z / tileSize) + halfGrid;
            int index = z * gridSize + x;
            if (index < 0 || index >= tileRenderers.Length || tileRenderers[index] == null)
            {
                return;
            }

            Color paintColor = GetSelectedPaintColor();
            Renderer tileRenderer = tileRenderers[index];
            tileRenderer.GetPropertyBlock(tileProperties);
            tileProperties.SetColor(BaseColorProperty, paintColor);
            tileProperties.SetColor(ColorProperty, paintColor);
            tileRenderer.SetPropertyBlock(tileProperties);
        }

        private void UpdateAvatarBob()
        {
            if (avatarVisual == null)
            {
                return;
            }

            if (!isStepping)
            {
                bobTime = 0f;
                avatarVisual.localPosition = Vector3.zero;
                return;
            }

            float stepDistance = Mathf.Max(tileSize, 0.001f);
            float progress = Vector3.Distance(stepStartPosition, avatarRoot.position) / stepDistance;
            bobTime += Time.deltaTime;
            avatarVisual.localPosition = Vector3.up * (Mathf.Sin(progress * Mathf.PI) * 0.13f);
        }

        private void UpdateStatsLabel()
        {
            if (playbackStatsLabel != null)
            {
                playbackStatsLabel.text =
                    "PLAYED " + playedCount +
                    "  |  SKIPPED WHILE BUSY " + skippedOverlapCount;
            }
        }

        private void ResetTest()
        {
            isStepping = false;
            if (avatarRoot != null)
            {
                avatarRoot.position = Vector3.zero;
                avatarRoot.rotation = Quaternion.identity;
            }

            if (movementAudioSource != null)
            {
                movementAudioSource.Stop();
            }

            playedCount = 0;
            skippedOverlapCount = 0;
            ResetTileColors();
            PaintCurrentTile();
            UpdateStatsLabel();
        }

        private void ResetTileColors()
        {
            if (tileRenderers == null)
            {
                return;
            }

            Color neutral = new Color(0.93f, 0.94f, 0.96f, 1f);
            for (int i = 0; i < tileRenderers.Length; i++)
            {
                Renderer tileRenderer = tileRenderers[i];
                if (tileRenderer == null)
                {
                    continue;
                }

                tileRenderer.GetPropertyBlock(tileProperties);
                tileProperties.SetColor(BaseColorProperty, neutral);
                tileProperties.SetColor(ColorProperty, neutral);
                tileRenderer.SetPropertyBlock(tileProperties);
            }
        }
    }
}
