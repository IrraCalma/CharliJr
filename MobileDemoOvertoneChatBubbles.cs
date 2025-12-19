using UnityEngine;
using UnityEngine.UI; // Button, Toggle, ScrollRect, LayoutRebuilder
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro; // TextMeshPro
using LLMUnity;

// Overtone
using LeastSquares.Overtone; // TTSEngine, TTSVoiceNative

namespace LLMUnitySamples
{
    public class MobileDemoOvertoneChatBubbles : MonoBehaviour
    {
        [Header("LLM Unity")]
        public LLMCharacter llmCharacter;

        [Header("UI (TMP)")]
        public GameObject ChatPanel;
        public TMP_InputField playerText;
        public GameObject ErrorText;

        [Header("Chat Bubbles")]
        [Tooltip("Content del ScrollRect donde se instancian las bubbles")]
        public Transform messagesContainer;
        [Tooltip("Prefab de burbuja de usuario (debe tener un TMP_Text llamado 'User_Txt')")]
        public GameObject userBubblePrefab;
        [Tooltip("Prefab de burbuja de bot (debe tener un TMP_Text llamado 'Ai_Txt')")]
        public GameObject botBubblePrefab;
        [Tooltip("Prefab de burbuja de thinking (debe tener 'Think_Txt' y 'Title_Txt')")]
        public GameObject thinkingBubblePrefab;
        [Tooltip("ScrollRect del chat (opcional, para auto-scroll)")]
        public ScrollRect scrollRect;

        // Referencias internas para el turno actual
        private TMP_Text botText;                 // Ai_Txt
        private TMP_Text thinkingBodyText;        // Think_Txt
        private TMP_Text thinkingTitleText;       // Title_Txt
        private CanvasGroup thinkingTitleCanvasGroup;
        private Coroutine thinkingBlinkCoroutine;

        // Estados de thinking
        private bool thinkingBubbleEnabledThisTurn = false;
        private bool thinkingStarted = false;
        private bool thinkingFinished = false;
        private float thinkingStartTime = 0f;

        [Header("Send Button")]
        public Button sendButton;

        [Header("Thinking Mode")]
        public Toggle thinkingToggle;   // Toggle UI (Qwen3Template invierte internamente)

        [Header("Overtone TTS")]
        public bool ttsEnabled = true;
        [Tooltip("Motor Overtone en la escena")]
        public TTSEngine overtoneEngine;
        [Tooltip("Actor Overtone (nuestro SO)")]
        public OvertoneVoiceActorSO actor;
        [Tooltip("Código de idioma (ej: es, en, pt...)")]
        public string ttsLanguage = "es";

        [Header("TTS Tags")]
        public string defaultMoodTag = "HAPPY";

        [Header("Streaming TTS")]
        public int maxChunkCharsWithoutPunctuation = 240;
        public float tinyGapSeconds = 0.08f;

        private readonly Queue<string> _sentenceQueue = new Queue<string>();
        private Coroutine _ttsPlaybackCoroutine;
        private bool _isCancelling = false;
        private int _lastConsumedIndex = 0;

        // Player interno (igual patrón que el script original)
        private GameObject _ttsPlayerGO;
        private AudioSource _ttsSource;
        private bool _isClipBuildingOrPlaying = false;

        [Header("FX (opcional)")]
        public bool useFx = false;

        [Header("FX Básicos")]
        public bool fx_gain = false;
        public float fx_gainAmount = 1.0f;

        public bool fx_echo = false;
        public int fx_echoDelayMs = 250;
        [Range(0f, 0.9f)] public float fx_echoDecay = 0.5f;

        public bool fx_reverb = false;
        public bool fx_telephone = false;
        public bool fx_underwater = false;

        public bool fx_distortion = false;
        [Range(0.1f, 1f)] public float fx_distortionThreshold = 0.6f;

        public bool fx_robot = false;
        public bool fx_reverse = false;

