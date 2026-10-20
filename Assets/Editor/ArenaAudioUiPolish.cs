using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ArenaAudioUiPolish
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string FontPath = "Assets/UI/Medieval/Cinzel-VariableFont_wght.ttf";
    private const string GeneratedFolder = "Assets/UI/Medieval/Generated";
    private const string CheckmarkPath = GeneratedFolder + "/MedievalCheckmark.png";
    private const string DefeatedIconPath = GeneratedFolder + "/DefeatedEnemiesIcon.png";

    [MenuItem("Tools/Swordplay/Apply Arena Audio And UI Polish")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            throw new InvalidOperationException($"Open {ScenePath} before applying arena UI polish.");

        ArenaGameDirector director = UnityEngine.Object.FindFirstObjectByType<ArenaGameDirector>(FindObjectsInactive.Include);
        SoundManager soundManager = UnityEngine.Object.FindFirstObjectByType<SoundManager>(FindObjectsInactive.Include);
        if (director == null || soundManager == null)
            throw new InvalidOperationException("ArenaGameDirector or SoundManager is missing from the launch scene.");

        EnsureGeneratedSprites();
        Sprite checkmarkSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CheckmarkPath);
        Sprite defeatedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(DefeatedIconPath);
        Font medievalFont = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (checkmarkSprite == null || defeatedSprite == null || medievalFont == null)
            throw new InvalidOperationException("Arena UI font or generated sprites could not be loaded.");

        SerializedObject soundData = new(soundManager);
        SetString(soundData, "bgmResourceFolder", "Audio/BGM/RPG Medieval Times");
        SetString(soundData, "defaultBgmName", "RPG_Love,Calmness,Nature_02");
        soundData.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject directorData = new(director);
        SetString(directorData, "menuBgmName", "RPG_Love,Calmness,Nature_02");
        SetString(directorData, "gameplayBgmName", "RPG_Figths,Enemies,Danger_02");

        Text currency = directorData.FindProperty("currencyText")?.objectReferenceValue as Text;
        Toggle soundToggle = directorData.FindProperty("soundToggle")?.objectReferenceValue as Toggle;
        if (currency == null || soundToggle == null)
            throw new InvalidOperationException("Currency text or sound toggle is not assigned on ArenaGameDirector.");

        RectTransform currencyRect = currency.rectTransform;
        currencyRect.anchorMin = Vector2.one;
        currencyRect.anchorMax = Vector2.one;
        currencyRect.pivot = Vector2.one;
        currencyRect.anchoredPosition = new Vector2(-48f, -34f);
        currencyRect.sizeDelta = new Vector2(520f, 64f);
        currency.alignment = TextAnchor.UpperRight;

        Image checkmark = soundToggle.graphic as Image;
        if (checkmark == null)
            throw new InvalidOperationException("The sound toggle checkmark Image is missing.");
        checkmark.sprite = checkmarkSprite;
        checkmark.type = Image.Type.Simple;
        checkmark.preserveAspect = true;
        checkmark.color = Color.white;
        RectTransform checkmarkRect = checkmark.rectTransform;
        checkmarkRect.anchorMin = Vector2.zero;
        checkmarkRect.anchorMax = Vector2.one;
        checkmarkRect.offsetMin = new Vector2(5f, 5f);
        checkmarkRect.offsetMax = new Vector2(-5f, -5f);

        Canvas canvas = currency.GetComponentInParent<Canvas>();
        if (canvas == null)
            throw new InvalidOperationException("Arena UI canvas was not found.");
        GameObject defeatedHud = GetOrCreateDefeatedHud(canvas.transform, medievalFont, defeatedSprite, out Text defeatedText);
        defeatedHud.SetActive(false);

        directorData.FindProperty("defeatedEnemiesHud").objectReferenceValue = defeatedHud;
        directorData.FindProperty("defeatedEnemiesText").objectReferenceValue = defeatedText;
        directorData.ApplyModifiedPropertiesWithoutUndo();

        foreach (Text text in canvas.GetComponentsInChildren<Text>(true))
        {
            text.font = medievalFont;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            Outline outline = text.GetComponent<Outline>();
            if (outline == null)
                outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.025f, 0.012f, 0.008f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;
            EditorUtility.SetDirty(text);
            EditorUtility.SetDirty(outline);
        }

        EditorUtility.SetDirty(currencyRect);
        EditorUtility.SetDirty(checkmark);
        EditorUtility.SetDirty(checkmarkRect);
        EditorUtility.SetDirty(director);
        EditorUtility.SetDirty(soundManager);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Arena Audio/UI] BGM selection, white medieval text, checkmark, currency placement, and defeated-enemy HUD applied.");
    }

    private static GameObject GetOrCreateDefeatedHud(Transform parent, Font font, Sprite iconSprite, out Text countText)
    {
        Transform existing = parent.Find("Defeated Enemies HUD");
        GameObject root = existing != null ? existing.gameObject : new GameObject("Defeated Enemies HUD", typeof(RectTransform));
        if (existing == null)
            root.transform.SetParent(parent, false);

        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.one;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = Vector2.one;
        rootRect.anchoredPosition = new Vector2(-48f, -30f);
        rootRect.sizeDelta = new Vector2(190f, 72f);

        Transform iconTransform = root.transform.Find("Defeated Icon");
        GameObject iconObject = iconTransform != null
            ? iconTransform.gameObject
            : new GameObject("Defeated Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (iconTransform == null)
            iconObject.transform.SetParent(root.transform, false);
        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(60f, 60f);
        Image icon = iconObject.GetComponent<Image>();
        icon.sprite = iconSprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.color = Color.white;

        Transform textTransform = root.transform.Find("Defeated Count");
        GameObject textObject = textTransform != null
            ? textTransform.gameObject
            : new GameObject("Defeated Count", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        if (textTransform == null)
            textObject.transform.SetParent(root.transform, false);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(68f, 0f);
        textRect.offsetMax = Vector2.zero;
        countText = textObject.GetComponent<Text>();
        countText.font = font;
        countText.fontSize = 42;
        countText.fontStyle = FontStyle.Bold;
        countText.alignment = TextAnchor.MiddleLeft;
        countText.color = Color.white;
        countText.text = "0";
        countText.raycastTarget = false;

        EditorUtility.SetDirty(rootRect);
        EditorUtility.SetDirty(icon);
        EditorUtility.SetDirty(countText);
        return root;
    }

    private static void SetString(SerializedObject data, string propertyName, string value)
    {
        SerializedProperty property = data.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"Serialized property '{propertyName}' was not found.");
        property.stringValue = value;
    }

    private static void EnsureGeneratedSprites()
    {
        EnsureFolder("Assets/UI");
        EnsureFolder("Assets/UI/Medieval");
        EnsureFolder(GeneratedFolder);
        WriteIcon(CheckmarkPath, DrawCheckmark);
        WriteIcon(DefeatedIconPath, DrawCrossedSwords);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureSprite(CheckmarkPath);
        ConfigureSprite(DefeatedIconPath);
    }

    private static void WriteIcon(string assetPath, Action<Texture2D> draw)
    {
        const int size = 128;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false, false);
        Color[] clear = Enumerable.Repeat(Color.clear, size * size).ToArray();
        texture.SetPixels(clear);
        draw(texture);
        texture.Apply(false, false);
        File.WriteAllBytes(Path.GetFullPath(assetPath), texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
    }

    private static void DrawCheckmark(Texture2D texture)
    {
        Color edge = new(0.12f, 0.055f, 0.018f, 1f);
        Color gold = new(1f, 0.88f, 0.55f, 1f);
        DrawSegment(texture, new Vector2(24, 64), new Vector2(52, 35), 15f, edge);
        DrawSegment(texture, new Vector2(49, 37), new Vector2(105, 99), 15f, edge);
        DrawSegment(texture, new Vector2(24, 64), new Vector2(52, 35), 9f, gold);
        DrawSegment(texture, new Vector2(49, 37), new Vector2(105, 99), 9f, gold);
    }

    private static void DrawCrossedSwords(Texture2D texture)
    {
        Color edge = new(0.08f, 0.025f, 0.015f, 1f);
        Color metal = new(0.96f, 0.96f, 0.92f, 1f);
        Color gold = new(1f, 0.76f, 0.24f, 1f);
        Vector2 a0 = new(28, 102); Vector2 a1 = new(101, 27);
        Vector2 b0 = new(27, 27); Vector2 b1 = new(101, 102);
        DrawSegment(texture, a0, a1, 10f, edge);
        DrawSegment(texture, b0, b1, 10f, edge);
        DrawSegment(texture, a0, a1, 5.5f, metal);
        DrawSegment(texture, b0, b1, 5.5f, metal);
        DrawSegment(texture, new Vector2(22, 91), new Vector2(39, 108), 8f, edge);
        DrawSegment(texture, new Vector2(89, 108), new Vector2(106, 91), 8f, edge);
        DrawSegment(texture, new Vector2(23, 92), new Vector2(38, 107), 4f, gold);
        DrawSegment(texture, new Vector2(90, 107), new Vector2(105, 92), 4f, gold);
    }

    private static void DrawSegment(Texture2D texture, Vector2 from, Vector2 to, float radius, Color color)
    {
        Vector2 delta = to - from;
        float lengthSquared = delta.sqrMagnitude;
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(from.x, to.x) - radius - 1f));
        int maxX = Mathf.Min(texture.width - 1, Mathf.CeilToInt(Mathf.Max(from.x, to.x) + radius + 1f));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(from.y, to.y) - radius - 1f));
        int maxY = Mathf.Min(texture.height - 1, Mathf.CeilToInt(Mathf.Max(from.y, to.y) + radius + 1f));
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            Vector2 point = new(x + 0.5f, y + 0.5f);
            float t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - from, delta) / lengthSquared) : 0f;
            float distance = Vector2.Distance(point, from + delta * t);
            if (distance > radius + 1f) continue;
            float alpha = Mathf.Clamp01(radius + 1f - distance);
            Color destination = texture.GetPixel(x, y);
            Color source = new(color.r, color.g, color.b, color.a * alpha);
            texture.SetPixel(x, y, Color.Lerp(destination, source, source.a));
        }
    }

    private static void ConfigureSprite(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        string name = path.Substring(path.LastIndexOf('/') + 1);
        AssetDatabase.CreateFolder(parent, name);
    }
}
