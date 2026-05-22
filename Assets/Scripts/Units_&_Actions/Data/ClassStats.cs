using System;
using UnityEngine;

[Serializable]
public class ClassStats
{
    public PlayerClass playerClass;

    [Header("Base Pools")]
    public int baseMaxHealth;
    public int baseMaxStamina;

    [Header("Core Stats")]
    public int strength = 10;
    public int constitution = 10;
    public int dexterity = 10;
    public int intelligence = 10;
    public int perception = 10;
    public int charisma = 10;
    public int luck = 10;
}

public enum PlayerClass
{
    Knight,
    Rogue,
    Mage,
    Cleric
}