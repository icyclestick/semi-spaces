using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Causes a NavMeshAgent to visually hover and bob in the air while still
/// pathfinding correctly along the ground.
/// 
/// Setup:
/// 1. Attach to a GameObject with a NavMeshAgent.
/// 2. Set baseAltitude to determine how high it flies.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class HoverMotion : MonoBehaviour
{
    [Header("Hover Settings")]
    [SerializeField, Tooltip("The baseline height (in meters) the drone will fly above the ground.")]
    private float baseAltitude = 2.5f;

    [SerializeField, Tooltip("How far up and down it bobs from the base altitude.")]
    private float bobAmplitude = 0.4f;

    [SerializeField, Tooltip("How fast it bobs up and down.")]
    private float bobSpeed = 3.0f;

    private NavMeshAgent agent;
    private float timeOffset;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        
        // Randomize the starting phase of the sine wave so a swarm of drones 
        // don't all bob up and down in perfect, unnatural synchronization.
        timeOffset = Random.Range(0f, 100f);
    }

    private void Update()
    {
        // Safety check if the agent dies or gets disabled
        if (agent == null || !agent.enabled) return;

        // Calculate the sine wave bobbing effect
        float bobOffset = Mathf.Sin((Time.time + timeOffset) * bobSpeed) * bobAmplitude;

        // Apply it directly to the NavMeshAgent's internal offset.
        // This physically lifts the meshes and colliders without breaking pathfinding.
        agent.baseOffset = baseAltitude + bobOffset;
    }
}
