using TMPro;
using UnityEngine;

public class DiceResultIconUI : MonoBehaviour
{
    [SerializeField] private TMP_Text valueText;

    public void SetUnknown()
    {
        if (valueText != null)
            valueText.text = "?";
    }

    public void SetValue(int value)
    {
        if (valueText != null)
            valueText.text = value.ToString();
    }
}