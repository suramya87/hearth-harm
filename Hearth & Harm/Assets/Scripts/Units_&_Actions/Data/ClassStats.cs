using System;
[Serializable]
public class ClassStats
{
    public PlayerClass playerClass;
    public int         maxHealth;
    public int         maxStamina;
}
public enum PlayerClass { Knight, Rogue, Mage, Cleric }