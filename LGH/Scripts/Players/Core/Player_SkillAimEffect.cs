using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// 플레이어의 스킬 조준 파티클을 담당합니다.
// 주요 기능:
// 1. 스킬 조준 시작/종료 상태를 네트워크로 동기화
// 2. 로컬 플레이어, 다른 플레이어, 봇 모두 동일하게 표시
// 3. 플레이어 PaintOwner 색상을 파티클에 적용
// 4. HDR 밝기와 채도 강화를 통해 파티클을 선명하게 표시
// 이 파일은 Player partial class이므로 별도 컴포넌트가 생성되지 않습니다.
// 기존 MyPlayer의 Player 컴포넌트에 Inspector 필드가 추가됩니다.
public partial class Player
{
    [Serializable]
    private sealed class SkillAimParticleTintTarget
    {
        [Tooltip("색상을 변경할 ParticleSystem입니다.")]
        [SerializeField]
        private ParticleSystem particle;

        [Tooltip(
            "해당 파티클의 개별 밝기입니다.\n" +
            "1 = 기본 밝기\n" +
            "1보다 큼 = 더 밝고 강하게\n" +
            "1보다 작음 = 더 어둡게")]
        [SerializeField, Range(0f, 4f)]
        private float brightness = 1f;

        [Tooltip(
            "해당 파티클의 투명도입니다.\n" +
            "1 = 완전 불투명\n" +
            "0 = 완전 투명")]
        [SerializeField, Range(0f, 1f)]
        private float alpha = 1f;

        public ParticleSystem Particle
        {
            get { return particle; }
        }

        public float Brightness
        {
            get { return brightness; }
        }

        public float Alpha
        {
            get { return alpha; }
        }
    }

    [Header("Skill Aim Charge Effect")]

    [Tooltip(
        "MyPlayer 자식의 Magic circle 오브젝트를 연결합니다.\n" +
        "평소에는 비활성화 상태여도 됩니다.")]
    [SerializeField]
    private GameObject skillAimEffectRoot;

    [Tooltip(
        "각 ParticleSystem별 밝기와 투명도를 설정합니다.\n" +
        "Magic circle, DarkSparks, Sparks, Sides, CircleSparks를 등록합니다.")]
    [SerializeField]
    private SkillAimParticleTintTarget[] skillAimParticleTints;

    [Header("Skill Aim Effect Color Boost")]

    [Tooltip(
        "전체 파티클의 HDR 밝기입니다.\n" +
        "1 = 기본 밝기\n" +
        "1.5~2 = 선명하고 눈에 잘 띄는 수준\n" +
        "너무 높으면 색상이 하얗게 날아갈 수 있습니다.")]
    [SerializeField, Range(0.5f, 3f)]
    private float skillAimColorIntensity = 1.8f;

    [Tooltip(
        "플레이어 색상의 채도를 높입니다.\n" +
        "0 = 기존 색상\n" +
        "1 = 최대한 선명한 원색")]
    [SerializeField, Range(0f, 1f)]
    private float skillAimSaturationBoost = 0.45f;

    [Tooltip(
        "전체 파티클의 알파 배율입니다.\n" +
        "투명한 파티클을 조금 더 진하게 표시할 때 사용합니다.")]
    [SerializeField, Range(0.5f, 2f)]
    private float skillAimAlphaMultiplier = 1.2f;

    // 서버가 관리하는 스킬 조준 파티클 활성 상태입니다.
    // true:
    // 해당 플레이어가 스킬을 준비하거나 조준 중
    // false:
    // 스킬을 발동했거나 조준이 취소됨
    // SyncVar이기 때문에 사람 플레이어와 봇 모두
    // 모든 클라이언트에서 동일하게 표시됩니다.
    public readonly SyncVar<bool> skillAimEffectActive =
        new SyncVar<bool>(false);

    private ParticleSystem[] skillAimParticles =
        Array.Empty<ParticleSystem>();

