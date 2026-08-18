using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerGameplayAnimationSetup
{
    private const string MagicPath =
        "Assets/LGH/Animations/X Bot@Standing 1H Magic Attack 03.fbx";
    private const string DizzyPath =
        "Assets/LGH/Animations/X Bot@Dizzy Idle.fbx";
    private const string ControllerPath =
        "Assets/ithappy/Creative_Characters_FREE/Animations/Animation_Controllers/Character_Movement.controller";
    private const float MagicHoldFrame = 40f;

    [MenuItem("Tools/LGH/Setup Player Gameplay Animations")]
    public static void Setup()
    {
        ConfigureMagicClips();
        ConfigureDizzyClip();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ConfigureController();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PlayerAnimation] Skill prepare/hold/release and stun animations configured.");
    }

    private static void ConfigureMagicClips()
    {
        ModelImporter importer = AssetImporter.GetAtPath(MagicPath) as ModelImporter;
        if (importer == null)
        {
            throw new System.InvalidOperationException("Magic animation FBX was not found: " + MagicPath);
        }

        ModelImporterClipAnimation source = GetSourceClip(importer);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.importAnimation = true;
        importer.clipAnimations = new[]
        {
            CreateClip(source, "SkillPrepare", source.firstFrame, MagicHoldFrame, false),
            CreateClip(source, "SkillHold", MagicHoldFrame, MagicHoldFrame + 1f, true),
            CreateClip(source, "SkillRelease", MagicHoldFrame, source.lastFrame, false)
        };
        importer.SaveAndReimport();
    }

    private static void ConfigureDizzyClip()
    {
        ModelImporter importer = AssetImporter.GetAtPath(DizzyPath) as ModelImporter;
        if (importer == null)
        {
            throw new System.InvalidOperationException("Dizzy animation FBX was not found: " + DizzyPath);
        }

        ModelImporterClipAnimation source = GetSourceClip(importer);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.importAnimation = true;
        importer.clipAnimations = new[]
        {
            CreateClip(source, "DizzyIdle", source.firstFrame, source.lastFrame, true)
        };
        importer.SaveAndReimport();
    }

    private static ModelImporterClipAnimation GetSourceClip(ModelImporter importer)
    {
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.clipAnimations;
        }

        if (clips == null || clips.Length == 0)
        {
            throw new System.InvalidOperationException("Animation FBX contains no clips: " + importer.assetPath);
        }

        return clips[0];
    }

    private static ModelImporterClipAnimation CreateClip(
        ModelImporterClipAnimation source,
        string name,
        float firstFrame,
        float lastFrame,
        bool loop)
    {
        return new ModelImporterClipAnimation
        {
            name = name,
            takeName = source.takeName,
            firstFrame = firstFrame,
            lastFrame = lastFrame,
            loopTime = loop,
            loopPose = loop,
            lockRootRotation = true,
            lockRootHeightY = true,
            lockRootPositionXZ = true,
            keepOriginalOrientation = true,
            keepOriginalPositionY = true,
            keepOriginalPositionXZ = true
        };
    }

    private static void ConfigureController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0)
        {
            throw new System.InvalidOperationException("Player Animator Controller was not found: " + ControllerPath);
        }

        EnsureTrigger(controller, "SkillPrepare");
        EnsureTrigger(controller, "SkillCast");
        EnsureTrigger(controller, "SkillCastDirect");
        EnsureTrigger(controller, "SkillCancel");
        EnsureBool(controller, "IsStunned");

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        RemoveState(stateMachine, "SkillPrepare");
        RemoveState(stateMachine, "SkillHold");
        RemoveState(stateMachine, "SkillRelease");
        RemoveState(stateMachine, "DizzyIdle");

        AnimationClip prepareClip = LoadClip(MagicPath, "SkillPrepare");
        AnimationClip holdClip = LoadClip(MagicPath, "SkillHold");
        AnimationClip releaseClip = LoadClip(MagicPath, "SkillRelease");
        AnimationClip dizzyClip = LoadClip(DizzyPath, "DizzyIdle");
        AnimatorState idle = FindState(stateMachine, "Idle_Look_Around") ?? stateMachine.defaultState;

        AnimatorState prepare = stateMachine.AddState("SkillPrepare", new Vector3(520f, 260f));
        AnimatorState hold = stateMachine.AddState("SkillHold", new Vector3(720f, 260f));
        AnimatorState release = stateMachine.AddState("SkillRelease", new Vector3(920f, 260f));
        AnimatorState dizzy = stateMachine.AddState("DizzyIdle", new Vector3(720f, 430f));
        prepare.motion = prepareClip;
        hold.motion = holdClip;
        hold.speed = 0f;
        release.motion = releaseClip;
        dizzy.motion = dizzyClip;

        AddAnyTrigger(stateMachine, prepare, "SkillPrepare", 0.05f);
        AddAnyTrigger(stateMachine, release, "SkillCastDirect", 0.05f);
        AddAnyBool(stateMachine, dizzy, "IsStunned", true, 0.05f);

        AnimatorStateTransition prepareToHold = prepare.AddTransition(hold);
        ConfigureExitTransition(prepareToHold, 0.98f, 0.03f);
        AddTriggerTransition(prepare, release, "SkillCast", 0.04f);
        AddTriggerTransition(hold, release, "SkillCast", 0.04f);
        AddTriggerTransition(prepare, idle, "SkillCancel", 0.06f);
        AddTriggerTransition(hold, idle, "SkillCancel", 0.06f);

        AnimatorStateTransition releaseToIdle = release.AddTransition(idle);
        ConfigureExitTransition(releaseToIdle, 0.98f, 0.08f);

        AnimatorStateTransition dizzyToIdle = dizzy.AddTransition(idle);
        dizzyToIdle.hasExitTime = false;
        dizzyToIdle.duration = 0.08f;
        dizzyToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsStunned");

        EditorUtility.SetDirty(controller);
    }

    private static AnimationClip LoadClip(string path, string name)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => candidate.name == name);
        if (clip == null)
        {
            throw new System.InvalidOperationException("Animation clip was not imported: " + name);
        }

        return clip;
    }

    private static AnimatorState FindState(AnimatorStateMachine stateMachine, string name)
    {
        return stateMachine.states
            .Select(child => child.state)
            .FirstOrDefault(state => state != null && state.name == name);
    }

    private static void RemoveState(AnimatorStateMachine stateMachine, string name)
    {
        AnimatorState state = FindState(stateMachine, name);
        if (state != null)
        {
            stateMachine.RemoveState(state);
        }
    }

    private static void EnsureTrigger(AnimatorController controller, string name)
    {
        EnsureParameter(controller, name, AnimatorControllerParameterType.Trigger);
    }

    private static void EnsureBool(AnimatorController controller, string name)
    {
        EnsureParameter(controller, name, AnimatorControllerParameterType.Bool);
    }

    private static void EnsureParameter(
        AnimatorController controller,
        string name,
        AnimatorControllerParameterType type)
    {
        if (!controller.parameters.Any(parameter => parameter.name == name))
        {
            controller.AddParameter(name, type);
        }
    }

    private static void AddAnyTrigger(
        AnimatorStateMachine stateMachine,
        AnimatorState destination,
        string parameter,
        float duration)
    {
        AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(destination);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
    }

    private static void AddAnyBool(
        AnimatorStateMachine stateMachine,
        AnimatorState destination,
        string parameter,
        bool value,
        float duration)
    {
        AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(destination);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.canTransitionToSelf = false;
        transition.AddCondition(
            value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
            0f,
            parameter);
    }

    private static void AddTriggerTransition(
        AnimatorState source,
        AnimatorState destination,
        string parameter,
        float duration)
    {
        AnimatorStateTransition transition = source.AddTransition(destination);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
    }

    private static void ConfigureExitTransition(
        AnimatorStateTransition transition,
        float exitTime,
        float duration)
    {
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.duration = duration;
    }
}
