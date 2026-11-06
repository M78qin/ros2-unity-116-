// Welcome gate for the Doosan-DT unified panel.
//
// IMPORTANT: this is a WELCOME PAGE, not authentication. The previous scene had an empty
// MonoBehaviour whose script GUID was permanently lost (cc8bcb5c…, zero serialized fields),
// and the LoginPanel below it had two input fields but no logic and no onClick handlers.
// Per the user's decision for this batch the panel no longer collects credentials: it shows
// the project title plus a single "进入系统" (enter) action and a "退出" (quit) action.
//
// Wiring is persisted by the editor tool Assets/DoosanDT/10_DigitalTwin/Editor/TwinUIFixMissingScripts.cs
// (menu: DigitalTwin/UI/2 Fix Missing Scripts). Awake only binds the buttons when nothing is
// persisted yet, so entering Play twice can never double-fire the click handlers.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class BatteryLoginGate : MonoBehaviour
    {
        [Header("Panel roots")]
        [SerializeField] private GameObject loginPanel;
        [SerializeField] private GameObject shellRoot;

        [Header("Actions")]
        [SerializeField] private Button enterButton;
        [SerializeField] private Button quitButton;

        [Header("Copy")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text hintText;

        [Header("Inputs (welcome page collects nothing, so these are hidden)")]
        [SerializeField] private GameObject[] inputFields = new GameObject[0];

        [SerializeField] private bool hideInputsOnEnable = true;

        public bool HasEntered { get; private set; }

        private void Awake()
        {
            BindIfNeeded();
        }

        private void OnEnable()
        {
            if (!hideInputsOnEnable || inputFields == null)
            {
                return;
            }

            foreach (GameObject field in inputFields)
            {
                if (field != null && field.activeSelf)
                {
                    field.SetActive(false);
                }
            }
        }

        /// <summary>Bind the actions if the editor tool could not persist them.</summary>
        public void BindIfNeeded()
        {
            if (enterButton != null && enterButton.onClick.GetPersistentEventCount() == 0)
            {
                enterButton.onClick.RemoveListener(Enter);
                enterButton.onClick.AddListener(Enter);
            }

            if (quitButton != null && quitButton.onClick.GetPersistentEventCount() == 0)
            {
                quitButton.onClick.RemoveListener(Quit);
                quitButton.onClick.AddListener(Quit);
            }
        }

        /// <summary>Show the welcome page again (used by the shell's "back to welcome" if ever needed).</summary>
        public void Show()
        {
            HasEntered = false;
            if (loginPanel != null)
            {
                loginPanel.SetActive(true);
            }

            if (shellRoot != null)
            {
                shellRoot.SetActive(false);
            }
        }

        /// <summary>Enter the twin: hide the welcome page and reveal the unified shell.</summary>
        public void Enter()
        {
            HasEntered = true;

            if (loginPanel != null)
            {
                loginPanel.SetActive(false);
            }

            TwinUIShellController shell = shellRoot != null ? shellRoot.GetComponent<TwinUIShellController>() : null;
            if (shellRoot != null && !shellRoot.activeSelf)
            {
                shellRoot.SetActive(true);
            }

            if (shell != null)
            {
                shell.ShowDefaultTab();
            }

            Debug.Log("[UI Gate] entered the twin (welcome page, no credential check)");
        }

        /// <summary>Quit action. In the editor this only hides the panel; in a build it quits the player.</summary>
        public void Quit()
        {
            if (loginPanel != null)
            {
                loginPanel.SetActive(false);
            }

            if (hintText != null)
            {
                hintText.text = "已退出（编辑器内不关闭进程）";
            }

            Debug.Log("[UI Gate] quit action");
#if !UNITY_EDITOR
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------- editor helpers

        public void Configure(
            GameObject loginPanelRoot,
            GameObject shell,
            Button enter,
            Button quit,
            TMP_Text title,
            TMP_Text hint,
            GameObject[] inputs)
        {
            loginPanel = loginPanelRoot;
            shellRoot = shell;
            enterButton = enter;
            quitButton = quit;
            titleText = title;
            hintText = hint;
            inputFields = inputs ?? new GameObject[0];
        }

        public void SetShellRoot(GameObject shell)
        {
            shellRoot = shell;
        }
    }
}