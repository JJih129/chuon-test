// 반복 16: Type-D 개편 런타임 에셋 묶음. TypeDOverhaulBuilder(에디터)가 Resources/TypeD/TypeD_OverhaulAssets.asset 로 만든다.
// Blender 모델(전원 노드·셔터·엄폐 블록·바이저 파편·팔 칼날), 기존 잡몹 프리팹, VFX, 생성 효과음. 비어 있으면 코드가 단순 도형으로 대체한다.
using UnityEngine;

[CreateAssetMenu(menuName = "ChuOn/Type-D Overhaul Assets", fileName = "TypeD_OverhaulAssets")]
public sealed class TypeDOverhaulAssets : ScriptableObject
{
    public const string ResourcePath = "TypeD/TypeD_OverhaulAssets";

    [Header("Blender models (Tools/ArtGen/typed_overhaul_blender.py)")]
    public GameObject powerNodeModel;
    public GameObject powerNodeBrokenModel;
    public GameObject shutterModel;
    public GameObject coverModel;
    public GameObject visorShardModel;
    public GameObject armBladeModel;
    public GameObject bladeShardModel;

    [Header("Lockdown mobs (Assets/Prefabs/Mobs, 공용 공격 토큰 AI)")]
    public GameObject sentryTurretPrefab;
    public GameObject patrolDronePrefab;

    [Header("VFX")]
    public GameObject dustVfx;
    public GameObject sparkVfx;
    public GameObject steamVfx;
    public GameObject coreBurstVfx;
    public GameObject blinkVfx;

    [Header("SFX (Tools/ArtGen/typed_overhaul_audio.py)")]
    public AudioClip wallSplat;
    public AudioClip impactHeavy;
    public AudioClip nodeHit;
    public AudioClip nodeBreak;
    public AudioClip shutterDrop;
    public AudioClip shutterRise;
    public AudioClip lockdownAlarm;
    public AudioClip ventCharge;
    public AudioClip ventBlast;
    public AudioClip despCharge;
    public AudioClip despBlast;
    public AudioClip strongParry;
    public AudioClip blink;
    public AudioClip execSlash;
    public AudioClip execFinal;
    public AudioClip partBreak;
    public AudioClip armourPurge;
    public AudioClip grabWhoosh;
    public AudioClip coverRise;

    static TypeDOverhaulAssets s_cached;
    static bool s_loaded;

    public static TypeDOverhaulAssets Load()
    {
        if (!s_loaded)
        {
            s_loaded = true;
            s_cached = Resources.Load<TypeDOverhaulAssets>(ResourcePath);
        }
        return s_cached;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_loaded = false;
        s_cached = null;
    }

    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 0.9f, float pitch = 1f)
    {
        if (clip != null)
            OneShotAudio.PlayAt(clip, position, volume, pitch, 0.4f);
    }
}
