using UnityEngine;

namespace Swordplay.UI
{
    [DefaultExecutionOrder(-1600), DisallowMultipleComponent]
    public sealed class MedievalCursorController : MonoBehaviour
    {
        public static MedievalCursorController Instance { get; private set; }

        [SerializeField] private Texture2D cursorTexture;
        [SerializeField] private Vector2 hotspot = new(10f, 7f);

        private bool visible = true;
        private bool lockPointerWhenHidden = true;

        private void Awake()
        {
            Instance = this;
            Apply(true);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) Apply(visible, lockPointerWhenHidden);
        }

        public static void ShowMenuCursor()
        {
            if (Instance != null)
                Instance.Apply(true);
            else
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        public static void HideGameplayCursor(bool lockPointer = true)
        {
            if (Instance != null)
                Instance.Apply(false, lockPointer);
            else
            {
                Cursor.visible = false;
                Cursor.lockState = lockPointer ? CursorLockMode.Locked : CursorLockMode.None;
            }
        }

        private void Apply(bool show, bool lockPointer = true)
        {
            visible = show;
            lockPointerWhenHidden = lockPointer;
            if (cursorTexture != null)
                Cursor.SetCursor(cursorTexture, hotspot, CursorMode.Auto);
            Cursor.lockState = show || !lockPointer ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = show;
        }
    }
}
