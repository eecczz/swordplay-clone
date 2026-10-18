using System;
using System.Collections.Generic;
using System.Linq;
using Swordplay.Visuals;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class ArenaContentInstaller
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private static readonly Color[] Palette = {
        new(1f,.28f,.42f), new(1f,.58f,.12f), new(1f,.86f,.14f), new(.25f,1f,.45f),
        new(.1f,.72f,1f), new(.48f,.32f,1f), new(1f,.25f,.82f), new(.1f,1f,.86f),
        new(.95f,.95f,.95f), new(.72f,1f,.18f), new(.9f,.35f,.12f)
    };

    [MenuItem("Tools/Swordplay/Install Arena Game Content")]
    public static void Install()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PlayerWorker player = UnityEngine.Object.FindObjectsByType<PlayerWorker>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .First(value => value.gameObject.activeInHierarchy);
        EnemyWorker[] existingEnemies = UnityEngine.Object.FindObjectsByType<EnemyWorker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        EnemyWorker source = existingEnemies.FirstOrDefault(value => value.name == "Arena Enemy 01")
            ?? existingEnemies.First(value => value.gameObject.activeInHierarchy);
        foreach (EnemyWorker generated in existingEnemies)
            if (generated != source && generated.name.StartsWith("Arena Enemy ", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(generated.gameObject);

        GameObject old = GameObject.Find("Arena Game Flow");
        if (old != null) UnityEngine.Object.DestroyImmediate(old);
        GameObject root = new("Arena Game Flow");
        ArenaGameDirector director = root.AddComponent<ArenaGameDirector>();
        Transform entrance = new GameObject("Enemy Entrance Spawner").transform;
        entrance.SetParent(root.transform);
        entrance.SetPositionAndRotation(player.transform.position + player.transform.forward * 15f,
            Quaternion.LookRotation(-player.transform.forward, Vector3.up));
        if (NavMesh.SamplePosition(entrance.position, out NavMeshHit entranceHit, 12f, NavMesh.AllAreas))
            entrance.position = entranceHit.position;

        var pool = new List<EnemyWorker> { source };
        source.name = "Arena Enemy 01";
        for (int i = 1; i < 10; i++)
        {
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject);
            clone.name = $"Arena Enemy {i + 1:00}";
            pool.Add(clone.GetComponent<EnemyWorker>());
        }
        Color playerEffect = player.GetComponent<KnightRocketLegVfx>()?.EffectColor ?? Color.cyan;
        Color[] enemyEffects = Palette.OrderByDescending(color => ColorDistance(color, playerEffect)).Take(10).ToArray();
        for (int i = 0; i < pool.Count; i++)
        {
            ApplyVariant(pool[i], i, enemyEffects[i]);
            pool[i].transform.SetPositionAndRotation(entrance.position, entrance.rotation);
            pool[i].gameObject.SetActive(false);
        }

        Camera[] sceneCameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Camera[] playerCameras = sceneCameras
            .Where(value => value.transform.IsChildOf(player.transform) || value.gameObject == player.gameObject)
            .ToArray();
        Camera[] activeGameplayCameras = playerCameras
            .Where(value => value.gameObject.activeSelf)
            .ToArray();
        Camera gameplay = playerCameras
            .FirstOrDefault(value => value.name.IndexOf("PlayerCamera", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? playerCameras.FirstOrDefault(value => value.gameObject.activeInHierarchy)
            ?? playerCameras.FirstOrDefault()
            ?? sceneCameras.First(value => value.gameObject.activeInHierarchy);
        foreach (Camera other in sceneCameras)
        {
            if (playerCameras.Contains(other)) continue;
            other.enabled = false;
            if (other.CompareTag("MainCamera")) other.tag = "Untagged";
            AudioListener listener = other.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
            EditorUtility.SetDirty(other);
        }
        foreach (Camera playerCamera in playerCameras)
        {
            if (playerCamera == gameplay) playerCamera.tag = "MainCamera";
        }
        AudioListener gameplayListener = gameplay.GetComponent<AudioListener>();
        if (gameplayListener != null) gameplayListener.enabled = true;
        Camera menu = UnityEngine.Object.Instantiate(gameplay, root.transform);
        menu.name = "Arena Vista Menu Camera";
        menu.tag = "MainCamera";
        menu.transform.position = player.transform.position - player.transform.forward * 18f + Vector3.up * 10f;
        menu.transform.LookAt(player.transform.position + Vector3.up * 2.5f);

        Canvas canvas = CreateCanvas(root.transform);
        if (!UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any())
            new GameObject("Arena Event System", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(root.transform);
        Text currency = MakeText(canvas.transform, "Currency", "● 0     ◆ 0", 28, TextAnchor.UpperRight,
            new Vector2(1,1), new Vector2(1,1), new Vector2(-38,-30), new Vector2(420,55));
        GameObject main = MakePanel(canvas.transform, "Main Menu", new Color(.02f,.035f,.07f,.24f));
        Text title = MakeText(main.transform, "Title", "SWORDPLAY", 58, TextAnchor.MiddleCenter,
            new Vector2(.5f,.72f), new Vector2(.5f,.72f), Vector2.zero, new Vector2(620,90));
        title.color = new Color(1f,.88f,.55f);
        Button start = MakeButton(main.transform, "GAME START", new Vector2(0,-10));
        Button settings = MakeButton(main.transform, "SETTINGS", new Vector2(0,-85));
        Button shop = MakeButton(main.transform, "SHOP", new Vector2(0,-160));

        GameObject settingsPanel = MakePanel(canvas.transform, "Settings", new Color(0,0,0,0));
        MakeText(settingsPanel.transform, "Settings Title", "SETTINGS", 46, TextAnchor.MiddleCenter,
            new Vector2(.5f,.76f), new Vector2(.5f,.76f), Vector2.zero, new Vector2(500,70));
        Slider sensitivity = MakeSlider(settingsPanel.transform, "Sensitivity", "SENSITIVITY", new Vector2(0,70), .5f, 12f, 5f);
        Toggle sound = MakeToggle(settingsPanel.transform, "Sound", "SOUND ON / OFF", new Vector2(0,0));
        Slider volume = MakeSlider(settingsPanel.transform, "Volume", "VOLUME", new Vector2(0,-70), 0f, 1f, 1f);
        Button settingsBack = MakeButton(settingsPanel.transform, "BACK", new Vector2(0,-175));

        GameObject shopPanel = MakePanel(canvas.transform, "Shop", new Color(.015f,.025f,.06f,.38f));
        MakeText(shopPanel.transform, "Shop Title", "SHOP", 46, TextAnchor.MiddleCenter,
            new Vector2(.5f,.84f), new Vector2(.5f,.84f), Vector2.zero, new Vector2(500,70));
        Button weaponAuraBuy = MakeShopCard(shopPanel.transform, "WEAPON AURA SKINS", "BUY  ● 50", new Vector2(-245,10), new Color(.15f,.8f,1f,.72f));
        Button characterBuy = MakeShopCard(shopPanel.transform, "CHARACTERS", "BUY  ● 100", new Vector2(245,10), new Color(1f,.42f,.62f,.72f));
        Text shopStatus = MakeText(shopPanel.transform, "Shop Status", "DEFEAT ENEMIES TO EARN GOLD", 20, TextAnchor.MiddleCenter,
            new Vector2(.5f,.22f), new Vector2(.5f,.22f), Vector2.zero, new Vector2(700,42));
        Button shopBack = MakeButton(shopPanel.transform, "BACK", new Vector2(0,-230));
        Text banner = MakeText(canvas.transform, "Start Banner", "START!", 82, TextAnchor.MiddleCenter,
            new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(700,120));
        banner.color = new Color(1f,.83f,.2f);

        SerializedObject data = new(director);
        data.FindProperty("player").objectReferenceValue = player;
        data.FindProperty("entranceSpawn").objectReferenceValue = entrance;
        SerializedProperty list = data.FindProperty("enemyPool"); list.arraySize = pool.Count;
        for (int i=0;i<pool.Count;i++) list.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];
        data.FindProperty("menuCamera").objectReferenceValue = menu;
        data.FindProperty("gameplayCamera").objectReferenceValue = gameplay;
        SerializedProperty cameras = data.FindProperty("gameplayCameras");
        cameras.arraySize = activeGameplayCameras.Length;
        for (int i = 0; i < activeGameplayCameras.Length; i++)
            cameras.GetArrayElementAtIndex(i).objectReferenceValue = activeGameplayCameras[i];
        data.FindProperty("mainPanel").objectReferenceValue = main;
        data.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
        data.FindProperty("shopPanel").objectReferenceValue = shopPanel;
        data.FindProperty("startBanner").objectReferenceValue = banner;
        data.FindProperty("currencyText").objectReferenceValue = currency;
        data.FindProperty("sensitivitySlider").objectReferenceValue = sensitivity;
        data.FindProperty("volumeSlider").objectReferenceValue = volume;
        data.FindProperty("soundToggle").objectReferenceValue = sound;
        data.FindProperty("shopStatusText").objectReferenceValue = shopStatus;
        data.ApplyModifiedPropertiesWithoutUndo();
        UnityEventTools.AddPersistentListener(start.onClick, director.StartGame);
        UnityEventTools.AddPersistentListener(settings.onClick, director.ShowSettings);
        UnityEventTools.AddPersistentListener(shop.onClick, director.ShowShop);
        UnityEventTools.AddPersistentListener(settingsBack.onClick, director.BackToMain);
        UnityEventTools.AddPersistentListener(shopBack.onClick, director.BackToMain);
        UnityEventTools.AddPersistentListener(weaponAuraBuy.onClick, director.PurchaseWeaponAuraSkin);
        UnityEventTools.AddPersistentListener(characterBuy.onClick, director.PurchaseCharacter);
        settingsPanel.SetActive(false); shopPanel.SetActive(false); banner.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Swordplay/Repair Launch Camera Flow")]
    public static void RepairLaunchCameraFlow()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PlayerWorker player = UnityEngine.Object.FindObjectsByType<PlayerWorker>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .First(value => value.gameObject.activeInHierarchy);
        ArenaGameDirector director = UnityEngine.Object.FindFirstObjectByType<ArenaGameDirector>(FindObjectsInactive.Include);
        Camera menu = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .First(value => value.name == "Arena Vista Menu Camera");
        Camera[] playerCameras = player.GetComponentsInChildren<Camera>(true);
        Camera[] activeGameplayCameras = playerCameras
            .Where(value => value.name.IndexOf("WeaponCamera", StringComparison.OrdinalIgnoreCase) < 0)
            .ToArray();
        Camera primary = playerCameras.FirstOrDefault(value =>
                             value.name.IndexOf("PlayerCamera", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? playerCameras.FirstOrDefault(value => value.CompareTag("MainCamera"))
            ?? playerCameras.FirstOrDefault(value => value.enabled)
            ?? playerCameras.First();

        foreach (Camera camera in playerCameras)
        {
            camera.tag = camera == primary ? "MainCamera" : "Untagged";
            camera.gameObject.SetActive(activeGameplayCameras.Contains(camera));
            EditorUtility.SetDirty(camera);
        }
        menu.gameObject.SetActive(true);
        menu.enabled = true;
        menu.tag = "MainCamera";
        AudioListener menuListener = menu.GetComponent<AudioListener>();
        if (menuListener != null) menuListener.enabled = true;

        SerializedObject data = new(director);
        data.FindProperty("gameplayCamera").objectReferenceValue = primary;
        SerializedProperty cameras = data.FindProperty("gameplayCameras");
        cameras.arraySize = activeGameplayCameras.Length;
        for (int i = 0; i < activeGameplayCameras.Length; i++)
            cameras.GetArrayElementAtIndex(i).objectReferenceValue = activeGameplayCameras[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static void ApplyVariant(EnemyWorker enemy, int index, Color effect)
    {
        var rocket = enemy.GetComponent<KnightRocketLegVfx>();
        if (rocket != null)
        {
            SerializedObject rocketData = new(rocket);
            rocketData.FindProperty("effectColor").colorValue = effect;
            rocketData.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (BrokenEdgeBladeVfx blade in enemy.GetComponentsInChildren<BrokenEdgeBladeVfx>(true))
        {
            SerializedObject so = new(blade);
            so.FindProperty("glowColor").colorValue = effect * 2.2f;
            so.FindProperty("coreColor").colorValue = Color.Lerp(effect, Color.white, .7f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        string[] groups = { "helmet", "head", "shoulder", "chest", "body", "arm", "hand" };
        foreach (string key in groups)
        {
            var choices = enemy.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (choices.Length < 2) continue;
            foreach (var choice in choices) choice.gameObject.SetActive(false);
            choices[index % choices.Length].gameObject.SetActive(true);
        }
    }

    private static float ColorDistance(Color a, Color b) =>
        (new Vector3(a.r, a.g, a.b) - new Vector3(b.r, b.g, b.b)).sqrMagnitude;

    private static Canvas CreateCanvas(Transform parent) { var go=new GameObject("Arena UI",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(parent);var c=go.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;var s=go.GetComponent<CanvasScaler>();s.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;s.referenceResolution=new Vector2(1920,1080);return c; }
    private static GameObject MakePanel(Transform p,string n,Color c){var g=new GameObject(n,typeof(RectTransform),typeof(Image));g.transform.SetParent(p,false);var r=(RectTransform)g.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;g.GetComponent<Image>().color=c;return g;}
    private static Font Font()=>Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    private static Text MakeText(Transform p,string n,string value,int size,TextAnchor align,Vector2 amin,Vector2 amax,Vector2 pos,Vector2 wh){var g=new GameObject(n,typeof(RectTransform),typeof(Text));g.transform.SetParent(p,false);var r=(RectTransform)g.transform;r.anchorMin=amin;r.anchorMax=amax;r.sizeDelta=wh;r.anchoredPosition=pos;var t=g.GetComponent<Text>();t.font=Font();t.text=value;t.fontSize=size;t.alignment=align;t.color=Color.white;t.fontStyle=FontStyle.Bold;return t;}
    private static Button MakeButton(Transform p,string label,Vector2 pos){var g=new GameObject(label,typeof(RectTransform),typeof(Image),typeof(Button));g.transform.SetParent(p,false);var r=(RectTransform)g.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(360,58);r.anchoredPosition=pos;g.GetComponent<Image>().color=new Color(.08f,.13f,.2f,.9f);var t=MakeText(g.transform,"Label",label,25,TextAnchor.MiddleCenter,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);t.rectTransform.offsetMin=t.rectTransform.offsetMax=Vector2.zero;return g.GetComponent<Button>();}
    private static Slider MakeSlider(Transform p,string n,string label,Vector2 pos,float min,float max,float value){MakeText(p,n+" Label",label,22,TextAnchor.MiddleRight,new Vector2(.5f,.5f),new Vector2(.5f,.5f),pos+new Vector2(-230,0),new Vector2(180,42));var g=new GameObject(n,typeof(RectTransform),typeof(Slider));g.transform.SetParent(p,false);var r=(RectTransform)g.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(360,36);r.anchoredPosition=pos+new Vector2(80,0);var bg=new GameObject("Background",typeof(RectTransform),typeof(Image));bg.transform.SetParent(g.transform,false);((RectTransform)bg.transform).anchorMin=Vector2.zero;((RectTransform)bg.transform).anchorMax=Vector2.one;bg.GetComponent<Image>().color=new Color(.1f,.15f,.22f,.9f);var fill=new GameObject("Fill",typeof(RectTransform),typeof(Image));fill.transform.SetParent(bg.transform,false);fill.GetComponent<Image>().color=new Color(.2f,.8f,1f,.9f);var s=g.GetComponent<Slider>();s.fillRect=(RectTransform)fill.transform;s.minValue=min;s.maxValue=max;s.value=value;return s;}
    private static Toggle MakeToggle(Transform p,string n,string label,Vector2 pos){var g=new GameObject(n,typeof(RectTransform),typeof(Image),typeof(Toggle));g.transform.SetParent(p,false);var r=(RectTransform)g.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(38,38);r.anchoredPosition=pos+new Vector2(80,0);g.GetComponent<Image>().color=new Color(.1f,.15f,.22f,.9f);var mark=new GameObject("Checkmark",typeof(RectTransform),typeof(Image));mark.transform.SetParent(g.transform,false);((RectTransform)mark.transform).anchorMin=Vector2.zero;((RectTransform)mark.transform).anchorMax=Vector2.one;mark.GetComponent<Image>().color=new Color(.2f,1f,.6f);var t=g.GetComponent<Toggle>();t.graphic=mark.GetComponent<Image>();t.isOn=true;MakeText(p,n+" Label",label,22,TextAnchor.MiddleRight,new Vector2(.5f,.5f),new Vector2(.5f,.5f),pos+new Vector2(-120,0),new Vector2(300,42));return t;}
    private static Button MakeShopCard(Transform p,string label,string purchaseLabel,Vector2 pos,Color c){var g=new GameObject(label,typeof(RectTransform),typeof(Image));g.transform.SetParent(p,false);var r=(RectTransform)g.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(410,360);r.anchoredPosition=pos;g.GetComponent<Image>().color=new Color(.04f,.07f,.12f,.92f);var preview=new GameObject("Preview",typeof(RectTransform),typeof(Image));preview.transform.SetParent(g.transform,false);var pr=(RectTransform)preview.transform;pr.anchorMin=new Vector2(.08f,.34f);pr.anchorMax=new Vector2(.92f,.9f);pr.offsetMin=pr.offsetMax=Vector2.zero;preview.GetComponent<Image>().color=c;MakeText(g.transform,"Name",label,25,TextAnchor.MiddleCenter,new Vector2(.5f,.23f),new Vector2(.5f,.23f),Vector2.zero,new Vector2(370,52));var buy=MakeButton(g.transform,purchaseLabel,new Vector2(0,-135));((RectTransform)buy.transform).sizeDelta=new Vector2(280,50);return buy;}
}
