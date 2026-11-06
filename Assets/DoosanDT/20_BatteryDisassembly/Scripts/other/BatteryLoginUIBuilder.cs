#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro; // 必须引用TMPro命名空间

public class LoginUIGenerator_TMPro : EditorWindow
{
    // ===================== 可直接修改的默认配置 =====================
    private static readonly Color PanelBgColor = new Color(0f, 0f, 0f, 0.88f);
    private static readonly Color InputFieldBgColor = new Color(0.204f, 0.471f, 0.839f, 1f);
    private static readonly Color ButtonBgColor = Color.white;
    private static readonly Color WhiteTextColor = Color.white;
    private static readonly Color BlackTextColor = Color.black;

    private const int LoginPanelWidth = 600;
    private const int LoginPanelHeight = 400;
    private const int InputFieldWidth = 450;
    private const int InputFieldHeight = 60;
    private const int ButtonWidth = 120;
    private const int ButtonHeight = 40;

    private const string LoginTitleText = "用户登录";
    private const string AccountPlaceholderText = "请输入账号";
    private const string PasswordPlaceholderText = "请输入密码";
    private const string CancelButtonText = "取消";
    private const string LoginButtonText = "登录";
    // ==================================================================

    [MenuItem("GameObject/UI/生成登录面板UI(TMPro版)", false, 11)]
    public static void GenerateLoginUI()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("LoginCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<StandaloneInputModule>();
        }

        GenerateLoginPanel(canvas.transform);
        Selection.activeGameObject = canvas.gameObject;
        EditorUtility.DisplayDialog("生成完成", "TMPro版登录UI已生成！\n可在Inspector中替换为你自己的TMPro字体。", "确定");
    }

    private static void GenerateLoginPanel(Transform parent)
    {
        GameObject loginPanelObj = new GameObject("LoginPanel");
        loginPanelObj.transform.SetParent(parent, false);
        RectTransform panelRt = loginPanelObj.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(LoginPanelWidth, LoginPanelHeight);
        panelRt.anchoredPosition = Vector2.zero;
        Image panelBg = loginPanelObj.AddComponent<Image>();
        panelBg.color = PanelBgColor;

        // 登录标题 (TMPro)
        GameObject loginTitleObj = new GameObject("LoginTitle");
        loginTitleObj.transform.SetParent(loginPanelObj.transform, false);
        RectTransform loginTitleRt = loginTitleObj.AddComponent<RectTransform>();
        loginTitleRt.anchorMin = new Vector2(0.5f, 1);
        loginTitleRt.anchorMax = new Vector2(0.5f, 1);
        loginTitleRt.pivot = new Vector2(0.5f, 1);
        loginTitleRt.sizeDelta = new Vector2(200, 50);
        loginTitleRt.anchoredPosition = new Vector2(0, -30);
        TextMeshProUGUI loginTitleText = loginTitleObj.AddComponent<TextMeshProUGUI>();
        loginTitleText.text = LoginTitleText;
        loginTitleText.fontSize = 28;
        loginTitleText.color = WhiteTextColor;
        loginTitleText.alignment = TextAlignmentOptions.Center;

        // 账号输入框 (TMPro Input Field)
        CreateTMPInputField("AccountInput", loginPanelObj.transform, AccountPlaceholderText, new Vector2(0, -100));
        // 密码输入框 (TMPro Input Field)
        GameObject passwordInput = CreateTMPInputField("PasswordInput", loginPanelObj.transform, PasswordPlaceholderText, new Vector2(0, -180));
        passwordInput.GetComponent<TMP_InputField>().contentType = TMP_InputField.ContentType.Password;

        // 按钮
        CreateTMPButton("CancelButton", loginPanelObj.transform, CancelButtonText, new Vector2(-85, -280));
        CreateTMPButton("LoginButton", loginPanelObj.transform, LoginButtonText, new Vector2(85, -280));
    }

    private static GameObject CreateTMPInputField(string name, Transform parent, string placeholderText, Vector2 anchoredPos)
    {
        GameObject inputObj = new GameObject(name);
        inputObj.transform.SetParent(parent, false);
        RectTransform inputRt = inputObj.AddComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0.5f, 0.5f);
        inputRt.anchorMax = new Vector2(0.5f, 0.5f);
        inputRt.pivot = new Vector2(0.5f, 0.5f);
        inputRt.sizeDelta = new Vector2(InputFieldWidth, InputFieldHeight);
        inputRt.anchoredPosition = anchoredPos;

        Image inputBg = inputObj.AddComponent<Image>();
        inputBg.color = InputFieldBgColor;
        TMP_InputField inputField = inputObj.AddComponent<TMP_InputField>();
        inputField.targetGraphic = inputBg;
        inputField.transition = Selectable.Transition.ColorTint;

        // 文字区域
        GameObject textAreaObj = new GameObject("Text Area");
        textAreaObj.transform.SetParent(inputObj.transform, false);
        RectTransform textAreaRt = textAreaObj.AddComponent<RectTransform>();
        textAreaRt.anchorMin = Vector2.zero;
        textAreaRt.anchorMax = Vector2.one;
        textAreaRt.offsetMin = new Vector2(10, 0);
        textAreaRt.offsetMax = new Vector2(-10, 0);

        // 输入文字 (TMPro)
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textAreaObj.transform, false);
        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = 14;
        text.color = WhiteTextColor;
        text.alignment = TextAlignmentOptions.Left;
        text.verticalAlignment = VerticalAlignmentOptions.Middle;

        // 占位符 (TMPro)
        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textAreaObj.transform, false);
        RectTransform placeholderRt = placeholderObj.AddComponent<RectTransform>();
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;
        TextMeshProUGUI placeholder = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholder.fontSize = 14;
        placeholder.color = new Color(1f, 1f, 1f, 0.6f);
        placeholder.text = placeholderText;
        placeholder.alignment = TextAlignmentOptions.Left;
        placeholder.verticalAlignment = VerticalAlignmentOptions.Middle;

        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        return inputObj;
    }

    private static GameObject CreateTMPButton(string name, Transform parent, string btnText, Vector2 anchoredPos)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        RectTransform btnRt = btnObj.AddComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0.5f);
        btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.pivot = new Vector2(0.5f, 0.5f);
        btnRt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        btnRt.anchoredPosition = anchoredPos;

        Image btnBg = btnObj.AddComponent<Image>();
        btnBg.color = ButtonBgColor;
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnBg;
        btn.transition = Selectable.Transition.ColorTint;

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = 14;
        text.color = BlackTextColor;
        text.alignment = TextAlignmentOptions.Center;
        text.text = btnText;

        return btnObj;
    }
}

#endif
