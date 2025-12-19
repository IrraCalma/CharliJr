using UnityEngine;
using TMPro;

public class ContextUpdater : MonoBehaviour
{
    private TextMeshProUGUI contextText;

    void Start()
    {
        // Get the TextMeshPro component for context display (Context_Txt)
        contextText = GetComponent<TextMeshProUGUI>();
        
        if (contextText == null)
        {
            Debug.LogError("ContextUpdater: No TextMeshProUGUI component found on this object!");
        }
    }

    void Update()
    {
        if (contextText != null)
        {
            // Update the text with the remaining context tokens
            contextText.text = LLMCharacterStats.RemainingContextTokens.ToString();
        }
    }
}