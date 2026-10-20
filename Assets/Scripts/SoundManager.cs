using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;
using System.Collections;

public class SoundManager : SingletonBase<SoundManager>
{
    private const string DefaultBgmFolder = "Audio/BGM/RPG Medieval Times";

    [SerializeField]
    private AudioMixer _mixer;
    [SerializeField]
    private AudioSource _bgSound;

    [Header("BGM")]
    [Tooltip("Resources/Audio/BGM 아래의 폴더입니다. 확장자는 입력하지 않습니다.")]
    [SerializeField] private string bgmResourceFolder = DefaultBgmFolder;
    [Tooltip("씬이 처음 열릴 때 재생할 BGM 이름입니다. 확장자는 입력하지 않습니다.")]
    [SerializeField] private string defaultBgmName = "RPG_Love,Calmness,Nature_02";

    private void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

        // �ʱ� BGM ����
        BGMPlay(defaultBgmName);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // SFX ��� (������Ʈ Ǯ�� ����)
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

        // Ŭ���� ���� �� AudioSource ��Ȱ��ȭ
        StartCoroutine(DisableAudioSource(audioSource, clip.length));
    }

    // BGM ���
    public void BGMPlay(string bgmName)
    {
        if (string.IsNullOrWhiteSpace(bgmName))
        {
            Debug.LogWarning("BGM name is empty.", this);
            return;
        }

        string trimmedName = bgmName.Trim();
        string resourcePath = trimmedName.Contains("/")
            ? $"Audio/BGM/{trimmedName.TrimStart('/')}"
            : $"{bgmResourceFolder.TrimEnd('/')}/{trimmedName}";
        AudioClip clip = Resources.Load<AudioClip>(resourcePath);
        if (clip == null)
        {
            Debug.LogWarning($"BGM '{trimmedName}' not found at Resources/{resourcePath}.", this);
            return;
        }

        if (_bgSound == null)
            _bgSound = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        if (_bgSound.clip == clip && _bgSound.isPlaying)
            return;

        if (_mixer != null)
        {
            AudioMixerGroup[] groups = _mixer.FindMatchingGroups("BGM");
            if (groups.Length > 0)
                _bgSound.outputAudioMixerGroup = groups[0];
        }
        _bgSound.clip = clip;
        _bgSound.loop = true;
        _bgSound.volume = 1f;
        _bgSound.Play();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BGMPlay(defaultBgmName);
    }

    // AudioSource ��Ȱ��ȭ �ڷ�ƾ
    private IEnumerator DisableAudioSource(AudioSource source, float delay)
    {
        yield return new WaitForSeconds(delay);
        source.gameObject.SetActive(false);
    }
}
