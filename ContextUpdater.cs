using UnityEngine;
using TMPro;

public class ContextUpdater : MonoBehaviour
{
    public TextMeshProUGUI contextText;

    void Start()
    {
        // Use the assigned TextMeshProUGUI reference from the inspector
        if (contextText == null)
        {
            Debug.LogError("ContextUpdater: No TextMeshProUGUI assigned in the inspector!");
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