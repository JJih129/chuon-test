// 반복 16: 개편 공격 큐 정직성 측정. 큐가 공지한 "판정까지 남은 시간"과 실제 판정 시각의 차이를 공격별로 모은다.
// 목표 오차 ≤ 0.03 s (Docs/Roadmap_Loop.md 출시 게이트). QA: OverhaulCueLedger.Report().
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class OverhaulCueLedger
{
    struct Pending
    {
        public string Attack;
        public float AnnouncedAt;
        public float Lead;
        public bool Announced;
    }

    static readonly Dictionary<int, Pending> s_pending = new Dictionary<int, Pending>();
    static readonly Dictionary<string, List<float>> s_errors = new Dictionary<string, List<float>>();
    static readonly List<int> s_buffer = new List<int>();
    static int s_next = 1;

    public static int Samples { get; private set; }
    public static float MaxAbsError { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Reset();

    public static void Reset()
    {
        s_pending.Clear();
        s_errors.Clear();
        Samples = 0;
        MaxAbsError = 0f;
    }

    /// <summary>새 큐 토큰 (공지는 Announce 로).</summary>
    public static int Begin(string attack)
    {
        int id = s_next++;
        s_pending[id] = new Pending { Attack = attack ?? "?" };
        return id;
    }

    public static void Announce(int token, float secondsToHit)
    {
        if (!s_pending.TryGetValue(token, out Pending p))
            return;
        p.AnnouncedAt = Time.time;
        p.Lead = Mathf.Max(0f, secondsToHit);
        p.Announced = true;
        s_pending[token] = p;
    }

    public static void Cancel(int token) => s_pending.Remove(token);

    /// <summary>이 공격의 공지된 모든 큐를 지금 판정된 것으로 기록.</summary>
    public static void ImpactAll(string attack)
    {
        float now = Time.time;
        s_buffer.Clear();
        foreach (KeyValuePair<int, Pending> kv in s_pending)
            if (kv.Value.Attack == attack)
                s_buffer.Add(kv.Key);
        for (int i = 0; i < s_buffer.Count; i++)
        {
            Pending p = s_pending[s_buffer[i]];
            s_pending.Remove(s_buffer[i]);
            if (!p.Announced)
                continue;
            float error = (now - p.AnnouncedAt) - p.Lead;
            if (!s_errors.TryGetValue(attack, out List<float> list))
                s_errors[attack] = list = new List<float>();
            list.Add(error);
            Samples++;
            MaxAbsError = Mathf.Max(MaxAbsError, Mathf.Abs(error));
        }
    }

    /// <summary>외부(봉쇄 터렛 등) 측정값을 직접 기록.</summary>
    public static void Record(string attack, float error)
    {
        if (!s_errors.TryGetValue(attack, out List<float> list))
            s_errors[attack] = list = new List<float>();
        list.Add(error);
        Samples++;
        MaxAbsError = Mathf.Max(MaxAbsError, Mathf.Abs(error));
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        foreach (KeyValuePair<string, List<float>> kv in s_errors)
        {
            float sum = 0f, max = 0f;
            foreach (float e in kv.Value)
            {
                sum += e;
                max = Mathf.Max(max, Mathf.Abs(e));
            }
            sb.Append(kv.Key).Append(": n=").Append(kv.Value.Count)
              .Append(" meanErr=").Append((sum / Mathf.Max(1, kv.Value.Count)).ToString("+0.000;-0.000"))
              .Append(" maxAbsErr=").Append(max.ToString("0.000")).Append('\n');
        }
        sb.Append("samples=").Append(Samples).Append(" maxAbs=").Append(MaxAbsError.ToString("0.000"));
        return sb.ToString();
    }
}
