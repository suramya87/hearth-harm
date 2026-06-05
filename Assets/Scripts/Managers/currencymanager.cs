using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class CurrencyManager : MonoBehaviour
{
    public static CurrencyManager Instance { get; private set; }

    public event Action<int> OnCoinsChanged;

    [Header("UI")]
    [SerializeField] private TMP_Text coinsText;

    [Header("Runtime")]
    [SerializeField] private int currentCoins;

    [Header("UI Animation")]
    [SerializeField] private float countDuration = 0.5f;

    private Coroutine countRoutine;
    private int displayedCoins;

    public int CurrentCoins => currentCoins;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        displayedCoins = currentCoins;
        RefreshUIImmediate();
    }

    public void AddCoins(int amount)
    {
        if (amount <= 0) return;

        currentCoins += amount;

        AnimateCoinsTo(currentCoins);

        OnCoinsChanged?.Invoke(currentCoins);

        Debug.Log($"[CurrencyManager] Added {amount} coins. Total: {currentCoins}");
    }

    public bool SpendCoins(int amount)
    {
        if (amount <= 0) return true;
        if (currentCoins < amount) return false;

        currentCoins -= amount;

        AnimateCoinsTo(currentCoins);

        OnCoinsChanged?.Invoke(currentCoins);

        Debug.Log($"[CurrencyManager] Spent {amount} coins. Total: {currentCoins}");

        return true;
    }

    public void SetCoins(int amount)
    {
        currentCoins = Mathf.Max(0, amount);

        AnimateCoinsTo(currentCoins);

        OnCoinsChanged?.Invoke(currentCoins);
    }

    private void AnimateCoinsTo(int target)
    {
        if (countRoutine != null)
            StopCoroutine(countRoutine);

        countRoutine = StartCoroutine(CountCoinsRoutine(displayedCoins, target));
    }

    private IEnumerator CountCoinsRoutine(int from, int to)
    {
        float timer = 0f;

        while (timer < countDuration)
        {
            timer += Time.deltaTime;

            float t = Mathf.Clamp01(timer / countDuration);
            int shownValue = Mathf.RoundToInt(Mathf.Lerp(from, to, t));

            SetDisplayedCoins(shownValue);

            yield return null;
        }

        SetDisplayedCoins(to);
        countRoutine = null;
    }

    private void SetDisplayedCoins(int value)
    {
        displayedCoins = value;

        if (coinsText != null)
            coinsText.text = displayedCoins.ToString();
    }

    private void RefreshUIImmediate()
    {
        SetDisplayedCoins(currentCoins);
    }
}