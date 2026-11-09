using System;
using UnityEngine;

// 골목 끝 보안 스캐너 빔 커튼 (회피 시험). 걸어서 지나가면 감지(소량 피해 + 뒤로 밀림), 회피 무적 프레임으로 굴러 지나가면 통과.
// 판정은 PlayerDamageReceiver 경로 그대로 (회피 무적이면 피해 무시) — 회피 규칙을 새로 만들지 않는다.
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class InfScannerCurtain : MonoBehaviour
{
    [SerializeField] Renderer[] beams = Array.Empty<Renderer>();
    [SerializeField, Min(0f)] float damage = 6f;
    [SerializeField, Min(0f)] float pushBack = 1.6f;
    [SerializeField, Min(0.1f)] float rehitCooldown = 0.9f;
    [SerializeField] AudioClip alarmSfx;
    [SerializeField] bool armed = true;

    public event Action<bool> PlayerCrossed; // true = 감지됨(피격), false = 깨끗하게 통과
    public int Detections { get; private set; }
    public int CleanPasses { get; private set; }

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    MaterialPropertyBlock _mpb;
    float _nextHitAt;
    float _flashUntil;
    bool _playerInside;
    bool _hitThisPass;
    float _enterSide;

    public void Configure(Renderer[] beamRenderers) => beams = beamRenderers ?? Array.Empty<Renderer>();

    public bool Armed
    {
        get => armed;
        set { armed = value; foreach (Renderer r in beams) if (r != null) r.enabled = value; }
    }

    void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        Armed = armed;
    }

    void Update()
    {
        if (!armed || beams.Length == 0)
            return;
        _mpb ??= new MaterialPropertyBlock();
        bool flash = Time.time < _flashUntil;
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 9f);
        for (int i = 0; i < beams.Length; i++)
        {
            Renderer r = beams[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            float flick = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 6f, i * 3.1f);
            Color c = flash ? new Color(1f, 0.35f, 0.25f, 1f) : new Color(1f, 0.08f, 0.06f, 0.85f * pulse * flick);
            _mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_mpb);
        }
    }

    static bool IsPlayer(Collider other, out Transform root)
    {
        root = other != null ? other.transform.root : null;
        return root != null && (other.CompareTag("Player") || root.CompareTag("Player"));
    }

    float Side(Vector3 p) => Vector3.Dot(p - transform.position, transform.forward);

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other, out Transform root))
            return;
        _playerInside = true;
        _hitThisPass = false;
        _enterSide = Mathf.Sign(Side(root.position - transform.forward * 0.01f));
    }

    void OnTriggerStay(Collider other)
    {
        if (!armed || !IsPlayer(other, out Transform root) || Time.time < _nextHitAt)
            return;

        PlayerDodgeController dodge = root.GetComponent<PlayerDodgeController>();
        if (dodge != null && dodge.IsDodging)
            return; // 굴러서 통과 중 = 스캔 회피

        PlayerHealth health = root.GetComponent<PlayerHealth>();
        int before = health != null ? health.CurrentHP : 0;
        IDamageReceiver receiver = root.GetComponent<IDamageReceiver>();
        receiver?.ReceiveHit(new HitPayload
        {
            damage = damage,
            hitType = HitType.Normal,
            hitPoint = root.position + Vector3.up * 1.1f,
            hitDirection = transform.forward * -Mathf.Sign(Side(root.position)),
            attacker = transform,
            canGuard = false,
            canParry = false,
            canPerfectDodge = false,
            unblockable = true
        });
        if (health != null && health.CurrentHP >= before)
            return; // 무적 프레임 등으로 무시됨

        _nextHitAt = Time.time + rehitCooldown;
        _flashUntil = Time.time + 0.35f;
        _hitThisPass = true;
        Detections++;
        if (alarmSfx != null)
            OneShotAudio.Play2D(alarmSfx, 0.55f);

        // 들어온 쪽으로 밀어낸다 (감지 = 통과 실패)
        CharacterController cc = root.GetComponent<CharacterController>();
        Vector3 back = transform.forward * (_enterSide >= 0f ? 1f : -1f);
        if (Mathf.Abs(Side(root.position)) < 0.01f)
            back = -transform.forward;
        Vector3 target = root.position + back * pushBack;
        target += back * Mathf.Max(0f, 0.6f - Mathf.Abs(Side(target)));
        if (cc != null) cc.enabled = false;
        root.position = new Vector3(target.x, root.position.y, target.z);
        if (cc != null) cc.enabled = true;
        PlayerCrossed?.Invoke(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other, out Transform root) || !_playerInside)
            return;
        _playerInside = false;
        float exitSide = Mathf.Sign(Side(root.position));
        if (!_hitThisPass && exitSide != _enterSide)
        {
            CleanPasses++;
            PlayerCrossed?.Invoke(false);
        }
    }
}