    private bool skillAimEffectInitialized;
    private bool localSkillAimEffectActive;

    // Player.Awake()에서 호출합니다.
    private void InitializeSkillAimEffect()
    {
        if (skillAimEffectInitialized)
        {
            return;
        }

        skillAimEffectInitialized = true;

        /*
         * Inspector 연결을 하지 않았을 경우
         * MyPlayer의 직계 자식에서 "Magic circle"을 자동으로 찾습니다.
         */
        if (skillAimEffectRoot == null)
        {
            Transform effectTransform =
                transform.Find("Magic circle");

            if (effectTransform != null)
            {
                skillAimEffectRoot =
                    effectTransform.gameObject;
            }
        }

        skillAimEffectActive.OnChange +=
            OnSkillAimEffectActiveChanged;

        if (skillAimEffectRoot == null)
        {
            Debug.LogWarning(
                "[Player] Skill Aim Effect Root가 연결되지 않았습니다. " +
                "MyPlayer의 Player 컴포넌트에서 Magic circle을 연결하세요.",
                this);

            return;
        }

        /*
         * Magic circle 루트가 비활성화된 상태여도
         * 모든 자식 ParticleSystem을 찾습니다.
         */
        skillAimParticles =
            skillAimEffectRoot.GetComponentsInChildren<ParticleSystem>(true);

        if (skillAimParticles.Length == 0)
        {
            Debug.LogWarning(
                "[Player] Magic circle 아래에서 ParticleSystem을 찾지 못했습니다.",
                this);
        }

        /*
         * 초기 PaintOwner 색상을 적용합니다.
         */
        ApplySkillAimEffectColor(myPaintOwner.Value);

        /*
         * 플레이어가 생성됐을 때 파티클이 켜져 있지 않도록
         * 강제로 정지하고 루트 오브젝트를 비활성화합니다.
         */
        SetSkillAimEffectLocal(false, true);
    }

    // Player.OnDestroy()에서 호출합니다.
    private void DisposeSkillAimEffect()
    {
        if (!skillAimEffectInitialized)
        {
            return;
        }

        skillAimEffectActive.OnChange -=
            OnSkillAimEffectActiveChanged;

        skillAimEffectInitialized = false;
    }

    // Player.OnStartClient()에서 호출합니다.
    // 네트워크에서 전달받은 현재 상태와 색상을 적용합니다.
    // 늦게 접속한 클라이언트도 현재 조준 상태를 볼 수 있습니다.
    private void RefreshSkillAimEffectFromNetwork()
    {
        if (!skillAimEffectInitialized)
        {
            InitializeSkillAimEffect();
        }

        ApplySkillAimEffectColor(myPaintOwner.Value);

        SetSkillAimEffectLocal(
            skillAimEffectActive.Value,
            true);
    }

    // 스킬 조준 파티클의 활성 상태를 요청합니다.
    // 사람:
    // 첫 번째 스킬 버튼에서 true
    // 두 번째 스킬 버튼 또는 취소에서 false
    // 봇:
    // 스킬 준비 시작에서 true
    // 실제 스킬 발동 또는 취소에서 false
    public void RequestSkillAimEffectActive(bool active)
    {
        if (!skillAimEffectInitialized)
        {
            InitializeSkillAimEffect();
        }

        /*
         * 일반 클라이언트가 다른 플레이어의 상태를
         * 임의로 변경하지 못하게 막습니다.
         *
         * 서버에서는 봇의 상태를 직접 변경할 수 있어야 하므로
         * IsServerInitialized일 때는 통과시킵니다.
         */
        if (IsClientInitialized &&
            !IsOwner &&
            !IsServerInitialized)
        {
            return;
        }

        /*
         * Dedicated Server는 화면이 없으므로
         * 서버 내부에서 ParticleSystem을 재생할 필요가 없습니다.
         *
         * Host 또는 일반 클라이언트에서는
         * 네트워크 왕복 지연 없이 즉시 표시합니다.
         */
        bool isDedicatedServer =
            IsServerInitialized &&
            !IsClientInitialized;

        if (!isDedicatedServer)
        {
            SetSkillAimEffectLocal(active);
        }

        /*
         * 서버 또는 Host에서 호출한 경우
         * SyncVar을 직접 변경합니다.
         *
         * 봇의 스킬은 서버에서 실행되므로
         * 이 경로를 통해 모든 클라이언트에 전달됩니다.
         */
        if (IsServerInitialized)
        {
            skillAimEffectActive.Value = active;
            return;
        }

        /*
         * 일반 플레이어 클라이언트는
         * ServerRpc를 통해 서버에 요청합니다.
         */
        if (IsClientInitialized && IsOwner)
        {
            ServerSetSkillAimEffectActive(active);
        }
    }

