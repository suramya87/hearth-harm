using System.Collections.Generic;
using UnityEngine;
public enum DieType { D4=4, D6=6, D8=8, D10=10, D12=12, D20=20 }
public static class DiceRoller
{
    public static int Roll(DieType d) => Random.Range(1, (int)d + 1);
    public static List<int> RollMultiple(DieType d, int n)
    { var r = new List<int>(); for (int i=0;i<n;i++) r.Add(Roll(d)); return r; }
}