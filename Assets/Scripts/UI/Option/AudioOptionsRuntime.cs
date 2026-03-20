using UnityEngine;

public static class AudioOptionsRuntime
{
    const string K_MASTER = "opt_master";
    const string K_MUSIC = "opt_music";
    const string K_SFX = "opt_sfx";
    const string K_VOICE = "opt_voice";

    static bool _initialized;
    static float _masterVolume = 1f;
    static float _musicVolume = 1f;
    static float _sfxVolume = 1f;
    static float _voiceVolume = 1f;

    public static float MasterVolume
    {
        get { EnsureInitialized(); return _masterVolume; }
    }

    public static float MusicVolume
    {
        get { EnsureInitialized(); return _musicVolume; }
    }

    public static float SfxVolume
    {
        get { EnsureInitialized(); return _sfxVolume; }
    }

    public static float VoiceVolume
    {
        get { EnsureInitialized(); return _voiceVolume; }
    }

    public static void RefreshFromPrefs()
    {
        _masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(K_MASTER, 1f));
        _musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(K_MUSIC, 1f));
        _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(K_SFX, 1f));
        _voiceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(K_VOICE, 1f));
        _initialized = true;
        AudioListener.volume = _masterVolume;
    }

    public static void SetMasterVolume(float value)
    {
        EnsureInitialized();
        _masterVolume = Mathf.Clamp01(value);
        AudioListener.volume = _masterVolume;
    }

    public static void SetMusicVolume(float value)
    {
        EnsureInitialized();
        _musicVolume = Mathf.Clamp01(value);
    }

    public static void SetSfxVolume(float value)
    {
        EnsureInitialized();
        _sfxVolume = Mathf.Clamp01(value);
    }

    public static void SetVoiceVolume(float value)
    {
        EnsureInitialized();
        _voiceVolume = Mathf.Clamp01(value);
    }

    public static float ScaleMusic(float baseVolume)
    {
        EnsureInitialized();
        return Mathf.Clamp01(baseVolume) * _musicVolume;
    }

    public static float ScaleSfx(float baseVolume)
    {
        EnsureInitialized();
        return Mathf.Clamp01(baseVolume) * _sfxVolume;
    }

    public static float ScaleVoice(float baseVolume)
    {
        EnsureInitialized();
        return Mathf.Clamp01(baseVolume) * _voiceVolume;
    }

    static void EnsureInitialized()
    {
        if (_initialized)
            return;

        RefreshFromPrefs();
    }
}
