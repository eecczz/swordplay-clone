using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ArenaPlayModeSmokeTest
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string ReportPath = "Library/ArenaPlayModeSmokeTest.txt";
    private const string SessionKey = "Swordplay.ArenaSmokeTest";
    private static double startedAt;
    private static string failures = "";
    private static bool gameStarted;

    static ArenaPlayModeSmokeTest()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        // During a play-mode domain reload isPlaying can still be false for this
        // constructor. Keep the update callback registered and let Tick wait until
        // the editor has actually entered play mode.
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    public static void Run()
    {
        failures = "";
        gameStarted = false;
        startedAt = 0d;
        File.WriteAllText(ReportPath, "STARTED\n");
        SessionState.SetBool(SessionKey, true);
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived += OnLog;
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(SessionKey, false))
        {
            SessionState.SetBool(SessionKey, false);
            EditorApplication.Exit(string.IsNullOrEmpty(failures) ? 0 : 1);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        ArenaGameDirector director = UnityEngine.Object.FindFirstObjectByType<ArenaGameDirector>();
        if (!gameStarted && director != null)
        {
            director.StartGame();
            gameStarted = true;
            startedAt = EditorApplication.timeSinceStartup;
        }
        if (!gameStarted || EditorApplication.timeSinceStartup - startedAt < 7.5) return;

        EditorApplication.update -= Tick;
        int activeEnemies = UnityEngine.Object.FindObjectsByType<EnemyWorker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        PlayerWorker player = UnityEngine.Object.FindFirstObjectByType<PlayerWorker>();
        Camera gameplay = Camera.main;
        if (director == null) failures += "Director missing\n";
        if (activeEnemies != 10) failures += $"Expected 10 active enemies, found {activeEnemies}\n";
        if (player == null || !player.enabled) failures += "Player was not enabled after StartGame\n";
        if (gameplay == null || !gameplay.gameObject.activeInHierarchy) failures += "Gameplay camera inactive\n";
        File.WriteAllText(ReportPath,
            $"ActiveEnemies={activeEnemies}\nPlayerEnabled={player != null && player.enabled}\nGameplayCamera={gameplay?.name}\nFailures={failures}");
        EditorApplication.ExitPlaymode();
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            failures += condition + "\n" + stackTrace + "\n";
    }
}
