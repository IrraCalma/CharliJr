using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace LLMUnity
{
    /// <summary>
    /// UI Updater script to display LLM statistics on UI elements.
    /// This script can be attached to a GameObject to update UI elements with global LLM stats.
    /// </summary>
    public class LLMStatsUIUpdater : MonoBehaviour
    {
        [Header("UI References")]
        public TextMeshProUGUI TPS_Txt; // Reference to the tokens per second text element in AI bubble
        public TextMeshProUGUI Context_Txt; // Reference to the context tokens text element in scene

        [Header("Update Settings")]
        public float updateInterval = 0.5f; // How often to update the UI in seconds

        private Coroutine updateCoroutine;

        void Start()
        {
            // Start updating the UI
            updateCoroutine = StartCoroutine(UpdateStatsUI());
        }

        void OnDestroy()
        {
            // Stop the coroutine when the object is destroyed
            if (updateCoroutine != null)
            {
                StopCoroutine(updateCoroutine);
            }
        }

        private IEnumerator UpdateStatsUI()
        {
            while (true)
            {
                // Update tokens per second display
                if (TPS_Txt != null)
                {
                    TPS_Txt.text = $"TPS: {LLMCharacterStats.CurrentTokensPerSecond:F2}";
                }

                // Update remaining context tokens display
                if (Context_Txt != null)
                {
                    Context_Txt.text = $"Context: {LLMCharacterStats.RemainingContextTokens} tokens remaining";
                }

                yield return new WaitForSeconds(updateInterval);
            }
        }

        /// <summary>
        /// Manual method to update all stats UI elements at once
        /// </summary>
        public void UpdateAllStats()
        {
            // Update tokens per second display
            if (TPS_Txt != null)
            {
                TPS_Txt.text = $"TPS: {LLMCharacterStats.CurrentTokensPerSecond:F2}";
            }

            // Update remaining context tokens display
            if (Context_Txt != null)
            {
                Context_Txt.text = $"Context: {LLMCharacterStats.RemainingContextTokens} tokens remaining";
            }
        }
    }
}