using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed class ArenaGameDirector : MonoBehaviour
{
    public static ArenaGameDirector Instance { get; private set; }

    [Header("Arena pool")]
    [SerializeField] private PlayerWorker player;
    [SerializeField] private Transform entranceSpawn;
    [SerializeField] private List<EnemyWorker> enemyPool = new();
    [SerializeField, Range(1, 10)] private int initialActiveEnemies = 8;
    [SerializeField, Min(0.05f)] private float entranceInterval = 0.32f;
    [SerializeField, Min(0.2f)] private float recycleDelay = 1.25f;

    [Header("Presentation")]
    [SerializeField] private Camera menuCamera;
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private Text startBanner;
    [SerializeField] private Text currencyText;
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle soundToggle;

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
        if (menuCamera != null) menuCamera.gameObject.SetActive(false);
        if (gameplayCamera != null) gameplayCamera.gameObject.SetActive(true);
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
        if (menuCamera != null) menuCamera.gameObject.SetActive(true);
        if (gameplayCamera != null) gameplayCamera.gameObject.SetActive(false);
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
}
