using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DicePhysicsRoller : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DicePhysicsDie d6Prefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform diceParent;

    [Header("Roll Force")]
    [SerializeField] private float upwardForce = 4f;
    [SerializeField] private float sideForce = 3f;
    [SerializeField] private float torqueForce = 12f;

    [Header("Settling")]
    [SerializeField] private float minimumRollTime = 0.75f;
    [SerializeField] private float settledConfirmTime = 0.35f;
    [SerializeField] private float maxRollTime = 6f;

    private readonly List<DicePhysicsDie> activeDice = new();

    [Header("Stage Visibility")]
    [SerializeField] private Camera diceCamera;

    public void SetStageVisible(bool visible)
    {
        if (diceCamera != null)
            diceCamera.enabled = visible;

        if (!visible)
            ClearDice();
    }

    public IEnumerator RollD6(int diceCount, System.Action<List<int>> onComplete)
    {
        ClearDice();

        for (int i = 0; i < diceCount; i++)
        {
            Vector3 spawnOffset = new Vector3(
                Random.Range(-0.75f, 0.75f),
                0f,
                Random.Range(-0.75f, 0.75f)
            );

            DicePhysicsDie die = Instantiate(
                d6Prefab,
                spawnPoint.position + spawnOffset,
                Random.rotation,
                diceParent
            );

            activeDice.Add(die);

            Vector3 force = new Vector3(
                Random.Range(-sideForce, sideForce),
                upwardForce,
                Random.Range(-sideForce, sideForce)
            );

            Vector3 torque = Random.onUnitSphere * torqueForce;

            die.Roll(force, torque);
        }

        yield return new WaitForSeconds(minimumRollTime);

        float timer = 0f;
        float settledTimer = 0f;

        while (timer < maxRollTime)
        {
            timer += Time.deltaTime;

            if (AllDiceSettled())
            {
                settledTimer += Time.deltaTime;

                if (settledTimer >= settledConfirmTime)
                    break;
            }
            else
            {
                settledTimer = 0f;
            }

            yield return null;
        }

        List<int> results = new();

        foreach (DicePhysicsDie die in activeDice)
        {
            if (die != null)
                results.Add(die.GetTopValue());
        }

        onComplete?.Invoke(results);
    }

    public void ClearDice()
    {
        foreach (DicePhysicsDie die in activeDice)
        {
            if (die != null)
                Destroy(die.gameObject);
        }

        activeDice.Clear();
    }

    private bool AllDiceSettled()
    {
        foreach (DicePhysicsDie die in activeDice)
        {
            if (die != null && !die.IsSettled)
                return false;
        }

        return true;
    }
}