        [Header("Pitch independiente")]
        public bool fx_pitchIndependent = false;
        [Range(-24f, 24f)]
        public float fx_pitchSemitones = 0f; // +12 = una octava arriba, -12 = una abajo

        [Header("Radio / Satélite")]
        public bool fx_spaceRadio = false;
        [Range(0f, 0.2f)]
        public float fx_spaceRadioNoise = 0.03f;

        // Paralelización de “builds”
        private readonly Queue<BuiltAudio> _readyClips = new Queue<BuiltAudio>();
        private readonly List<Task<BuiltAudio>> _builders = new List<Task<BuiltAudio>>();
        [SerializeField] private int maxConcurrentBuilders = 2;

        [Header("UI Manager")]
        public ChatUIManager uiManager;   // ← referencia al UI Manager

        // ─────────────────────────────────────────────────────────────────────
        async void Start()
        {
            // ENTER desde el input
            if (playerText != null)
            {
                playerText.onSubmit.AddListener(OnInputFieldSubmit);
                playerText.interactable = false;
            }

            // Botón Send
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(OnSendButtonClicked);
            }

            // Thinking toggle -> Qwen3Template (invertido dentro del template)
            if (thinkingToggle != null)
            {
                Qwen3Template.SetGlobalThinkingMode(thinkingToggle.isOn);
                thinkingToggle.onValueChanged.AddListener(OnThinkingToggleChanged);
            }

            await WarmUp();

            // Habilitar input tras warmup (SIN abrir teclado)
            if (playerText != null)
            {
                playerText.interactable = true;
                playerText.text = "";
            }
        }

        private void OnThinkingToggleChanged(bool isOn)
        {
            Qwen3Template.SetGlobalThinkingMode(isOn);
        }

        async Task WarmUp()
        {
            await llmCharacter.Warmup();
        }

        // ───────────────────── ENTRADA DEL USUARIO ─────────────────────

        public void OnSendButtonClicked()
        {
            if (playerText == null) return;
            string msg = playerText.text;
            if (string.IsNullOrWhiteSpace(msg)) return;

            OnInputFieldSubmit(msg);
        }

        void OnInputFieldSubmit(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            StopStreamingTTS();

            // ✅ CLAVE: bloquear y limpiar input inmediatamente
            // ya tenemos "message" capturado.
            if (playerText != null)
            {
                playerText.interactable = false;
                playerText.text = "";
            }

            _lastConsumedIndex = 0;

            // Reset de thinking para ESTE turno
            thinkingStarted = false;
            thinkingFinished = false;
            thinkingStartTime = 0f;
            StopThinkingBlink();
            thinkingBodyText = null;
            thinkingTitleText = null;
            thinkingTitleCanvasGroup = null;

            // ¿Instanciamos burbuja de thinking este turno?
            thinkingBubbleEnabledThisTurn = (thinkingToggle != null && thinkingToggle.isOn);

            // 1) Burbuja del usuario
            CreateUserBubble(message);

            // 2) Burbuja de thinking (si el toggle está ON)
            if (thinkingBubbleEnabledThisTurn)
            {
                CreateThinkingBubble();
            }

            // 3) Burbuja del bot (respuesta visible)
            CreateBotBubble();
            if (botText != null)
                botText.text = "...";

            // 4) Lanzar el chat con streaming hacia SetAIText
            _ = llmCharacter.Chat(message, SetAIText, AIReplyComplete);

            // Avisar al UI Manager: hemos enviado una consulta al LLM
            if (uiManager != null)
                uiManager.OnUserMessageSentFromChat();
        }

        // Enviar un mensaje al LLM desde fuera (voz -> texto)
        public void SendExternalMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            StopStreamingTTS();

            // ✅ CLAVE: igual que al enviar manualmente.
            if (playerText != null)
            {
                playerText.interactable = false;
                playerText.text = "";
            }

