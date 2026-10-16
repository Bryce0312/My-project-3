using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingRoom30.Review
{
    /// <summary>Validated floor corridors for this authored living room, independent of NavMesh.</summary>
    public sealed class ReviewRouteGraph
    {
        public readonly Vector3[] Nodes =
        {
            new Vector3(-2.70f, 0f, -2.42f), // entrance spawn
            new Vector3(-2.45f, 0f, -1.55f), // overview
            new Vector3(-3.10f, 0f, -1.35f), // west corner
            new Vector3(-3.10f, 0f, 1.25f),  // west passage
            new Vector3(-0.95f, 0f, 1.30f),  // cross-room / side TV view
            new Vector3(-1.05f, 0f, 0.00f),  // coffee table / sofa side
            new Vector3(-2.85f, 0f, 1.35f),  // display cabinet
            new Vector3(1.65f, 0f, 1.40f),   // bookcase / east circulation
            new Vector3(2.45f, 0f, 1.15f),   // optional east corner
            new Vector3(-0.95f, 0f, -0.06f), // seat entry / safe standing exit
        };

        private readonly int[,] edges =
        {
            {0,1}, {1,2}, {2,3}, {3,4}, {3,6}, {4,6},
            {4,5}, {4,7}, {7,8}, {5,9}
        };

        public bool TryRoute(Vector3 observer, int destination,
            Func<Vector3, Vector3, bool> clear, List<Vector3> result)
        {
            result.Clear();
            if (destination < 0 || destination >= Nodes.Length) return false;
            observer.y = 0f;
            int count = Nodes.Length;
            var distance = new float[count];
            var previous = new int[count];
            var visited = new bool[count];
            for (int i = 0; i < count; i++)
            {
                // Rejoin only a corridor node with a clear body-sized path. Never draw a
                // shortcut from a wandering user through the sofa, chair or coffee table.
                distance[i] = clear(observer, Nodes[i])
                    ? Vector3.Distance(observer, Nodes[i]) : float.PositiveInfinity;
                previous[i] = -1;
            }
            for (int round = 0; round < count; round++)
            {
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                    if (!visited[i] && distance[i] < best) { u = i; best = distance[i]; }
                if (u < 0) break;
                visited[u] = true;
                if (u == destination) break;
                for (int edge = 0; edge < edges.GetLength(0); edge++)
                {
                    int v = edges[edge, 0] == u ? edges[edge, 1]
                        : edges[edge, 1] == u ? edges[edge, 0] : -1;
                    if (v < 0 || visited[v] || !clear(Nodes[u], Nodes[v])) continue;
                    float next = distance[u] + Vector3.Distance(Nodes[u], Nodes[v]);
                    if (next < distance[v]) { distance[v] = next; previous[v] = u; }
                }
            }
            if (float.IsPositiveInfinity(distance[destination])) return false;
            int cursor = destination;
            for (int guard = 0; cursor >= 0 && guard < count; guard++)
            {
                result.Add(Nodes[cursor]);
                cursor = previous[cursor];
            }
            result.Reverse();
            result.Insert(0, observer);
            return true;
        }
    }
}
