using UnityEngine;
using TMPro;

public class TPSUpdater : MonoBehaviour
{
    private TextMeshProUGUI tpsText;

    void Start()
    {
        // Get the TextMeshPro component from the burbuja where this script is attached
        tpsText = GetComponent<TextMeshProUGUI>();
        
        if (tpsText == null)
        {
            Debug.LogError("TPSUpdater: No TextMeshProUGUI component found on this object!");
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