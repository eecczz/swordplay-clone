using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Swordplay.Visuals;
using UnityEngine;
using UnityEngine.UI;

public sealed class ArenaGameDirector : MonoBehaviour
{
    public static ArenaGameDirector Instance { get; private set; }

    [Header("Arena pool")]
    [SerializeField] private PlayerWorker player;
    [SerializeField] private Transform entranceSpawn;
    [SerializeField] private List<EnemyWorker> enemyPool = new();
    [SerializeField, Range(1, 10)] private int initialActiveEnemies = 10;
    [SerializeField, Min(0.05f)] private float entranceInterval = 0.32f;
    [SerializeField, Min(0.2f)] private float recycleDelay = 1.25f;

    [Header("Presentation")]
    [SerializeField] private Camera menuCamera;
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private List<Camera> gameplayCameras = new();
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private Text startBanner;
    [SerializeField] private Text currencyText;
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle soundToggle;
    [SerializeField] private Text shopStatusText;
    [SerializeField, Min(1)] private int weaponAuraPrice = 50;
    [SerializeField, Min(1)] private int characterPrice = 100;

    private readonly Queue<EnemyWorker> available = new();
    private int gold;
    private int diamonds;
    private bool gameRunning;

    private void Awake()
    {
        Instance = this;
        gold = PlayerPrefs.GetInt("ArenaGold", 0);
        diamonds = PlayerPrefs.GetInt("ArenaDiamonds", 0);
        foreach (EnemyWorker enemy in enemyPool.Where(value => value != null).Distinct())
        {
            enemy.gameObject.SetActive(false);
            available.Enqueue(enemy);
        }
        ShowMainMenu();
        RefreshCurrency();
        if (startBanner != null) startBanner.gameObject.SetActive(false);
        if (volumeSlider != null) volumeSlider.value = PlayerPrefs.GetFloat("ArenaVolume", 1f);
        if (soundToggle != null) soundToggle.isOn = PlayerPrefs.GetInt("ArenaSound", 1) != 0;
        ApplyAudioSettings();
    }

    public void StartGame()
    {
        if (gameRunning) return;
        gameRunning = true;
        mainPanel?.SetActive(false);
        settingsPanel?.SetActive(false);
        shopPanel?.SetActive(false);
        if (menuCamera != null)
        {
            menuCamera.tag = "Untagged";
            menuCamera.gameObject.SetActive(false);
        }
        SetGameplayCameras(true);
        if (player != null)
        {
            player.enabled = true;
            player.SetArenaGameplayEnabled(true);
        }
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        Time.timeScale = 1f;
        StartCoroutine(BeginArena());
    }

    private IEnumerator BeginArena()
    {
        int count = Mathf.Min(initialActiveEnemies, enemyPool.Count);
        for (int i = 0; i < count; i++)
        {
            SpawnNext();
            yield return new WaitForSeconds(entranceInterval);
        }
        yield return new WaitForSeconds(1.1f);
        if (startBanner != null)
        {
            startBanner.gameObject.SetActive(true);
            startBanner.text = "START!";
            yield return new WaitForSeconds(1.1f);
            startBanner.gameObject.SetActive(false);
        }
    }

    private void SpawnNext()
    {
        if (available.Count == 0 || entranceSpawn == null) return;
        EnemyWorker enemy = available.Dequeue();
        enemy.gameObject.SetActive(true);
        Vector3 jitter = entranceSpawn.right * ((enemyPool.IndexOf(enemy) % 3) - 1) * 0.75f;
        enemy.ResetForPool(entranceSpawn.position + jitter, entranceSpawn.rotation);
    }

    public void NotifyEnemyDefeated(EnemyWorker enemy)
    {
        if (enemy == null) return;
        gold += 10;
        if (gold > 0 && gold % 100 == 0) diamonds++;
        PlayerPrefs.SetInt("ArenaGold", gold);
        PlayerPrefs.SetInt("ArenaDiamonds", diamonds);
        RefreshCurrency();
        StartCoroutine(Recycle(enemy));
    }

    private IEnumerator Recycle(EnemyWorker enemy)
    {
        enemy.gameObject.SetActive(false);
        yield return new WaitForSeconds(recycleDelay);
        available.Enqueue(enemy);
        SpawnNext();
    }

