using System;
using UnityEngine;

/// <summary>
/// 배경음 / 효과음 음량 (0~100, 10 단위 = 설정창 볼륨 칸 10개).
/// PlayerPrefs 에 저장한다 — 게임을 껐다 켜도 유지 (팀 저장 파일 구조는 건드리지 않음).
///   효과음: UISound 가 Sfx01 을 곱해서 쓴다
///   배경음: 배경음악을 넣을 때 Bgm01 을 쓰고, Changed 로 바뀐 값을 바로 반영한다
/// </summary>
public static class SoundSettings
{
    private const string KEY_BGM = "SokoEat.Volume.Bgm";
    private const string KEY_SFX = "SokoEat.Volume.Sfx";
    private const int DEFAULT_VOLUME = 70;

    /// <summary>음량이 바뀌었을 때 (배경음 재생기 등이 구독)</summary>
    public static event Action Changed;

    private static bool s_loaded;
    private static int s_bgm = DEFAULT_VOLUME;
    private static int s_sfx = DEFAULT_VOLUME;

    public static int Bgm { get { Load(); return s_bgm; } set { Set(ref s_bgm, KEY_BGM, value); } }
    public static int Sfx { get { Load(); return s_sfx; } set { Set(ref s_sfx, KEY_SFX, value); } }

    public static float Bgm01 => Bgm / 100f;
    public static float Sfx01 => Sfx / 100f;

    private static void Load()
    {
        if (s_loaded)
            return;
        s_loaded = true;
        s_bgm = Mathf.Clamp(PlayerPrefs.GetInt(KEY_BGM, DEFAULT_VOLUME), 0, 100);
        s_sfx = Mathf.Clamp(PlayerPrefs.GetInt(KEY_SFX, DEFAULT_VOLUME), 0, 100);
    }

    private static void Set(ref int field, string key, int value)
    {
        Load();
        value = Mathf.Clamp(Mathf.RoundToInt(value / 10f) * 10, 0, 100); // 10 단위
        if (field == value)
            return;
        field = value;
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
