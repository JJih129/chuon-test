using UnityEngine;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    [Header("Settings")]
    public bool lockOnStart = true;
    public bool escToggle = true;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (lockOnStart)
            Lock();
    }

    void Update()
    {
        if (!escToggle)
            return;

        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
        {
            RuntimeUiInputUtility.ForceMenuCursor();
            return;
        }

        if (Time.timeScale <= 0f)
        {
            RuntimeUiInputUtility.ForceMenuCursor();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                Unlock();
            else
                Lock();
        }
    }

    public static void Lock()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public static void Unlock()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