    public void ShowMainMenu()
    {
        gameRunning = false;
        mainPanel?.SetActive(true);
        settingsPanel?.SetActive(false);
        shopPanel?.SetActive(false);
        SetGameplayCameras(false);
        if (menuCamera != null)
        {
            menuCamera.gameObject.SetActive(true);
            menuCamera.enabled = true;
            menuCamera.tag = "MainCamera";
        }
        if (player != null)
        {
            player.enabled = true;
            player.SetArenaGameplayEnabled(false);
        }
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        Time.timeScale = 0f;
    }

    public void ShowSettings() { mainPanel?.SetActive(false); settingsPanel?.SetActive(true); shopPanel?.SetActive(false); }
    public void ShowShop() { mainPanel?.SetActive(false); settingsPanel?.SetActive(false); shopPanel?.SetActive(true); }
    public void BackToMain() { ApplyAudioSettings(); ShowMainMenu(); }
    public void ApplyAudioSettings()
    {
        bool enabled = soundToggle == null || soundToggle.isOn;
        float volume = volumeSlider != null ? volumeSlider.value : 1f;
        AudioListener.volume = enabled ? volume : 0f;
        PlayerPrefs.SetInt("ArenaSound", enabled ? 1 : 0);
        PlayerPrefs.SetFloat("ArenaVolume", volume);
        if (player != null && sensitivitySlider != null)
            player.SetSensitivity(sensitivitySlider.value);
    }

    private void RefreshCurrency()
    {
        if (currencyText != null) currencyText.text = $"● {gold:N0}     ◆ {diamonds:N0}";
    }

    public void PurchaseWeaponAuraSkin()
    {
        if (!TrySpendGold(weaponAuraPrice, "NOT ENOUGH GOLD")) return;
        int owned = PlayerPrefs.GetInt("ArenaWeaponAuraSkins", 0) + 1;
        PlayerPrefs.SetInt("ArenaWeaponAuraSkins", owned);
        Color[] palette =
        {
            new(.18f, .82f, 1f), new(1f, .32f, .55f), new(1f, .72f, .18f),
            new(.35f, 1f, .52f), new(.72f, .4f, 1f)
        };
        Color color = palette[(owned - 1) % palette.Length];
        KnightRocketLegVfx rocket = player != null ? player.GetComponent<KnightRocketLegVfx>() : null;
        if (rocket != null) rocket.SetEffectColor(color);
        if (player != null)
            foreach (BrokenEdgeBladeVfx blade in player.GetComponentsInChildren<BrokenEdgeBladeVfx>(true))
                blade.SetEffectColors(color * 2.2f, Color.Lerp(color, Color.white, .7f));
        SetShopStatus($"AURA SKIN {owned} EQUIPPED");
    }

    public void PurchaseCharacter()
    {
        if (!TrySpendGold(characterPrice, "NOT ENOUGH GOLD")) return;
        int owned = PlayerPrefs.GetInt("ArenaOwnedCharacters", 1) + 1;
        PlayerPrefs.SetInt("ArenaOwnedCharacters", owned);
        SetShopStatus($"CHARACTER {owned} UNLOCKED");
    }

    private bool TrySpendGold(int price, string failureMessage)
    {
        if (gold < price)
        {
            SetShopStatus(failureMessage);
            return false;
        }
        gold -= price;
        PlayerPrefs.SetInt("ArenaGold", gold);
        RefreshCurrency();
        return true;
    }

    private void SetShopStatus(string value)
    {
        if (shopStatusText != null) shopStatusText.text = value;
    }

    private void SetGameplayCameras(bool active)
    {
        IEnumerable<Camera> cameras = gameplayCameras
            .Where(value => value != null)
            .Append(gameplayCamera)
            .Where(value => value != null)
            .Distinct();
        Camera primary = gameplayCamera != null ? gameplayCamera : cameras.FirstOrDefault();
        foreach (Camera camera in cameras)
        {
            bool ownsPlayerRoot = player != null && camera.gameObject == player.gameObject;
            if (!active && !ownsPlayerRoot)
                camera.gameObject.SetActive(false);
            else if (active)
                camera.gameObject.SetActive(true);
            camera.enabled = active;
            camera.tag = active && camera == primary ? "MainCamera" : "Untagged";
            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active && camera == primary;
        }
    }
}
