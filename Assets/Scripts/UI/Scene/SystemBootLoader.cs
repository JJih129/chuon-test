using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro; 
using System.Collections;
using System.Text; 
using DG.Tweening; // DOTween 필수

public class SystemBootLoader : MonoBehaviour
{
    [Header("■ 설정")]
    public string nextSceneName = "LobbyScene"; 
    public float typingSpeed = 0.02f; 

    [Header("■ UI 연결")]
    public TextMeshProUGUI logText;      
    public TextMeshProUGUI progressText; 
    public Slider progressBar;           

    private string[] bootLogs = new string[]
    {
        "[0x000000] BIOS_CHECK :: Verifying hardware integrity signatures...",
        "[0x00001A] POWER_MNGT :: Initializing fusion core cells... [OUTPUT: 120%]",
        "[0x00004F] MEMORY_ALLOC :: Mounting virtual heap (0x7FFF0000 - 0xFFFFFFFF)... [OK]",
        "> Loading Kernel: EGO_OS_KERNEL_v4.9.223_rev14 (Build Date: 2077-11-20)",
        "> Mount Volume: /dev/sda1 [Filesystem: ZFS_Quantum_Encrypted]",
        "[0x00012C] DRIVER_LOAD :: Neural_Interface_Adapter... [SUCCESS]",
        "[0x00014D] DRIVER_LOAD :: Ocular_Visual_Sensor_Array_v9... [CALIBRATED]",
        "[0x00021F] SYSTEM_CHECK :: Scanning biometrics... ID Verified: CHUON.",
        ">>> INITIATING NEURAL LINK SYNCHRONIZATION SEQUENCE <<<",
        "[0x0004A1] NET_SECURE :: Decrypting military-grade firewall (Layer 7)... [BYPASSED]",
        "[0x0005B2] NET_SECURE :: Establishing uplink to EGO_Mainframe... [CONNECTED]",
        "Loading module: 'Combat_Heuristics_Pack.dll'... [100%]",
        "Loading module: 'Advanced_Movement_Algorithms.lib'... [100%]",
        "Loading module: 'Weapon_Mastery_Database.db'... [100%]",
        "WARNING: Emotional Inhibitor chip not detected. Proceeding with caution.",
        "[0x0008F4] OPTIMIZATION :: Pre-caching shader pipelines... [COMPLETED]",
        "[0x0009A1] PHYSICS_ENG :: Calibrating rigid body dynamics... [OK]",
        "Checking peripheral devices... Right_Arm_Blade [ONLINE], Left_Arm_Shield [ONLINE].",
        "Synchronizing cognitive data with local storage... [SYNC OK]",
        "Applying user configuration profile: 'ASSAULT_MODE'.",
        "System diagnostics complete. All subsystems nominal.",
        ">>> ACCESS GRANTED. WELCOME BACK, OPERATOR.",
        "SYSTEM READY."
    };

    void Start()
    {
        StartCoroutine(BootSequence());
    }

    IEnumerator BootSequence()
    {
        // 1. 비동기 씬 로딩 시작 (배경에서 몰래 로딩)
        AsyncOperation op = SceneManager.LoadSceneAsync(nextSceneName);
        op.allowSceneActivation = false; // 바로 넘어가지 않게 막음

        StringBuilder sb = new StringBuilder();
        int totalLogs = bootLogs.Length;
        
        // 2. 로그 출력 연출
        for (int i = 0; i < totalLogs; i++)
        {
            sb.AppendLine(bootLogs[i]);
            logText.text = sb.ToString();

            // 맨 밑으로 스크롤 유지 (선택사항: 정렬을 Top-Left로 했다면 자연스럽게 됨)
            
            float progress = (float)(i + 1) / totalLogs; 
            
            if (progressBar) progressBar.value = progress;
            if (progressText) progressText.text = $"SYSTEM BOOT... {(progress * 100):0}%";

            yield return new WaitForSeconds(Random.Range(typingSpeed * 0.5f, typingSpeed * 1.5f));
        }

        // 3. 완료 상태 보여주기
        if (progressText) progressText.text = "SYSTEM BOOT... 100%";
        if (progressBar) progressBar.value = 1.0f;
        
        yield return new WaitForSeconds(0.5f); // 잠시 대기

        // 4. 실제 로딩이 끝날 때까지 대기
        while (op.progress < 0.9f)
        {
            yield return null;
        }

        // ★ [추가됨] 페이드 아웃 후 씬 이동
        // SceneFader가 있으면 화면을 검게 만들고 나서 이동을 허락함
        if (SceneFader.Instance != null && SceneFader.Instance.fadeCanvasGroup != null)
        {
            // 검은 화면(Alpha 1)으로 1초 동안 변해라
            SceneFader.Instance.fadeCanvasGroup.blocksRaycasts = true;
            SceneFader.Instance.fadeCanvasGroup
                .DOFade(1f, 1.0f) 
                .OnComplete(() => 
                {
                    // 페이드가 다 끝나면 그때 씬 이동 허용!
                    op.allowSceneActivation = true; 
                });
        }
        else
        {
            // 페이더가 없으면 그냥 바로 이동
            op.allowSceneActivation = true;
        }
    }
}