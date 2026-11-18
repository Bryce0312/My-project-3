using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Whisper;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
using UnityEngine.Networking;
#endif

internal sealed class AnnotationVoiceInput : MonoBehaviour
{
    private const int MaximumSeconds = 12;
    private const float MicrophoneStartTimeout = 2f;
    private const float MinimumSeconds = .35f;

    private enum State { Idle, Permission, Starting, Recording, Transcribing }

    private AudioClip recording;
    private State state;
    private int attempt;
    private float deadline;
    private Action<string, bool> statusChanged;
    private Action<string> textRecognized;

    public static bool IsSupported => Application.platform == RuntimePlatform.Android
        || Application.platform == RuntimePlatform.WindowsEditor
        || Application.platform == RuntimePlatform.WindowsPlayer;

    public bool IsBusy => state != State.Idle;
    public bool IsRecording => state == State.Recording;

    public void Bind(Action<string, bool> onStatusChanged, Action<string> onTextRecognized)
    {
        statusChanged = onStatusChanged;
        textRecognized = onTextRecognized;
    }

    public void Toggle()
    {
        switch (state)
        {
            case State.Idle: RequestMicrophone(); break;
            case State.Recording: StopAndTranscribe(); break;
            default: Cancel(); break;
        }
    }

    public void Cancel() => Cancel(null);

    private void Cancel(string message)
    {
        attempt++;
        ReleaseRecording();
        ChangeState(State.Idle, message);
    }

    private void RequestMicrophone()
    {
        if (!IsSupported || !isActiveAndEnabled) return;
        int request = ++attempt;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            ChangeState(State.Permission, "Allow microphone access to dictate.");
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => BeginRecording(request);
            callbacks.PermissionDenied += _ => Finish(request, "Microphone access was denied.", true);
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            return;
        }
#endif
        BeginRecording(request);
    }

    private void BeginRecording(int request)
    {
        if (!IsCurrent(request)) return;
        try
        {
            recording = Microphone.Start(null, false, MaximumSeconds + 3, WhisperWrapper.WhisperSampleRate);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not open the dictation microphone: " + exception.Message);
        }

        if (recording == null)
        {
            Finish(request, "Microphone unavailable. Check the input device.", true);
            return;
        }

        deadline = Time.realtimeSinceStartup + MicrophoneStartTimeout;
        ChangeState(State.Starting, "Starting microphone...");
    }

    private void Update()
    {
        switch (state)
        {
            case State.Starting:
                if (Microphone.GetPosition(null) > 0)
                {
                    deadline = Time.realtimeSinceStartup + MaximumSeconds;
                    ChangeState(State.Recording, "Listening in English. Press Stop when done.");
                }
                else if (Time.realtimeSinceStartup >= deadline)
                {
                    ReleaseRecording();
                    Finish(attempt, "Microphone did not start. Try again.", true);
                }
                break;
            case State.Recording:
                if (Time.realtimeSinceStartup >= deadline) StopAndTranscribe();
                break;
        }
    }

    private async void StopAndTranscribe()
    {
        int request = attempt;
        float[] samples = null;
        int rate = recording.frequency;
        int channels = recording.channels;
        try
        {
            int frames = Microphone.GetPosition(null);
            if (frames == 0 && !Microphone.IsRecording(null))
                frames = recording.samples;

            if (frames >= rate * MinimumSeconds)
            {
                samples = new float[frames * channels];
                if (!recording.GetData(samples, 0)) samples = null;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not read the dictation microphone: " + exception.Message);
        }
        finally
        {
            ReleaseRecording();
        }

        if (samples == null || Array.TrueForAll(samples, sample => Mathf.Abs(sample) < .008f))
        {
            Finish(request, "No clear speech captured. Try speaking again.", true);
            return;
        }

        ChangeState(State.Transcribing, "Transcribing English on this device...");
        try
        {
            string transcript = await EnglishDictation.TranscribeAsync(samples, rate, channels);
            if (!IsCurrent(request)) return;
            if (string.IsNullOrWhiteSpace(transcript))
            {
                Finish(request, "No English speech recognized. Try again.", true);
                return;
            }
            Finish(request, null);
            textRecognized?.Invoke(transcript);
        }
        catch (Exception exception)
        {
            if (!IsCurrent(request)) return;
            Debug.LogWarning("English dictation failed: " + exception.Message);
            Finish(request, "Dictation failed. Try again or type your note.", true);
        }
    }

    private bool IsCurrent(int request) => this != null && isActiveAndEnabled && attempt == request;

    private void Finish(int request, string message, bool error = false)
    {
        if (IsCurrent(request)) ChangeState(State.Idle, message, error);
    }

    private void ChangeState(State next, string message, bool error = false)
    {
        state = next;
        statusChanged?.Invoke(message, error);
    }

    private void ReleaseRecording()
    {
        if (recording == null) return;
        var clip = recording;
        recording = null;
        try
        {
            Microphone.End(null);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not close the dictation microphone: " + exception.Message);
        }
        finally
        {
            Destroy(clip);
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && (state == State.Starting || state == State.Recording))
            Cancel("Recording stopped when the app paused.");
    }

    private void OnDisable()
    {
        attempt++;
        state = State.Idle;
        ReleaseRecording();
    }
}

internal static class EnglishDictation
{
    private const string ModelName = "ggml-tiny.en-q5_1.bin";
    private static Task<WhisperWrapper> modelTask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetModel() => modelTask = null;

    public static async Task<string> TranscribeAsync(float[] samples, int sampleRate, int channels)
    {
        int threads = Mathf.Clamp(SystemInfo.processorCount / 2, 1, 4);
        if (modelTask == null || modelTask.IsFaulted || modelTask.IsCanceled
            || (modelTask.Status == TaskStatus.RanToCompletion && modelTask.Result == null))
        {
            var context = WhisperContextParams.GetDefaultParams();
            context.UseGpu = false;
            modelTask = LoadModelAsync(Path.Combine(Application.streamingAssetsPath, "Dictation", ModelName), context);
        }

        var model = await modelTask.ConfigureAwait(false);
        if (model == null) throw new InvalidOperationException("The English Whisper model could not be loaded.");

        var result = await Task.Run(() =>
        {
            var options = WhisperParams.GetDefaultParams();
            options.Language = "en";
            options.Translate = false;
            options.NoContext = true;
            options.ThreadsCount = threads;
            return model.GetText(samples, sampleRate, channels, options);
        }).ConfigureAwait(false);
        string text = result?.Result.Trim();
        // Whisper sometimes emits only an annotation such as [silence] or (music).
        if (!string.IsNullOrEmpty(text) && ((text[0] == '[' && text[text.Length - 1] == ']')
            || (text[0] == '(' && text[text.Length - 1] == ')')))
            return null;
        return text;
    }

    private static async Task<WhisperWrapper> LoadModelAsync(string path, WhisperContextParams context)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // StreamingAssets is inside the APK and must be read through UnityWebRequest.
        byte[] weights;
        using (var request = UnityWebRequest.Get(path))
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException("Could not read the packaged dictation model: " + request.error);
            weights = request.downloadHandler.data;
        }
        return await Task.Run(() => WhisperWrapper.InitFromBuffer(weights, context)).ConfigureAwait(false);
#else
        return await Task.Run(() => WhisperWrapper.InitFromFile(path, context)).ConfigureAwait(false);
#endif
    }
}