            _lastConsumedIndex = 0;

            // Reset de thinking para ESTE turno
            thinkingStarted = false;
            thinkingFinished = false;
            thinkingStartTime = 0f;
            StopThinkingBlink();
            thinkingBodyText = null;
            thinkingTitleText = null;
            thinkingTitleCanvasGroup = null;

            // ¿Instanciamos burbuja de thinking este turno?
            thinkingBubbleEnabledThisTurn = (thinkingToggle != null && thinkingToggle.isOn);

            // 1) Burbuja del usuario
            CreateUserBubble(message);

            // 2) Burbuja de thinking (si el toggle está ON)
            if (thinkingBubbleEnabledThisTurn)
            {
                CreateThinkingBubble();
            }

            // 3) Burbuja del bot (respuesta visible)
            CreateBotBubble();
            if (botText != null)
                botText.text = "...";

            // 4) Lanzar el chat con streaming hacia SetAIText
            _ = llmCharacter.Chat(message, SetAIText, AIReplyComplete);

            // Avisar al UI Manager: consulta enviada desde voz
            if (uiManager != null)
                uiManager.OnUserMessageSentFromChat();
        }

        // ───────────────────── SPLIT DE <think> ─────────────────────

        private static void SplitThinkingFromText(string fullText, out string thinkingPart, out string visiblePart)
        {
            thinkingPart = "";
            visiblePart = fullText ?? "";

            if (string.IsNullOrEmpty(fullText))
                return;

            string lower = fullText.ToLowerInvariant();
            const string openTag = "<think>";
            const string closeTag = "</think>";

            int openIdx = lower.IndexOf(openTag);
            if (openIdx < 0)
            {
                thinkingPart = "";
                visiblePart = fullText;
                return;
            }

            int contentStart = openIdx + openTag.Length;

            int closeIdx = lower.IndexOf(closeTag, contentStart);
            if (closeIdx < 0)
            {
                if (contentStart < fullText.Length)
                    thinkingPart = fullText.Substring(contentStart).Trim();
                else
                    thinkingPart = "";

                visiblePart = "";
                return;
            }

            int contentLen = closeIdx - contentStart;
            if (contentLen > 0)
                thinkingPart = fullText.Substring(contentStart, contentLen).Trim();
            else
                thinkingPart = "";

            int answerStart = closeIdx + closeTag.Length;
            if (answerStart < fullText.Length)
                visiblePart = fullText.Substring(answerStart).TrimStart();
            else
                visiblePart = "";
        }

        // ───────────────────── STREAMING DEL BOT ─────────────────────

        public void SetAIText(string text)
        {
            SplitThinkingFromText(text, out string thinkingPart, out string visiblePart);

            if (!thinkingBubbleEnabledThisTurn)
            {
                if (botText != null)
                    botText.text = visiblePart;

                EnqueueNewCompletedSentences(visiblePart);
                EnsurePlaybackCoroutine();
                ScrollToBottom();
                return;
            }

            if (!thinkingStarted && !string.IsNullOrEmpty(thinkingPart))
            {
                thinkingStarted = true;
                thinkingStartTime = Time.time;
                StartThinkingBlink();
            }

            if (thinkingStarted && !thinkingFinished && !string.IsNullOrEmpty(visiblePart))
            {
                thinkingFinished = true;
                float elapsed = Time.time - thinkingStartTime;

                StopThinkingBlink();

                if (thinkingTitleText != null)
                    thinkingTitleText.text = $"Thought for {elapsed:0.0}s";

                Debug.Log($"[Thinking] Tiempo de razonamiento: {elapsed:0.000} s");
            }

            if (thinkingBodyText != null)
                thinkingBodyText.text = thinkingPart;

            if (botText != null)
                botText.text = visiblePart;

            EnqueueNewCompletedSentences(visiblePart);
            EnsurePlaybackCoroutine();

            ScrollToBottom();
        }

        public void AIReplyComplete()
        {
            if (botText != null)
                EnqueueTailIfAny(botText.text);

            EnsurePlaybackCoroutine();

            if (playerText != null)
            {
                playerText.interactable = true;
                playerText.text = "";
            }

            ScrollToBottom();

            if (uiManager != null)
                uiManager.OnBotReplyFinishedFromChat();
        }

        public void CancelRequests()
        {
            llmCharacter.CancelRequests();
            StopStreamingTTS();
            StopThinkingBlink();
            thinkingStarted = false;
            thinkingFinished = true;
            AIReplyComplete();
        }

        public void ExitGame()
        {
            Debug.Log("Exit button clicked");
            Application.Quit();
        }

        bool onValidateWarning = true;
        bool onValidateInfo = true;
        void OnValidate()
        {
            if (onValidateWarning && llmCharacter != null && !llmCharacter.remote && llmCharacter.llm != null && llmCharacter.llm.model == "")
            {
                Debug.LogWarning($"Please select a model in the {llmCharacter.llm.gameObject.name} GameObject!");
                onValidateWarning = false;
            }
            if (onValidateInfo && llmCharacter != null && llmCharacter.llm != null)
            {
                Debug.Log($"Select 'Download On Start' in the {llmCharacter.llm.gameObject.name} GameObject to download the models when the app starts.");
                onValidateInfo = false;
            }
        }

        // ───────────────────── CHAT BUBBLES HELPERS ─────────────────────

        private static TMP_Text FindChildTMPByName(GameObject root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;
            TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in tmps)
            {
                if (t.name == childName)
                    return t;
            }
            return null;
        }

        private void CreateUserBubble(string message)
        {
            if (userBubblePrefab == null || messagesContainer == null)
            {
                Debug.LogWarning("[ChatBubbles] Falta userBubblePrefab o messagesContainer.");
                return;
            }

            GameObject bubble = Instantiate(userBubblePrefab, messagesContainer);
            TMP_Text txt = FindChildTMPByName(bubble, "User_Txt") ?? bubble.GetComponentInChildren<TMP_Text>();

            if (txt != null)
                txt.text = message;

            ScrollToBottom();
        }

        private void CreateBotBubble()
        {
            if (botBubblePrefab == null || messagesContainer == null)
            {
                Debug.LogWarning("[ChatBubbles] Falta botBubblePrefab o messagesContainer.");
                return;
            }

            GameObject bubble = Instantiate(botBubblePrefab, messagesContainer);
            botText = FindChildTMPByName(bubble, "Ai_Txt") ?? bubble.GetComponentInChildren<TMP_Text>();

            if (botText == null)
            {
                Debug.LogWarning("[ChatBubbles] El prefab de bot no tiene TMP_Text llamado 'Ai_Txt'.");
            }
            else
            {
                botText.text = "";
            }

            ScrollToBottom();
        }

        private void CreateThinkingBubble()
        {
            if (thinkingBubblePrefab == null || messagesContainer == null)
            {
                Debug.LogWarning("[ChatBubbles] Falta thinkingBubblePrefab o messagesContainer.");
                return;
            }

            GameObject bubble = Instantiate(thinkingBubblePrefab, messagesContainer);

            thinkingBodyText = FindChildTMPByName(bubble, "Think_Txt");
            thinkingTitleText = FindChildTMPByName(bubble, "Title_Txt");

            if (thinkingBodyText != null)
                thinkingBodyText.text = "";

            if (thinkingTitleText != null)
            {
                thinkingTitleText.text = "Thinking";

                thinkingTitleCanvasGroup = thinkingTitleText.GetComponent<CanvasGroup>();
                if (thinkingTitleCanvasGroup == null)
                {
                    Debug.LogWarning("[ChatBubbles] Title_Txt no tiene CanvasGroup, no se animará el alpha.");
                }
                else
                {
                    thinkingTitleCanvasGroup.alpha = 1f;
                }
            }
            else
            {
                thinkingTitleCanvasGroup = null;
            }

            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            if (messagesContainer == null) return;

            Canvas.ForceUpdateCanvases();

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 0f;

            RectTransform rt = messagesContainer as RectTransform;
            if (rt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        // ─────────── Animación de blink del título "Thinking" ───────────

        private void StartThinkingBlink()
        {
            if (thinkingTitleCanvasGroup == null) return;

            StopThinkingBlink();
            thinkingBlinkCoroutine = StartCoroutine(ThinkingBlinkRoutine());
        }

        private void StopThinkingBlink()
        {
            if (thinkingBlinkCoroutine != null)
            {
                StopCoroutine(thinkingBlinkCoroutine);
                thinkingBlinkCoroutine = null;
            }

            if (thinkingTitleCanvasGroup != null)
                thinkingTitleCanvasGroup.alpha = 1f;
        }

        private IEnumerator ThinkingBlinkRoutine()
        {
            const float speed = 2.0f;
            const float minAlpha = 0.3f;
            const float maxAlpha = 1.0f;

            while (!thinkingFinished)
            {
                float t = Mathf.PingPong(Time.time * speed, 1f);
                float a = Mathf.Lerp(minAlpha, maxAlpha, t);
                thinkingTitleCanvasGroup.alpha = a;
                yield return null;
            }

            thinkingTitleCanvasGroup.alpha = 1f;
            thinkingBlinkCoroutine = null;
        }

        /*──────────────── STREAMING ORACIONES (TTS) ───────────────*/

        private void EnqueueNewCompletedSentences(string fullText)
        {
            if (!ttsEnabled) return;
            if (string.IsNullOrEmpty(fullText) || _lastConsumedIndex >= fullText.Length) return;

            int start = _lastConsumedIndex;
            for (int i = _lastConsumedIndex; i < fullText.Length; i++)
            {
                char c = fullText[i];
                if (c == '.' || c == '!' || c == '?')
                {
                    int len = i - start + 1;
                    if (len > 0)
                    {
                        string sentence = fullText.Substring(start, len).Trim();
                        start = i + 1;
                        if (!string.IsNullOrWhiteSpace(sentence))
                        {
                            _sentenceQueue.Enqueue(sentence);
                            Debug.Log($"[TTS][enqueue] {sentence}");
                        }
                    }
                }
            }
            _lastConsumedIndex = Mathf.Clamp(start, 0, fullText.Length);
        }

        private void EnqueueTailIfAny(string fullText)
        {
            if (!ttsEnabled) return;
            if (string.IsNullOrEmpty(fullText) || _lastConsumedIndex >= fullText.Length) return;

            string tail = fullText.Substring(_lastConsumedIndex).Trim();
            if (string.IsNullOrWhiteSpace(tail)) { _lastConsumedIndex = fullText.Length; return; }

            if (!HasSentencePunctuation(tail))
                tail += ".";

            _sentenceQueue.Enqueue(tail);
            _lastConsumedIndex = fullText.Length;
            Debug.Log($"[TTS][enqueue-tail] {tail}");
        }

        private void EnsurePlaybackCoroutine()
        {
            if (!ttsEnabled) return;
            if (_ttsPlaybackCoroutine == null)
            {
                _isCancelling = false;
                _ttsPlaybackCoroutine = StartCoroutine(TTSPlaybackLoop());
            }
        }

        private IEnumerator TTSPlaybackLoop()
        {
            StartCoroutine(BuilderWatcher());

            while (true)
            {
                if (_isCancelling) break;

                while (_sentenceQueue.Count > 0 && _builders.Count < maxConcurrentBuilders)
                {
                    string sentence = _sentenceQueue.Dequeue();
                    if (!string.IsNullOrWhiteSpace(sentence))
                    {
                        var t = BuildTaggedSentenceAsync_NoPlay(sentence);
                        _builders.Add(t);
                    }
                }

                if (_readyClips.Count > 0 && !_isClipBuildingOrPlaying)
                {
                    var audio = _readyClips.Dequeue();
                    if (audio.samples != null && audio.samples.Length > 0)
                    {
                        _isClipBuildingOrPlaying = true;
                        PlaySamples(audio.samples, audio.sampleRate);
                    }
                }

                if (tinyGapSeconds > 0f) yield return new WaitForSecondsRealtime(tinyGapSeconds);
                else yield return null;
            }
            _ttsPlaybackCoroutine = null;
            yield break;
        }

        private void StopStreamingTTS()
        {
            _isCancelling = true;
            _sentenceQueue.Clear();

            if (_ttsPlaybackCoroutine != null)
            {
                StopCoroutine(_ttsPlaybackCoroutine);
                _ttsPlaybackCoroutine = null;
            }

            _readyClips.Clear();
            _builders.Clear();
            StopAndCleanupInternalPlayer();
        }

        private static bool HasSentencePunctuation(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '.' || c == '!' || c == '?') return true;
            }
            return false;
        }

        /*──────────────── Sanitización + Tags ───────────────*/

        private static string StripToAllowedChars(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string decomposed = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);

            foreach (char ch in decomposed)
            {
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;

                if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ||
                    ch == '?' || ch == '!' || ch == '.' || ch == ','
                    || ch == '[' || ch == ']' || ch == '\'' || ch == '<' || ch == '>' || ch == '/')
                {
                    sb.Append(ch);
                }
            }
            var outStr = sb.ToString().Normalize(NormalizationForm.FormC);
            return Regex.Replace(outStr, @"\s+", " ").Trim();
        }

        private static string NormalizeEmotionMarkup(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var rx = new Regex(@"\[(HAPPY|NEUTRAL|SAD|ANGRY|FEAR|DISGUST|SURPRISE)\]\s*(.*?)\s*\[END\]",
                               RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return rx.Replace(input, m => $"<emotion='{m.Groups[1].Value.ToLower()}'>{m.Groups[2].Value}</emotion>");
        }

        private static string RemovePauseTagsPlain(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Regex.Replace(s, @"\[(?:Pause(?:\+|\-)?)]", "", RegexOptions.IgnoreCase);
        }

        private IEnumerator BuilderWatcher()
        {
            var toRemove = new List<Task<BuiltAudio>>();
            while (!_isCancelling)
            {
                toRemove.Clear();
                foreach (var t in _builders)
                {
                    if (t.IsCompleted)
                    {
                        try
                        {
                            var audio = t.Result;
                            if (audio.samples != null && audio.samples.Length > 0)
                                _readyClips.Enqueue(audio);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning("[TTS] builder task error: " + ex.Message);
                        }
                        toRemove.Add(t);
                    }
                }
                if (toRemove.Count > 0) _builders.RemoveAll(x => toRemove.Contains(x));
                yield return null;
            }
        }

        private async Task<BuiltAudio> BuildTaggedSentenceAsync_NoPlay(string rawSentence)
        {
            try
            {
                string cleaned = StripToAllowedChars(rawSentence);
                if (string.IsNullOrWhiteSpace(cleaned)) return default;

                if (!Regex.IsMatch(cleaned, @"\[(HAPPY|NEUTRAL|SAD|ANGRY|FEAR|DISGUST|SURPRISE)\]", RegexOptions.IgnoreCase)
                    && !Regex.IsMatch(cleaned, @"<emotion='(.*?)'>", RegexOptions.IgnoreCase))
                {
                    string mood = string.IsNullOrWhiteSpace(defaultMoodTag) ? "HAPPY" : defaultMoodTag.Trim().ToUpperInvariant();
                    cleaned = $"[{mood}] {cleaned} [END]";
                }

                string normalized = NormalizeEmotionMarkup(cleaned);
                normalized = RemovePauseTagsPlain(normalized);
                var segments = ParseEmotionAndPauseTags(normalized);

                var actorVoice = actor != null ? actor.GetVoice(ttsLanguage) : null;
                if (actor == null || actorVoice == null)
                {
                    Debug.LogError($"[TTS] No OvertoneVoiceActorSO/voice defined for '{ttsLanguage}'.");
                    return default;
                }

                var all = new List<float>(8192);
                int sampleRate = 22050;

                var voiceModel = TTSVoiceNative.LoadVoiceFromResources(actorVoice.overtoneVoiceName);
                voiceModel.SetSpeakerId(actorVoice.speakerId);

                foreach (var seg in segments)
                {
                    if (seg.IsPause)
                    {
                        int ms = GetPauseMs(seg.pauseTag);
                        int silence = Mathf.RoundToInt(sampleRate * (ms / 1000f));
                        if (silence > 0) all.AddRange(new float[silence]);
                        continue;
                    }

                    string safeText = (seg.text ?? "").Replace("\n", " ").Replace("\r", " ").Trim();
                    if (string.IsNullOrWhiteSpace(safeText)) continue;

                    var clip = await overtoneEngine.Speak(safeText, voiceModel);
                    sampleRate = clip.frequency;

                    float[] samples = new float[clip.samples];
                    clip.GetData(samples, 0);

                    ApplyEmotionPreset(seg.emotion, actor, ttsLanguage, out float pitch, out float speed);

                    if (actorVoice != null && actorVoice.GetFxProfile(actor) != null && !useFx)
                    {
                        samples = actorVoice.ApplyFx(actor, samples, sampleRate, pitch, speed);
                    }
                    else
                    {
                        if (useFx)
                        {
                            samples = AudioFXProcessor.ApplyAll(
                                samples, sampleRate,
                                pitch, speed,
                                fx_gain, fx_gainAmount,
                                fx_echo, fx_echoDelayMs, fx_echoDecay,
                                fx_reverb,
                                fx_telephone,
                                fx_underwater,
                                fx_distortion, fx_distortionThreshold,
                                fx_robot,
                                fx_reverse,
                                fx_pitchIndependent, fx_pitchSemitones,
                                fx_spaceRadio, fx_spaceRadioNoise
                            );
                        }
                        else
                        {
                            samples = AudioFXProcessor.ApplyAll(
                                samples, sampleRate,
                                pitch, speed,
                                false, 1f,
                                false, 0f, 0f,
                                false,
                                false,
                                false,
                                false, 0.6f,
                                false,
                                false,
                                false, 0f,
                                false, 0f
                            );
                        }
                    }

                    all.AddRange(samples);
                }

                voiceModel?.Dispose();

                return new BuiltAudio { samples = all.ToArray(), sampleRate = sampleRate };
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[TTS] BuildTaggedSentenceAsync_NoPlay error: " + ex.Message);
                return default;
            }
        }

        private struct BuiltAudio { public float[] samples; public int sampleRate; }

        private void PlaySamples(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length == 0) { _isClipBuildingOrPlaying = false; return; }

            if (_ttsPlayerGO == null)
            {
                _ttsPlayerGO = new GameObject("Overtone_StreamPlayer");
                _ttsPlayerGO.hideFlags = HideFlags.HideAndDontSave;
                _ttsSource = _ttsPlayerGO.AddComponent<AudioSource>();
                _ttsSource.playOnAwake = false;
                _ttsSource.spatialBlend = 0f;
                _ttsSource.volume = 1f;
                _ttsSource.mute = false;
            }

            _ttsSource.Stop();
            _ttsSource.clip = null;

            var clip = AudioClip.Create("Overtone_StreamClip", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            _ttsSource.clip = clip;
            _ttsSource.Play();

            StartCoroutine(WaitUntilClipEnds());
        }

        private IEnumerator WaitUntilClipEnds()
        {
            while (_ttsSource != null && _ttsSource.isPlaying) yield return null;
            _isClipBuildingOrPlaying = false;
        }

        private void StopAndCleanupInternalPlayer(bool keepGO = false)
        {
            if (_ttsSource != null)
            {
                _ttsSource.Stop();
                _ttsSource.clip = null;
            }
            if (!keepGO && _ttsPlayerGO != null)
            {
                DestroyImmediate(_ttsPlayerGO);
                _ttsPlayerGO = null;
                _ttsSource = null;
            }
        }

        private static void ApplyEmotionPreset(string emotion, OvertoneVoiceActorSO actor, string lang, out float pitch, out float speed)
        {
            var voice = actor?.GetVoice(lang);
            pitch = voice != null ? voice.GetPitch(actor) : 1f;
            speed = voice != null ? voice.GetSpeed(actor) : 1f;

            if (string.IsNullOrEmpty(emotion)) return;

            switch (emotion.ToLower())
            {
                case "happy": pitch += 0.025f; speed += 0.15f; break;
                case "sad": pitch -= 0.025f; speed -= 0.3f; break;
                case "angry": pitch -= 0.025f; speed += 0.3f; break;
                case "fear": pitch += 0.025f; speed -= 0.35f; break;
                case "disgust": pitch -= 0.01f; speed -= 0.2f; break;
                case "surprise": pitch += 0.05f; speed += 0.35f; break;
            }
        }

        private static int GetPauseMs(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return 400;
            if (tag.IndexOf("Pause+", System.StringComparison.OrdinalIgnoreCase) >= 0) return 800;
            if (tag.IndexOf("Pause-", System.StringComparison.OrdinalIgnoreCase) >= 0) return 250;
            return 450;
        }

        private class EmotionSegment
        {
            public string text;
            public string emotion;
            public string pauseTag;
            public bool IsPause => !string.IsNullOrEmpty(pauseTag);
        }

        private static List<EmotionSegment> ParseEmotionAndPauseTags(string input)
        {
            var segments = new List<EmotionSegment>();
            if (string.IsNullOrEmpty(input)) return segments;

            int pos = 0;
            var regex = new Regex(@"(<emotion='(.*?)'>(.*?)<\/emotion>)|(\[Pause[\+\-]?\])", RegexOptions.IgnoreCase);
            var matches = regex.Matches(input);

            foreach (Match match in matches)
            {
                int matchIndex = match.Index;

                if (matchIndex > pos)
                {
                    string before = input.Substring(pos, matchIndex - pos).Trim();
                    if (!string.IsNullOrWhiteSpace(before))
                        segments.Add(new EmotionSegment { text = before });
                }

                if (match.Groups[1].Success)
                {
                    string emotion = match.Groups[2].Value.Trim();
                    string content = match.Groups[3].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var inner = Regex.Split(content, @"(\[Pause[\+\-]?\])");
                        foreach (var part in inner)
                        {
                            var trimmed = part.Trim();
                            if (string.IsNullOrEmpty(trimmed)) continue;

                            if (trimmed.StartsWith("[Pause", System.StringComparison.OrdinalIgnoreCase))
                                segments.Add(new EmotionSegment { pauseTag = trimmed });
                            else
                                segments.Add(new EmotionSegment { text = trimmed, emotion = emotion });
                        }
                    }
                }
                else if (match.Groups[4].Success)
                {
                    string pauseTag = match.Groups[4].Value.Trim();
                    segments.Add(new EmotionSegment { pauseTag = pauseTag });
                }

                pos = matchIndex + match.Length;
            }

            if (pos < input.Length)
            {
                string last = input.Substring(pos).Trim();
                if (!string.IsNullOrWhiteSpace(last))
                    segments.Add(new EmotionSegment { text = last });
            }

            return segments;
        }
    }
}
