using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized manager for all living SwarmAgent instances. Provides
/// neighbour queries, group state, and acts as the single registry
/// so individual drones never need FindObjectsOfType or OverlapSphere.
///
/// Architecture:
///   - Singleton MonoBehaviour. Auto-created by SwarmAgent.OnInit() if
///     no instance exists in the scene.
///   - Drones register in OnInit() and unregister in OnEnemyDeath() /
///     OnCleanup().
///   - Neighbour queries use sqrMagnitude and a reusable List buffer
///     to avoid per-frame GC allocations.
///
/// Performance Note:
///   Uses a linear scan for neighbour queries. With the expected 20-30
///   drones, O(n) per query is more than fast enough and far simpler
///   than spatial partitioning (k-d tree, grid).
///
/// Usage:
///   SwarmFormation.Instance.Register(this);
///   SwarmFormation.Instance.Unregister(this);
///   List&lt;SwarmAgent&gt; neighbours = SwarmFormation.Instance.GetNeighbours(this, radius);
/// </summary>
public class SwarmFormation : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Singleton
    // ──────────────────────────────────────────────

    private static SwarmFormation instance;

    /// <summary>
    /// The singleton instance. If none exists, creates a new GameObject
    /// with this component attached. This means no manual scene setup
    /// is required — the first SwarmAgent to initialise will create it.
    /// </summary>
    public static SwarmFormation Instance
    {
        get
        {
            if (instance == null)
            {
                // Check if one already exists in the scene (e.g., placed manually).
                instance = FindAnyObjectByType<SwarmFormation>();

                if (instance == null)
                {
                    GameObject go = new GameObject("[SwarmFormation]");
                    instance = go.AddComponent<SwarmFormation>();
                    Debug.Log("[SwarmFormation] Auto-created singleton instance.");
                }
            }
            return instance;
        }
    }

    /// <summary>
    /// True if a SwarmFormation singleton currently exists.
    /// Use this before accessing Instance during teardown paths (e.g.,
    /// OnEnemyDeath, OnCleanup) to avoid auto-creating a new GameObject
    /// while the scene is being destroyed.
    /// </summary>
    public static bool HasInstance => instance != null;

    // ──────────────────────────────────────────────
    //  Registry
    // ──────────────────────────────────────────────

    /// <summary>All currently living and registered SwarmAgents.</summary>
    private readonly List<SwarmAgent> agents = new List<SwarmAgent>();

    /// <summary>
    /// Reusable buffer for neighbour query results. Returned by
    /// GetNeighbours() — callers must consume it before the next call.
    /// This avoids allocating a new List every frame for every drone.
    /// </summary>
    private readonly List<SwarmAgent> neighbourBuffer = new List<SwarmAgent>();

    /// <summary>How many Swarm drones are currently alive and registered.</summary>
    public int ActiveCount => agents.Count;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // Enforce singleton — destroy duplicates.
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[SwarmFormation] Duplicate instance destroyed.", this);
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    // ──────────────────────────────────────────────
    //  Registration
    // ──────────────────────────────────────────────

    /// <summary>
    /// Registers a SwarmAgent with the formation. Called from
    /// SwarmAgent.OnInit(). Duplicate registrations are ignored.
    /// </summary>
    /// <param name="agent">The drone to register.</param>
    public void Register(SwarmAgent agent)
    {
        if (agent == null) return;

        if (!agents.Contains(agent))
        {
            agents.Add(agent);
            Debug.Log($"[SwarmFormation] Registered '{agent.gameObject.name}'. " +
                      $"Active count: {agents.Count}");
        }
    }

    /// <summary>
    /// Unregisters a SwarmAgent from the formation. Called from
    /// SwarmAgent.OnEnemyDeath() and OnCleanup(). Safe to call
    /// multiple times — silently ignores agents not in the list.
    /// </summary>
    /// <param name="agent">The drone to unregister.</param>
    public void Unregister(SwarmAgent agent)
    {
        if (agent == null) return;

        if (agents.Remove(agent))
        {
            Debug.Log($"[SwarmFormation] Unregistered '{agent.gameObject.name}'. " +
                      $"Active count: {agents.Count}");
        }
    }

    // ──────────────────────────────────────────────
    //  Queries
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns all registered SwarmAgents within the given radius of
    /// the requester. Uses sqrMagnitude to avoid per-check sqrt.
    ///
    /// IMPORTANT: The returned list is a shared buffer. Callers must
    /// consume its contents immediately — the next call to
    /// GetNeighbours() will clear and refill the same list.
    /// </summary>
    /// <param name="requester">The drone asking for neighbours.</param>
    /// <param name="radius">Search radius in world units.</param>
    /// <returns>A shared list of neighbours (do not cache this reference).</returns>
    public List<SwarmAgent> GetNeighbours(SwarmAgent requester, float radius)
    {
        neighbourBuffer.Clear();

        float radiusSqr = radius * radius;
        Vector3 origin = requester.transform.position;

        for (int i = 0; i < agents.Count; i++)
        {
            SwarmAgent other = agents[i];

            // Skip self and dead drones.
            if (other == requester || other == null || other.IsDead)
                continue;

            float distSqr = (other.transform.position - origin).sqrMagnitude;
            if (distSqr <= radiusSqr)
            {
                neighbourBuffer.Add(other);
            }
        }

        return neighbourBuffer;
    }

    /// <summary>
    /// Returns the average world-space position of all living registered
    /// drones. Useful for cohesion fallback and level-design triggers.
    /// Returns Vector3.zero if no drones are registered.
    /// </summary>
    public Vector3 GetSwarmCentroid()
    {
        if (agents.Count == 0) return Vector3.zero;

        Vector3 sum = Vector3.zero;
        int validCount = 0;

        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i] != null && !agents[i].IsDead)
            {
                sum += agents[i].transform.position;
                validCount++;
            }
        }

        return validCount > 0 ? sum / validCount : Vector3.zero;
    }
}
