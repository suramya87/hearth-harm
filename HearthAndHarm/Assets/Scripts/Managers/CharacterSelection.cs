using UnityEngine;
/// <summary>Persists character choice between scenes.</summary>
public static class CharacterSelection
{
    public static int        Index  { get; set; } = 0;
    public static GameObject Prefab { get; set; } = null;
}