    // Owner 클라이언트가 스킬 조준 상태를 서버에 제출합니다.
    [ServerRpc]
    private void ServerSetSkillAimEffectActive(
        bool active,
        NetworkConnection sender = null)
    {
        /*
         * 해당 Player의 실제 Owner가 보낸 요청인지 확인합니다.
         */
        if (sender != null && sender != Owner)
        {
            return;
        }

        skillAimEffectActive.Value = active;
    }

    // 서버의 SyncVar 값이 변경되면
    // 각 클라이언트에서 호출됩니다.
    private void OnSkillAimEffectActiveChanged(
        bool previous,
        bool next,
        bool asServer)
    {
        /*
         * 화면이 없는 Dedicated Server에서는
         * 파티클을 재생하지 않습니다.
         */
        if (IsServerInitialized &&
            !IsClientInitialized)
        {
            return;
        }

        SetSkillAimEffectLocal(next);
    }

    // 현재 클라이언트 화면에서
    // 해당 플레이어의 파티클을 켜거나 끕니다.
    // 이 함수 자체는 네트워크 요청을 보내지 않습니다.
    private void SetSkillAimEffectLocal(
        bool active,
        bool force = false)
    {
        if (skillAimEffectRoot == null)
        {
            return;
        }

        if (!force &&
            localSkillAimEffectActive == active)
        {
            return;
        }

        localSkillAimEffectActive = active;

        if (active)
        {
            /*
             * 루트 오브젝트를 먼저 활성화해야
             * 비활성화 상태였던 ParticleSystem을 재생할 수 있습니다.
             */
            skillAimEffectRoot.SetActive(true);

            for (int i = 0;
                 i < skillAimParticles.Length;
                 i++)
            {
                ParticleSystem particle =
                    skillAimParticles[i];

                if (particle == null)
                {
                    continue;
                }

                /*
                 * 이전 재생에서 남아 있던 파티클을 제거한 다음
                 * 처음부터 다시 재생합니다.
                 */
                particle.Clear(true);
                particle.Play(true);
            }

            return;
        }

        /*
         * 파티클을 즉시 정지하고
         * 현재 남아 있는 파티클도 모두 제거합니다.
         */
        for (int i = 0;
             i < skillAimParticles.Length;
             i++)
        {
            ParticleSystem particle =
                skillAimParticles[i];

            if (particle == null)
            {
                continue;
            }

            particle.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /*
         * 사용하지 않을 때 루트 자체를 비활성화하여
         * Update 및 렌더링 비용을 줄입니다.
         */
        skillAimEffectRoot.SetActive(false);
    }

    // 플레이어의 PaintOwner 색상을
    // 스킬 조준 ParticleSystem에 적용합니다.
    // Material을 복제하지 않고 MainModule.startColor만 바꾸므로
    // 플레이어마다 Material 인스턴스가 생기지 않습니다.
    private void ApplySkillAimEffectColor(PaintOwner owner)
    {
        if (skillAimEffectRoot == null)
        {
            return;
        }

        Color ownerColor =
            GetPaintOwnerTintColor(owner);

        /*
         * Tint Targets에 등록되지 않은 ParticleSystem도
         * 기본 플레이어 색상으로 표시합니다.
         */
        for (int i = 0;
             i < skillAimParticles.Length;
             i++)
        {
            SetParticleStartColor(
                skillAimParticles[i],
                ownerColor,
                1f,
                1f);
        }

        /*
         * Inspector에 등록한 ParticleSystem에는
         * 각자 다른 밝기와 알파를 적용합니다.
         */
        if (skillAimParticleTints != null)
        {
            for (int i = 0;
                 i < skillAimParticleTints.Length;
                 i++)
            {
                SkillAimParticleTintTarget target =
                    skillAimParticleTints[i];

                if (target == null ||
                    target.Particle == null)
                {
                    continue;
                }

                SetParticleStartColor(
                    target.Particle,
                    ownerColor,
                    target.Brightness,
                    target.Alpha);
            }
        }

        /*
         * 파티클이 켜진 상태에서 PaintOwner가 변경됐다면
         * 기존 파티클을 제거하고 새 색상으로 다시 재생합니다.
         */
        if (!localSkillAimEffectActive)
        {
            return;
        }

        for (int i = 0;
             i < skillAimParticles.Length;
             i++)
        {
            ParticleSystem particle =
                skillAimParticles[i];

            if (particle == null)
            {
                continue;
            }

            particle.Clear(true);
            particle.Play(true);
        }
    }

    // ParticleSystem의 Start Color를 변경합니다.
    // HSV 채도 강화와 HDR Value를 사용하여
    // 색상이 어둡거나 탁해지지 않고 눈에 잘 띄도록 합니다.
    private void SetParticleStartColor(
        ParticleSystem particle,
        Color ownerColor,
        float individualBrightness,
        float individualAlpha)
    {
        if (particle == null)
        {
            return;
        }

        individualBrightness =
            Mathf.Max(0f, individualBrightness);

        individualAlpha =
            Mathf.Clamp01(individualAlpha);

        /*
         * RGB를 HSV로 변환합니다.
         *
         * Hue:
         * 빨강, 파랑, 노랑, 초록 등의 실제 색상 종류
         *
         * Saturation:
         * 색상의 선명도
         *
         * Value:
         * 색상의 밝기
         */
        Color.RGBToHSV(
            ownerColor,
            out float hue,
            out float saturation,
            out float value);

        /*
         * 기존 플레이어 색상이 조금 탁한 경우
         * Saturation을 1에 가깝게 올려 더 선명하게 만듭니다.
         */
        saturation = Mathf.Lerp(
            saturation,
            1f,
            skillAimSaturationBoost);

        /*
         * 개별 파티클 밝기와 전체 밝기를 곱합니다.
         *
         * 여기서는 Clamp01을 사용하지 않습니다.
         * 1보다 큰 Value를 허용하여 HDR 색상으로 출력합니다.
         */
        value *=
            individualBrightness *
            skillAimColorIntensity;

        /*
         * 마지막 인자 true는 HDR 출력을 허용합니다.
         *
         * true가 없으면 RGB가 1에서 잘리기 때문에
         * Brightness를 높여도 효과가 약하게 보일 수 있습니다.
         */
        Color tintedColor =
            Color.HSVToRGB(
                hue,
                saturation,
                value,
                true);

        tintedColor.a =
            Mathf.Clamp01(
                ownerColor.a *
                individualAlpha *
                skillAimAlphaMultiplier);

        ParticleSystem.MainModule main =
            particle.main;

        main.startColor =
            new ParticleSystem.MinMaxGradient(tintedColor);
    }

    // 플레이어가 despawn되거나 클라이언트가 종료될 때
    // 남아 있는 파티클을 정리합니다.
    private void StopSkillAimEffectForShutdown()
    {
        SetSkillAimEffectLocal(false, true);
    }
}