using System.Collections.Generic;
using UnityEngine;

/// <summary>효과음 이름 (Assets/UI/Resources/Sounds/sfx_&lt;이름&gt;.mp3)</summary>
public static class Sfx
{
    public const string UiHover = "ui_hover";
    public const string UiSelect = "ui_select";
    public const string UiBack = "ui_back";
    public const string Locked = "locked";
    public const string PauseOpen = "pause_open";
    public const string Land = "land";
    public const string StageIntro = "stage_intro";
    public const string CountTick = "count_tick";
    public const string CountGo = "count_go";
    public const string TileCapture = "tile_capture";
    public const string BoxPush = "box_push";
    public const string TimeWarning = "time_warning";
    public const string ClearJingle = "clear_jingle";
    public const string Died = "died";
    public const string Launch = "launch";
}

/// <summary>
/// 효과음 재생. 어디서나 UISound.Play(Sfx.UiSelect) 한 줄.
/// - 소리 파일: Resources/Sounds/sfx_&lt;이름&gt; (Addressables 와 무관하게 바로 불러온다)
/// - 씬이 바뀌어도 살아 있는 재생기 하나에 AudioSource 여러 개를 돌려 쓴다 (겹쳐 울려도 끊기지 않게)
/// - timeScale 0 (일시정지, 카운트다운) 에도 재생된다
/// - 전체 음량 Volume 은 나중에 설정창 효과음 칸에 연결한다
/// </summary>
public static class UISound
{
    /// <summary>효과음 전체 음량 (0~1) = 설정창 "효과음" 칸 (SoundSettings 에 저장)</summary>
    public static float Volume => SoundSettings.Sfx01;

    private const int SOURCE_COUNT = 8;
    private const float SAME_SOUND_GAP = 0.03f; // 같은 소리가 이 시간 안에 또 오면 무시 (호버 연타 등)

    // 소리마다 기본 크기 — 자주 나는 소리는 작게
    private static readonly Dictionary<string, float> s_baseVolume = new Dictionary<string, float>
    {
        { Sfx.UiHover, 0.35f }, { Sfx.UiSelect, 0.7f }, { Sfx.UiBack, 0.6f }, { Sfx.Locked, 0.7f },
        { Sfx.PauseOpen, 0.6f }, { Sfx.Land, 0.8f }, { Sfx.StageIntro, 0.7f }, { Sfx.CountTick, 0.7f },
        { Sfx.CountGo, 0.8f }, { Sfx.TileCapture, 0.45f }, { Sfx.BoxPush, 0.6f }, { Sfx.TimeWarning, 0.6f },
        { Sfx.ClearJingle, 0.9f }, { Sfx.Died, 0.9f }, { Sfx.Launch, 0.8f },
    };

    private static readonly Dictionary<string, AudioClip> s_clips = new Dictionary<string, AudioClip>();
    private static readonly Dictionary<string, float> s_lastPlayed = new Dictionary<string, float>();
    private static AudioSource[] s_sources;
    private static int s_next;

    /// <param name="pitchJitter">음 높이를 ±이 비율만큼 무작위로 (예: 0.08 = ±8%). 자주 나는 소리가 덜 반복적으로 들린다</param>
    public static void Play(string name, float pitchJitter = 0f, float volumeScale = 1f)
    {
        if (string.IsNullOrEmpty(name) || Volume <= 0f)
            return;

        float now = Time.unscaledTime;
        if (s_lastPlayed.TryGetValue(name, out float last) && now - last < SAME_SOUND_GAP)
            return;
        s_lastPlayed[name] = now;

        AudioClip clip = GetClip(name);
        if (clip == null)
            return;

        AudioSource source = NextSource();
        if (source == null)
            return;

        float baseVolume = s_baseVolume.TryGetValue(name, out float v) ? v : 0.7f;
        source.pitch = pitchJitter > 0f ? 1f + Random.Range(-pitchJitter, pitchJitter) : 1f;
        source.PlayOneShot(clip, baseVolume * volumeScale * Volume);
    }

    private static AudioClip GetClip(string name)
    {
        if (s_clips.TryGetValue(name, out AudioClip clip))
            return clip;

        clip = Resources.Load<AudioClip>("Sounds/sfx_" + name);
        if (clip == null)
            LogPrinter.LogWarning($"[UISound] 소리 없음: Resources/Sounds/sfx_{name}");
        s_clips[name] = clip; // 없어도 기록해서 매번 찾지 않는다
        return clip;
    }

    private static AudioSource NextSource()
    {
        if (s_sources == null || s_sources[0] == null)
        {
            var go = new GameObject("@UISound");
            Object.DontDestroyOnLoad(go);
            s_sources = new AudioSource[SOURCE_COUNT];
            for (int i = 0; i < SOURCE_COUNT; i++)
            {
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;   // 2D
                s.ignoreListenerPause = true;
                s_sources[i] = s;
            }
            s_next = 0;
        }

        AudioSource source = s_sources[s_next];
        s_next = (s_next + 1) % SOURCE_COUNT;
        return source;
    }
}
