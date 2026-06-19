using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;
using System.Collections.Generic;
using System.Collections;

public class SoundManager : SingletonBase<SoundManager>
{
    [SerializeField]
    private AudioMixer _mixer;
    [SerializeField]
    private AudioSource _bgSound;

    private void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

        // 초기 BGM 설정
        if (!string.IsNullOrEmpty(SceneManager.GetActiveScene().name))
        {
            BgSoundPlay(SceneManager.GetActiveScene().name);
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // SFX 재생 (오브젝트 풀링 적용)
    public void SFXPlay(string sfxName)
    {
        AudioClip clip = Resources.Load<AudioClip>($"Audio/SFX/{sfxName}");
        if (clip == null)
        {
            Debug.LogWarning($"SFX '{sfxName}' not found in Resources/Audio/SFX/");
            return;
        }



        GameObject go = new GameObject(sfxName + "Sound");
        AudioSource audioSource = go.AddComponent<AudioSource>();

        audioSource.clip = clip;
        audioSource.Play();

        // 클립이 끝난 후 AudioSource 비활성화
        StartCoroutine(DisableAudioSource(audioSource, clip.length));
    }

    // BGM 재생
    private void BgSoundPlay(string sceneName)
    {
        AudioClip clip = Resources.Load<AudioClip>($"Audio/BGM/{sceneName}");
        if (clip == null)
        {
            Debug.LogWarning($"BGM for scene '{sceneName}' not found in Resources/Audio/BGM/");
            return;
        }

        _bgSound.outputAudioMixerGroup = _mixer.FindMatchingGroups("BGM")[0];
        _bgSound.clip = clip;
        _bgSound.loop = true;
        _bgSound.volume = 1f;
        _bgSound.Play();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BgSoundPlay(scene.name);
    }

    // AudioSource 비활성화 코루틴
    private IEnumerator DisableAudioSource(AudioSource source, float delay)
    {
        yield return new WaitForSeconds(delay);
        source.gameObject.SetActive(false);
    }
}
