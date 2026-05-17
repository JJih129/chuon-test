// IParryReact.cs
using UnityEngine;

public interface IParryReact
{
    void OnParried(GameObject parrier, float riposteDamage, float stunDuration);
}

public interface IPerfectDodgeReact
{
    void OnPerfectDodged(GameObject dodger);
}
