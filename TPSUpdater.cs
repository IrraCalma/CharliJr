using UnityEngine;
using TMPro;

public class TPSUpdater : MonoBehaviour
{
    public TMP_Text tpsText;

    void Start()
    {
        // Use the assigned TextMeshProUGUI reference from the inspector
        if (tpsText == null)
        {
            Debug.LogError("TPSUpdater: No TextMeshProUGUI assigned in the inspector!");
        }
    }

    void Update()
    {
        if (tpsText != null)
        {
            // Update the text with the current tokens per second
            tpsText.text = Mathf.RoundToInt(LLMCharacterStats.CurrentTokensPerSecond).ToString();
        }
    }
}