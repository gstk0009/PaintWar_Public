using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class GameplayAudioSetup
{
    private const int SampleRate = 44100;
    private const string BgmFolder = "Assets/Resources/BGM";
    private const string SfxFolder = "Assets/Resources/SFX/Gameplay";
    private const string SoundManagerPrefabPath = "Assets/CSE/Prerfabs/SoundManager.prefab";
    private const string AutoStampPath = "Temp/PaintWarGameplayAudioSetup.v2.done";
    private const string LogPath = "Logs/PaintWarGameplayAudioSetup.log";

    private static readonly string[] SkillIds =
    {
        "arcane_lance",
        "crystal_bloom",
        "fire_surge",
        "frost_wall",
        "ground_rupture",
        "lightning_chain",
        "meteor_cross",
        "red_energy_burst"
    };

    [InitializeOnLoadMethod]
    private static void QueueFirstSetup()
    {
        if (!File.Exists(AutoStampPath))
        {
            EditorApplication.delayCall += RunFirstSetup;
        }
    }

    [MenuItem("PaintWar/Audio/Build Or Refresh Gameplay Audio")]
    public static void BuildOrRefresh()
    {
        Directory.CreateDirectory(SfxFolder);
        WriteMonoWav(SfxFolder + "/movement_step_02.wav", CreatePaintTileStep());
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureAudioImporters();

        float winnerStartSeconds = BuildWinnerExcerpt();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureAudioImporters();
        ConfigureWinnerImporter();

        ConfigureSoundManagerPrefab();
        ConfigureWeaponProfiles();
        AssetDatabase.SaveAssets();
        File.WriteAllText(
            LogPath,
            "Gameplay audio setup complete. Winner excerpt starts at " +
            winnerStartSeconds.ToString("0.00") + "s and lasts 30.00s.\n");
        File.WriteAllText(AutoStampPath, DateTime.UtcNow.ToString("O"));
        Debug.Log("[GameplayAudioSetup] Complete. Winner excerpt start=" +
                  winnerStartSeconds.ToString("0.00") + "s.");
    }

    [MenuItem("PaintWar/Audio/Rebuild Movement Step")]
    public static void RebuildMovementStep()
    {
        Directory.CreateDirectory(SfxFolder);
        WriteMonoWav(SfxFolder + "/movement_step_02.wav", CreatePaintTileStep());
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureAudioImporters();
        AssetDatabase.SaveAssets();
        Debug.Log("[GameplayAudioSetup] Rebuilt the single paint-tile movement step.");
    }

    private static void RunFirstSetup()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += RunFirstSetup;
            return;
        }

        try
        {
            BuildOrRefresh();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            File.WriteAllText(LogPath, exception.ToString());
        }
    }

    private static void GenerateGameplaySfx()
    {
        for (int i = 0; i < 3; i++)
        {
            WriteMonoWav(SfxFolder + "/movement_step_0" + (i + 1) + ".wav", CreateFootstep(i));
        }

        WriteMonoWav(SfxFolder + "/teleport.wav", CreateTeleport());
        for (int i = 0; i < SkillIds.Length; i++)
        {
            WriteMonoWav(SfxFolder + "/" + SkillIds[i] + "_marker.wav", CreateSkillMarker(i));
            WriteMonoWav(SfxFolder + "/" + SkillIds[i] + "_impact.wav", CreateSkillImpact(i));
        }
    }

    private static float[] CreateFootstep(int variant)
    {
        float duration = 0.18f;
        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        System.Random random = new System.Random(1200 + variant);
        float filteredNoise = 0f;
        float baseFrequency = 96f + variant * 8f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float envelope = Mathf.Exp(-t * 23f) * SmoothAttack(t, 0.003f);
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            filteredNoise = Mathf.Lerp(filteredNoise, noise, 0.13f);
            float thump = Mathf.Sin(2f * Mathf.PI * (baseFrequency * t - 38f * t * t));
            float pop = Mathf.Sin(2f * Mathf.PI * (260f + variant * 22f) * t) * Mathf.Exp(-t * 42f);
            float wetChirp = Mathf.Sin(
                2f * Mathf.PI * ((285f + variant * 14f) * t - 390f * t * t)) *
                Mathf.Exp(-t * 13f);
            samples[i] = (thump * 0.34f + filteredNoise * 0.18f + pop * 0.22f + wetChirp * 0.56f) * envelope;
        }

        return PunchUp(samples, 0.94f, 0.72f);
    }

    private static float[] CreatePaintTileStep()
    {
        // Tuned to GameBGM's D# minor palette. The low body carries weight while
        // the short D#-F#-A# mallet response supplies the upbeat spring.
        const float duration = 0.175f;
        const float dSharp4 = 311.127f;
        const float fSharp4 = 369.994f;
        const float aSharp4 = 466.164f;
        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        System.Random random = new System.Random(27000);
        float filteredNoise = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            filteredNoise = Mathf.Lerp(filteredNoise, noise, 0.08f);
            float clickNoise = noise - filteredNoise;

            float body = SweptSine(t, 132f, 84f, duration) *
                         Mathf.Exp(-t * 23f) *
                         SmoothAttack(t, 0.0018f);
            float root = (
                Mathf.Sin(2f * Mathf.PI * dSharp4 * t) +
                0.18f * Mathf.Sin(2f * Mathf.PI * dSharp4 * 2f * t)) *
                Mathf.Exp(-t * 29f) *
                SmoothAttack(t, 0.0012f);
            float mallet = (
                Mathf.Sin(2f * Mathf.PI * fSharp4 * t) +
                0.30f * Mathf.Sin(2f * Mathf.PI * fSharp4 * 2f * t) +
                0.10f * Mathf.Sin(2f * Mathf.PI * fSharp4 * 3f * t)) *
                Mathf.Exp(-t * 35f) *
                SmoothAttack(t, 0.0008f);

            float springTime = t - 0.052f;
            float spring = springTime >= 0f
                ? (
                    Mathf.Sin(2f * Mathf.PI * aSharp4 * springTime) +
                    0.22f * Mathf.Sin(2f * Mathf.PI * aSharp4 * 2f * springTime)) *
                  Mathf.Exp(-springTime * 47f) *
                  SmoothAttack(springTime, 0.001f)
                : 0f;

            float mixed = body * 0.44f +
                          root * 0.25f +
                          mallet * 0.24f +
                          spring * 0.13f +
                          clickNoise * Mathf.Exp(-t * 72f) * 0.035f;
            samples[i] = (float)System.Math.Tanh(mixed * 1.18f);
        }

        LimitPeak(samples, 0.76f);
        return samples;
    }

    private static float SweptSine(
        float time,
        float startFrequency,
        float endFrequency,
        float duration)
    {
        float safeDuration = Mathf.Max(0.0001f, duration);
        float phase = startFrequency * time +
                      0.5f * (endFrequency - startFrequency) *
                      time * time / safeDuration;
        return Mathf.Sin(2f * Mathf.PI * phase);
    }

    private static float[] CreateTeleport()
    {
        const float duration = 0.78f;
        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        System.Random random = new System.Random(7221);
        float filteredNoise = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            filteredNoise = Mathf.Lerp(filteredNoise, noise, 0.055f);

            // A reverse suction, a short silence while displaced, then a clean arrival ping.
            float suctionEnvelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.48f)) *
                                    (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p - 0.48f) / 0.09f)));
            float suctionPhase = 2f * Mathf.PI * (120f * t + 1150f * t * t);
            float suction = (Mathf.Sin(suctionPhase) * 0.5f + filteredNoise * 0.42f) * suctionEnvelope;
            float arrivalTime = t - 0.54f;
            float arrivalEnvelope = arrivalTime >= 0f ? Mathf.Exp(-arrivalTime * 10f) : 0f;
            float phasePing = Mathf.Sin(2f * Mathf.PI * 1046.5f * arrivalTime) +
                              0.48f * Mathf.Sin(2f * Mathf.PI * 1568f * arrivalTime);
            float airyTail = arrivalTime >= 0f ? filteredNoise * Mathf.Exp(-arrivalTime * 7f) : 0f;
            samples[i] = suction * 0.78f + phasePing * arrivalEnvelope * 0.48f + airyTail * 0.22f;
        }

        return PunchUp(samples, 0.97f, 0.72f);
    }

    private static float[] CreateSkillMarker(int style)
    {
        float duration = style == 6 ? 0.9f : style == 4 ? 0.72f : 0.58f;
        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        System.Random random = new System.Random(3000 + style * 97);
        float filteredNoise = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            filteredNoise = Mathf.Lerp(filteredNoise, noise, style == 5 ? 0.48f : 0.07f);
            float sample;
            switch (style)
            {
                case 0: // Arcane Lance: a tight, focused rising blade charge.
                    float lancePhase = 2f * Mathf.PI * (310f * t + 680f * t * t);
                    float lanceTick = Mathf.Exp(-Mathf.Pow((p - 0.18f) / 0.025f, 2f)) +
                                      Mathf.Exp(-Mathf.Pow((p - 0.45f) / 0.025f, 2f)) +
                                      Mathf.Exp(-Mathf.Pow((p - 0.72f) / 0.025f, 2f));
                    sample = Mathf.Sin(lancePhase) * Mathf.Pow(p, 0.55f) * 0.55f +
                             Mathf.Sin(2f * lancePhase) * lanceTick * 0.42f;
                    break;
                case 1: // Crystal Bloom: separated crystalline arpeggio petals.
                    int crystalStep = Mathf.Min(3, Mathf.FloorToInt(p * 4f));
                    float crystalLocal = p * 4f - crystalStep;
                    float crystalFrequency = crystalStep == 0 ? 659.25f : crystalStep == 1 ? 783.99f :
                                             crystalStep == 2 ? 987.77f : 1318.5f;
                    float crystalPhase = 2f * Mathf.PI * crystalFrequency * t;
                    sample = (Mathf.Sin(crystalPhase) + 0.6f * Mathf.Sin(crystalPhase * 2.71f)) *
                             Mathf.Exp(-crystalLocal * 5.5f) * 0.56f;
                    break;
                case 2: // Fire Surge: ignition hiss swelling into a flame roar.
                    float fireFlutter = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 17f * t);
                    float fireBody = Mathf.Sin(2f * Mathf.PI * (128f - 32f * p) * t);
                    sample = (filteredNoise * 0.64f * fireFlutter + fireBody * 0.42f) *
                             Mathf.SmoothStep(0f, 1f, p);
                    break;
                case 3: // Frost Wall: descending icy scrape with brittle glints.
                    float icePhase = 2f * Mathf.PI * (1450f * t - 650f * t * t);
                    float iceGlints = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 9f * t)), 12f);
                    sample = Mathf.Sin(icePhase) * 0.38f +
                             Mathf.Sin(icePhase * 1.618f) * iceGlints * 0.58f + filteredNoise * 0.12f;
                    break;
                case 4: // Ground Rupture: three unmistakable subterranean warning knocks.
                    float groundPulse = Mathf.Exp(-Mathf.Pow((p - 0.14f) / 0.06f, 2f)) +
                                        Mathf.Exp(-Mathf.Pow((p - 0.48f) / 0.06f, 2f)) +
                                        Mathf.Exp(-Mathf.Pow((p - 0.82f) / 0.06f, 2f));
                    sample = (Mathf.Sin(2f * Mathf.PI * 67f * t) * 0.78f + filteredNoise * 0.28f) * groundPulse;
                    break;
                case 5: // Lightning Chain: gated electrical chatter, not a continuous charge.
                    float electricGate = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 27f * t)), 5f);
                    float electricCarrier = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * (720f + 390f * p) * t));
                    sample = (electricCarrier * 0.44f + filteredNoise * 0.56f) * electricGate * (0.5f + 0.5f * p);
                    break;
                case 6: // Meteor Cross: ominous low pulses under a falling whistle.
                    float meteorPulse = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (3f + 5f * p) * t);
                    float fallPhase = 2f * Mathf.PI * (1250f * t - 520f * t * t);
                    sample = Mathf.Sin(2f * Mathf.PI * 54f * t) * meteorPulse * 0.68f +
                             Mathf.Sin(fallPhase) * Mathf.Pow(p, 1.4f) * 0.38f;
                    break;
                default: // Red Energy Burst: synthetic heartbeat and unstable charge.
                    float heart = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 4.2f * t)), 10f);
                    float energy = Mathf.Sin(2f * Mathf.PI * (180f * t + 430f * t * t));
                    sample = Mathf.Sin(2f * Mathf.PI * 76f * t) * heart * 0.72f +
                             Mathf.Sign(energy) * Mathf.Pow(p, 1.2f) * 0.34f;
                    break;
            }

            float envelope = SmoothAttack(t, 0.006f) * Mathf.Pow(1f - p, 0.22f);
            samples[i] = sample * envelope;
        }

        return PunchUp(samples, 0.97f, 0.74f);
    }

    private static float[] CreateSkillImpact(int style)
    {
        float duration = style == 6 ? 1.2f : style == 4 ? 1.05f : 0.82f;
        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        System.Random random = new System.Random(6000 + style * 131);
        float filteredNoise = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            filteredNoise = Mathf.Lerp(filteredNoise, noise, style == 1 || style == 5 ? 0.5f : 0.075f);
            float sample;
            switch (style)
            {
                case 0: // Arcane Lance: dry piercing crack followed by a thin metallic ring.
                    float pierce = Mathf.Sin(2f * Mathf.PI * (1750f * t - 420f * t * t));
                    sample = noise * Mathf.Exp(-t * 90f) * 0.75f + pierce * Mathf.Exp(-t * 11f) * 0.66f;
                    break;
                case 1: // Crystal Bloom: a cluster of glass shards at inharmonic pitches.
                    float shardEnvelope = Mathf.Exp(-t * 7.5f);
                    sample = (Mathf.Sin(2f * Mathf.PI * 1180f * t) +
                              0.72f * Mathf.Sin(2f * Mathf.PI * 1733f * t) +
                              0.52f * Mathf.Sin(2f * Mathf.PI * 2467f * t)) * shardEnvelope * 0.48f +
                             filteredNoise * Mathf.Exp(-t * 24f) * 0.5f;
                    break;
                case 2: // Fire Surge: broad noisy combustion with a rolling low flame tail.
                    float flameFlutter = 0.62f + 0.38f * Mathf.Sin(2f * Mathf.PI * 13f * t);
                    sample = noise * Mathf.Exp(-t * 24f) * 0.72f +
                             filteredNoise * flameFlutter * Mathf.Exp(-t * 3.8f) * 0.64f +
                             Mathf.Sin(2f * Mathf.PI * (88f * t - 31f * t * t)) * Mathf.Exp(-t * 7f) * 0.55f;
                    break;
                case 3: // Frost Wall: hard ice crack, then brittle cascading fragments.
                    float crackGate = Mathf.Exp(-t * 48f) +
                                      0.55f * Mathf.Exp(-Mathf.Pow((t - 0.17f) / 0.025f, 2f)) +
                                      0.4f * Mathf.Exp(-Mathf.Pow((t - 0.31f) / 0.03f, 2f));
                    sample = noise * crackGate * 0.62f +
                             (Mathf.Sin(2f * Mathf.PI * 2130f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 3190f * t)) *
                             crackGate * 0.5f;
                    break;
                case 4: // Ground Rupture: heavy rock slam and long granular rumble.
                    float earthBoom = Mathf.Sin(2f * Mathf.PI * (72f * t - 28f * t * t));
                    float rockHits = Mathf.Exp(-Mathf.Pow((t - 0.22f) / 0.055f, 2f)) +
                                     0.7f * Mathf.Exp(-Mathf.Pow((t - 0.48f) / 0.075f, 2f));
                    sample = earthBoom * Mathf.Exp(-t * 4f) * 0.86f +
                             filteredNoise * Mathf.Exp(-t * 2.8f) * 0.5f + noise * rockHits * 0.42f;
                    break;
                case 5: // Lightning Chain: multiple discrete high-voltage snaps.
                    float snapGate = Mathf.Exp(-Mathf.Pow((t - 0.01f) / 0.012f, 2f)) +
                                     Mathf.Exp(-Mathf.Pow((t - 0.12f) / 0.016f, 2f)) +
                                     0.8f * Mathf.Exp(-Mathf.Pow((t - 0.24f) / 0.02f, 2f)) +
                                     0.6f * Mathf.Exp(-Mathf.Pow((t - 0.39f) / 0.024f, 2f));
                    float voltage = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 1380f * t));
                    sample = (voltage * 0.48f + filteredNoise * 0.75f) * snapGate;
                    break;
                case 6: // Meteor Cross: arrival crack, enormous low body, burning debris tail.
                    float meteorBoom = Mathf.Sin(2f * Mathf.PI * (61f * t - 20f * t * t));
                    float debris = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 15f * t);
                    sample = noise * Mathf.Exp(-t * 42f) * 0.86f +
                             meteorBoom * Mathf.Exp(-t * 2.8f) * 0.94f +
                             filteredNoise * debris * Mathf.Exp(-t * 3f) * 0.54f;
                    break;
                default: // Red Energy Burst: synthetic bass punch with a harmonic discharge.
                    float energyBoom = Mathf.Sin(2f * Mathf.PI * (105f * t - 43f * t * t));
                    float discharge = Mathf.Sin(2f * Mathf.PI * 420f * t + 2.2f * Mathf.Sin(2f * Mathf.PI * 31f * t));
                    sample = energyBoom * Mathf.Exp(-t * 5f) * 0.82f +
                             discharge * Mathf.Exp(-t * 8f) * 0.58f + noise * Mathf.Exp(-t * 34f) * 0.52f;
                    break;
            }

            samples[i] = sample * Mathf.Pow(1f - p, 0.18f);
        }

        return PunchUp(samples, 0.99f, 0.72f);
    }

    private static float BuildWinnerExcerpt()
    {
        const string sourcePath = BgmFolder + "/winner_bgm.mp3";
        const string outputPath = BgmFolder + "/WinnerBGM_30s.wav";
        AudioImporter importer = AssetImporter.GetAtPath(sourcePath) as AudioImporter;
        if (importer != null)
        {
            AudioImporterSampleSettings sourceSettings = importer.defaultSampleSettings;
            if (!sourceSettings.preloadAudioData || importer.loadInBackground)
            {
                sourceSettings.preloadAudioData = true;
                importer.defaultSampleSettings = sourceSettings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }
        }

        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(sourcePath);
        if (clip == null)
        {
            throw new InvalidOperationException("Winner BGM is missing: " + sourcePath);
        }

        clip.LoadAudioData();
        float[] source = new float[clip.samples * clip.channels];
        if (!clip.GetData(source, 0))
        {
            throw new InvalidOperationException("Could not decode winner_bgm.mp3.");
        }

        int excerptFrames = Mathf.Min(clip.samples, Mathf.RoundToInt(30f * clip.frequency));
        int startFrame = FindBestWinnerStart(source, clip.samples, clip.channels, clip.frequency, excerptFrames);
        float[] excerpt = new float[excerptFrames * clip.channels];
        Array.Copy(source, startFrame * clip.channels, excerpt, 0, excerpt.Length);
        ApplyStereoFade(excerpt, clip.channels, clip.frequency, 0.08f, 1.15f);
        Normalize(excerpt, 0.94f);
        WriteWav(outputPath, excerpt, clip.channels, clip.frequency);
        return startFrame / (float)clip.frequency;
    }

    private static int FindBestWinnerStart(float[] data, int frames, int channels, int frequency, int excerptFrames)
    {
        if (frames <= excerptFrames)
        {
            return 0;
        }

        int earliest = Mathf.Min(frames - excerptFrames, frequency * 2);
        int latest = frames - excerptFrames;
        int stride = Mathf.Max(1, frequency / 2);
        int sampleStride = 96;
        double bestScore = double.MinValue;
        int bestStart = earliest;
        for (int start = earliest; start <= latest; start += stride)
        {
            double energy = 0d;
            double peak = 0d;
            double openingEnergy = 0d;
            int count = 0;
            int openingCount = 0;
            for (int frame = start; frame < start + excerptFrames; frame += sampleStride)
            {
                double value = 0d;
                for (int channel = 0; channel < channels; channel++)
                {
                    value += Math.Abs(data[frame * channels + channel]);
                }

                value /= channels;
                energy += value * value;
                peak = Math.Max(peak, value);
                count++;
                if (frame < start + frequency * 3)
                {
                    openingEnergy += value * value;
                    openingCount++;
                }
            }

            double rms = Math.Sqrt(energy / Math.Max(1, count));
            double openingRms = Math.Sqrt(openingEnergy / Math.Max(1, openingCount));
            double score = rms + peak * 0.08d + openingRms * 0.22d - start / (double)frequency * 0.0003d;
            if (score > bestScore)
            {
                bestScore = score;
                bestStart = start;
            }
        }

        return bestStart;
    }

    private static void ConfigureSoundManagerPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(SoundManagerPrefabPath);
        try
        {
            CSE.SoundManager manager = root.GetComponent<CSE.SoundManager>();
            AudioSource[] sources = root.GetComponents<AudioSource>();
            AudioSource bgm = sources.Length > 0 ? sources[0] : root.AddComponent<AudioSource>();
            AudioSource sfx = sources.Length > 1 ? sources[1] : root.AddComponent<AudioSource>();
            AudioSource movement = sources.Length > 2 ? sources[2] : root.AddComponent<AudioSource>();
            bgm.playOnAwake = false;
            bgm.loop = true;
            bgm.spatialBlend = 0f;
            sfx.playOnAwake = false;
            sfx.loop = false;
            sfx.spatialBlend = 0f;
            movement.playOnAwake = false;
            movement.loop = false;
            movement.spatialBlend = 0f;
            movement.dopplerLevel = 0f;

            Transform spatialRoot = root.transform.Find("SpatialSfxSources");
            if (spatialRoot == null)
            {
                GameObject spatialRootObject = new GameObject("SpatialSfxSources");
                spatialRootObject.transform.SetParent(root.transform, false);
                spatialRoot = spatialRootObject.transform;
            }

            const int spatialSourceCount = 8;
            AudioSource[] spatialSources = new AudioSource[spatialSourceCount];
            for (int i = 0; i < spatialSourceCount; i++)
            {
                string sourceName = "SpatialSfxSource_" + (i + 1).ToString("00");
                Transform sourceTransform = spatialRoot.Find(sourceName);
                if (sourceTransform == null)
                {
                    GameObject sourceObject = new GameObject(sourceName);
                    sourceObject.transform.SetParent(spatialRoot, false);
                    sourceTransform = sourceObject.transform;
                }

                AudioSource source = sourceTransform.GetComponent<AudioSource>();
                if (source == null)
                {
                    source = sourceTransform.gameObject.AddComponent<AudioSource>();
                }

                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 3.5f;
                source.maxDistance = 12f;
                source.dopplerLevel = 0f;
                source.spread = 35f;
                spatialSources[i] = source;
            }

            SerializedObject serialized = new SerializedObject(manager);
            SetObject(serialized, "loginAndLobbyBgm", BgmFolder + "/LobbyBGM.mp3");
            SetObject(serialized, "gameBgm", BgmFolder + "/GameBGM.mp3");
            SetObject(serialized, "finalCountdownBgm", BgmFolder + "/LastDanceBGM.mp3");
            SetObject(serialized, "nextRoundBgm", BgmFolder + "/NextRoundBGM.mp3");
            SetObject(serialized, "winnerBgm", BgmFolder + "/WinnerBGM_30s.wav");
            serialized.FindProperty("bgmSource").objectReferenceValue = bgm;
            serialized.FindProperty("sfxSource").objectReferenceValue = sfx;
            serialized.FindProperty("movementSfxSource").objectReferenceValue = movement;
            serialized.FindProperty("sfxVolume").floatValue = 1f;
            serialized.FindProperty("spatialSfxMinDistance").floatValue = 3.5f;
            serialized.FindProperty("spatialSfxMaxDistance").floatValue = 12f;
            SerializedProperty spatialProperty = serialized.FindProperty("spatialSfxSources");
            spatialProperty.arraySize = spatialSources.Length;
            for (int i = 0; i < spatialSources.Length; i++)
            {
                spatialProperty.GetArrayElementAtIndex(i).objectReferenceValue = spatialSources[i];
            }
            SetObject(serialized, "movementStepClip", SfxFolder + "/movement_step_02.wav");
            SetObject(serialized, "teleportClip", SfxFolder + "/teleport.wav");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, SoundManagerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureWeaponProfiles()
    {
        for (int i = 0; i < SkillIds.Length; i++)
        {
            string profilePath = "Assets/LGH/Data/WeaponVfxProfiles/" + SkillIds[i] + "_vfx.asset";
            MagicWeaponVfxProfile profile = AssetDatabase.LoadAssetAtPath<MagicWeaponVfxProfile>(profilePath);
            if (profile == null)
            {
                Debug.LogWarning("[GameplayAudioSetup] Missing profile: " + profilePath);
                continue;
            }

            SerializedObject serialized = new SerializedObject(profile);
            SetObject(serialized, "markerAudioClip", SfxFolder + "/" + SkillIds[i] + "_marker.wav");
            SetObject(serialized, "impactAudioClip", SfxFolder + "/" + SkillIds[i] + "_impact.wav");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }
    }

    private static void ConfigureAudioImporters()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { SfxFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                continue;
            }

            importer.forceToMono = true;
            importer.loadInBackground = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.preloadAudioData = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.72f;
            settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureWinnerImporter()
    {
        const string path = BgmFolder + "/WinnerBGM_30s.wav";
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null)
        {
            return;
        }

        importer.forceToMono = false;
        importer.loadInBackground = true;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.Streaming;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.82f;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = false;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
    }

    private static void SetObject(SerializedObject serialized, string propertyName, string assetPath)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException("Missing serialized property: " + propertyName);
        }

        property.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
    }

    private static float SmoothAttack(float time, float attackSeconds)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / Mathf.Max(0.0001f, attackSeconds)));
    }

    private static float[] Normalize(float[] samples, float targetPeak)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        }

        if (peak <= 0.00001f)
        {
            return samples;
        }

        float gain = targetPeak / peak;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
        }

        return samples;
    }

    private static void LimitPeak(float[] samples, float maximumPeak)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        }

        if (peak <= maximumPeak || peak <= 0.00001f)
        {
            return;
        }

        float gain = maximumPeak / peak;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }
    }

    private static float[] PunchUp(float[] samples, float targetPeak, float compressionExponent)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float value = samples[i];
            float magnitude = Mathf.Pow(Mathf.Abs(value), Mathf.Clamp(compressionExponent, 0.5f, 1f));
            samples[i] = Mathf.Sign(value) * magnitude;
        }

        return Normalize(samples, targetPeak);
    }

    private static void ApplyStereoFade(float[] samples, int channels, int frequency, float fadeIn, float fadeOut)
    {
        int frames = samples.Length / channels;
        int fadeInFrames = Mathf.Min(frames, Mathf.RoundToInt(fadeIn * frequency));
        int fadeOutFrames = Mathf.Min(frames, Mathf.RoundToInt(fadeOut * frequency));
        for (int frame = 0; frame < frames; frame++)
        {
            float gain = 1f;
            if (frame < fadeInFrames)
            {
                gain *= frame / (float)Mathf.Max(1, fadeInFrames);
            }

            int remaining = frames - 1 - frame;
            if (remaining < fadeOutFrames)
            {
                gain *= remaining / (float)Mathf.Max(1, fadeOutFrames);
            }

            for (int channel = 0; channel < channels; channel++)
            {
                samples[frame * channels + channel] *= gain;
            }
        }
    }

    private static void WriteMonoWav(string path, float[] samples)
    {
        WriteWav(path, samples, 1, SampleRate);
    }

    private static void WriteWav(string path, float[] samples, int channels, int frequency)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? SfxFolder);
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int dataSize = samples.Length * sizeof(short);
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(frequency);
            writer.Write(frequency * channels * sizeof(short));
            writer.Write((short)(channels * sizeof(short)));
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);
            for (int i = 0; i < samples.Length; i++)
            {
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue));
            }
        }
    }
}
