// Scripts/AudioPlaybackController.cs
// 録音音声の再生を管理するコントローラー
using UnityEngine;
using System;

/// <summary>
/// 録音音声の再生を管理します。
/// フォーカス中の波紋に関連した音声を再生・停止できます。
/// </summary>
public class AudioPlaybackController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AudioSource audioSource;
    
    public bool IsPlaying => audioSource != null && audioSource.isPlaying;
    public float PlaybackProgress => (audioSource != null && audioSource.clip != null && audioSource.clip.length > 0) 
        ? audioSource.time / audioSource.clip.length 
        : 0f;
    public float CurrentTime => audioSource != null ? audioSource.time : 0f;
    public float TotalDuration => (audioSource != null && audioSource.clip != null) ? audioSource.clip.length : 0f;
    
    public event Action OnPlaybackStarted;
    public event Action OnPlaybackFinished;
    
    // 現在の再生情報
    private string currentMonthKey;
    private string currentAudioFileName;
    
    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        
        // 空間音響を無効化 (2D再生)
        audioSource.spatialBlend = 0f;
        audioSource.playOnAwake = false;
    }

    private bool wasPlaying = false;

    private void Update()
    {
        // 再生終了検出（再生中→停止への遷移を検出）
        if (audioSource != null && audioSource.clip != null)
        {
            if (wasPlaying && !audioSource.isPlaying)
            {
                // 再生が終了した
                Debug.Log("[AudioPlaybackController] Playback finished (detected in Update)");
                wasPlaying = false;
                audioSource.clip = null;
                OnPlaybackFinished?.Invoke();
            }
            else if (audioSource.isPlaying)
            {
                wasPlaying = true;
            }
        }
    }

    /// <summary>
    /// 指定されたEmotionPointの録音を再生します。
    /// </summary>
    public void PlayRecording(string monthKey, string audioFileName)
    {
        if (string.IsNullOrEmpty(audioFileName))
        {
            Debug.LogWarning("[AudioPlaybackController] No audio file specified.");
            return;
        }
        
        if (AudioStorageManager.Instance == null)
        {
            Debug.LogError("[AudioPlaybackController] AudioStorageManager not available.");
            return;
        }
        
        // 同じファイルを再生中ならトグル動作 (停止)
        if (IsPlaying && currentAudioFileName == audioFileName)
        {
            StopPlayback();
            return;
        }
        
        // 再生可否チェック
        if (!AudioStorageManager.Instance.CanPlayRecording(monthKey))
        {
            Debug.LogWarning($"[AudioPlaybackController] Cannot play recording from {monthKey} - plan restriction.");
            // TODO: UIで「アップグレードで再生可能」を表示
            return;
        }
        
        // ファイル存在チェック
        if (!AudioStorageManager.Instance.RecordingExists(monthKey, audioFileName))
        {
            Debug.LogWarning($"[AudioPlaybackController] Recording file not found: {monthKey}/{audioFileName}");
            return;
        }
        
        currentMonthKey = monthKey;
        currentAudioFileName = audioFileName;
        
        // 非同期でロードして再生
        AudioStorageManager.Instance.LoadRecording(monthKey, audioFileName, OnAudioLoaded);
    }
    
    private void OnAudioLoaded(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogError("[AudioPlaybackController] Failed to load audio clip.");
            return;
        }
        
        audioSource.clip = clip;
        audioSource.Play();
        
        Debug.Log($"[AudioPlaybackController] Playing: {currentAudioFileName} ({clip.length:F2}s)");
        OnPlaybackStarted?.Invoke();
    }
    
    /// <summary>
    /// 再生を停止します。
    /// </summary>
    public void StopPlayback()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            Debug.Log("[AudioPlaybackController] Playback stopped.");
        }
        
        if (audioSource != null)
        {
            audioSource.clip = null;
        }
        
        currentAudioFileName = null;
        currentMonthKey = null;
        
        OnPlaybackFinished?.Invoke();
    }
    
    /// <summary>
    /// 再生位置をシークします。
    /// </summary>
    /// <param name="normalizedPosition">0-1の正規化された位置</param>
    public void Seek(float normalizedPosition)
    {
        if (audioSource != null && audioSource.clip != null)
        {
            audioSource.time = Mathf.Clamp(normalizedPosition * audioSource.clip.length, 0f, audioSource.clip.length);
        }
    }
}
