using UnityEngine;

// ============================== ▼ 변수 헤더(튜닝 가이드) ▼ ==============================
// [애니 파라미터 연결]
// animator : 애니메이터
// paramSpeed : 속도 파라미터명 ("Speed")
// paramMoveX : 좌우 파라미터명 ("MoveX")
// paramMoveY : 전후 파라미터명 ("MoveY")
//
// [입력/속도 소스]
// useInputAxes : true면 입력 축으로, false면 실제 속도로 계산
// inputSmoothing : 입력/속도 -> 파라미터 보간 시간(부드러움 조절)
// ========================================================================================

[DisallowMultipleComponent]
public class GuardLocomotionAnimatorBridge : MonoBehaviour
{
    [Header("▶ Animator Params")]
    public Animator animator;
    public string paramSpeed = "Speed";
    public string paramMoveX = "MoveX";
    public string paramMoveY = "MoveY";

    [Header("▶ Source")]
    public bool useInputAxes = true;
    [Range(0f, 0.2f)] public float inputSmoothing = 0.05f;

    // 입력 기반
    float curX, curY;

    // (속도 기반용) 실제 월드 속도 참조가 있다면 연결
    public Rigidbody rb;              // 리지드바디 사용 시
    public CharacterController cc;    // CC 사용 시
    public Transform cameraPivot;     // 카메라 기준으로 로컬화 원할 때

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!cameraPivot) cameraPivot = Camera.main ? Camera.main.transform : null;
    }

    void Update()
    {
        float targetX = 0f, targetY = 0f;

        if (useInputAxes)
        {
            // 프로젝트 입력 체계에 맞게 치환
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            // 카메라 기준 로컬화(선택)
            Vector3 input = new Vector3(h, 0, v);
            if (cameraPivot)
            {
                Vector3 fwd = cameraPivot.forward; fwd.y = 0; fwd.Normalize();
                Vector3 right = cameraPivot.right; right.y = 0; right.Normalize();
                Vector3 world = fwd * input.z + right * input.x;

                // 플레이어 로컬로 변환
                Vector3 local = transform.InverseTransformDirection(world);
                targetX = Mathf.Clamp(local.x, -1f, 1f);
                targetY = Mathf.Clamp(local.z, -1f, 1f);
            }
            else
            {
                targetX = Mathf.Clamp(h, -1f, 1f);
                targetY = Mathf.Clamp(v, -1f, 1f);
            }
        }
        else
        {
            // 속도 기반 (RB/CC 중 하나 사용)
            Vector3 vel = Vector3.zero;
            if (rb) vel = rb.velocity;
            else if (cc) vel = cc.velocity;

            // 카메라 기준 → 플레이어 로컬
            Vector3 local = transform.InverseTransformDirection(vel);
            float maxSpeed = 5f; // 프로젝트 이동 최고속도에 맞춰 정규화
            targetX = Mathf.Clamp(local.x / maxSpeed, -1f, 1f);
            targetY = Mathf.Clamp(local.z / maxSpeed, -1f, 1f);
        }

        // 부드럽게
        curX = Mathf.MoveTowards(curX, targetX, Time.deltaTime / Mathf.Max(0.0001f, inputSmoothing));
        curY = Mathf.MoveTowards(curY, targetY, Time.deltaTime / Mathf.Max(0.0001f, inputSmoothing));

        float speed = Mathf.Clamp01(new Vector2(curX, curY).magnitude);

        animator.SetFloat(paramMoveX, curX);
        animator.SetFloat(paramMoveY, curY);
        animator.SetFloat(paramSpeed, speed);
    }
}
