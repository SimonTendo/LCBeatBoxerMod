using UnityEngine;

[CreateAssetMenu(menuName = "LCBeatBoxerMod/DataContainers/AttackData")]
public class AttackData : ScriptableObject
{
    [Header("General")]
    public AttackAnimationNames animationName;
    [Space(3f)]
    [Header("Parameters")]
    [Tooltip("Determines if the enemy will cancel hits it receives itself.")]
    public bool canBlock = true;
    [Tooltip("Determines if the enemy will be stunned if hit while vulnerable.")]
    public bool canStun = true;
    public float stunTime = 1.5f;
    [Space(3f)]
    [Header("Audiovisual")]
    public AudioClip creatureSFX;
    public AudioClip audienceSFX;
}
