using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Intruder : NPC
{
    // Hiding spots the intruder can go to
    private HidingSpots m_HidingSpots;

    // Intruder state 
    private StateMachine m_state;

    // The place the intruder was last seen in 
    private Vector2? m_lastKnownLocation;

    // The Current FoV
    private List<Polygon> m_FovPolygon;

    private int m_NoTimesSpotted;
    
    // Total time being chased and visible
    private float m_AlertTime;

    // Total time being chased and invisible 
    private float m_SearchedTime;

    // List of guards; to assess the fitness of the hiding spots
    private List<Guard> m_guards;

    // 8 cardinal directions
    private static List<Vector2> directions = new List<Vector2>(
        new Vector2[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right,
        new Vector2(1,1), new Vector2(1,-1), new Vector2(-1,1), new Vector2(-1,-1) });

    // Settings for lookahead algorithms
    //private int lookAheadDepth = 10;
    private float lookAheadStepSize = 0.5f;
    //private int lookAheadSteps = 0;
    //private int totalSteps = 0;
    private bool rescuing = false;
    private float rescuedTime = 0f;
    private int rescueCalls = 0;
 
    private float prevTime;

    public override void Initiate()
    {
        base.Initiate();

        m_HidingSpots = transform.parent.parent.Find("Map").GetComponent<HidingSpots>();
        m_guards = transform.parent.parent.Find("NpcManager").GetComponent<GuardsManager>().GetGuards();
        AddFoV();

        // The intruder's field of view to detect guards
        m_FovPolygon = new List<Polygon>() {new Polygon()};

        // Start the state as incognito
        m_state = new StateMachine();
        m_state.ChangeState(new Incognito(this));

        // Multiply the intruder's speed
        NpcSpeed *= 1.5f;
        NpcRotationSpeed *= 2f;
    }
    
    public override void ResetNpc()
    {
        base.ResetNpc();

        m_NoTimesSpotted = 0;
        m_AlertTime = 0f;
        m_SearchedTime = 0f;

        rescuing = false;
        rescuedTime = 0f;
        rescueCalls = 0;
    }
    
    // Run the state the intruder is in
    public void ExecuteState()
    {
        if (GetNpcData().intruderPlanner == IntruderPlanner.UserInput)
            MoveByInput();
        else
        {
            m_state.Update();
        }
    }

    public override void UpdateMetrics(float timeDelta)
    {
        base.UpdateMetrics(timeDelta);
        if (m_state.GetState() is Chased)
        {
            m_AlertTime += timeDelta;
        }
        else if (m_state.GetState() is Hide)
        {
            m_SearchedTime += timeDelta;
        }
        if (rescuing)
        {
            rescuedTime += timeDelta;
        }
    }

    private void LateUpdate()
    {
        CastVision();
    }

    // Cast the guard field of view
    public void CastVision()
    {
        Fov.CastFieldOfView();
        LoadFovPolygon();
    }

    private void LoadFovPolygon()
    {
        m_FovPolygon[0].Clear();
        foreach (var vertex in Fov.GetFovVertices())
            m_FovPolygon[0].AddPoint(vertex);
    }

    public Vector2 GetLastKnownLocation()
    {
        return m_lastKnownLocation.Value;
    }

    // Create and add the Field of View
    public void AddFoV()
    {
        // The game object that contains the field of view
        GameObject fovGameObject = new GameObject("FoV");

        // Assign it as a child to the guard
        var transform1 = transform;
        fovGameObject.transform.parent = transform1;
        fovGameObject.transform.position = transform1.position;

        Fov = fovGameObject.AddComponent<FieldOfView>();
        Fov.Initiate(361f, 50f, new Color32(200, 200, 200, 150));
    }

    // Render the guard and the FoV if seen by the intruder
    public void RenderIntruder(bool isSeen)
    {
        Renderer.enabled = isSeen;
        FovRenderer.enabled = isSeen;
    }

    // Intruder is seen so update the known location of the intruder 
    public void Seen()
    {
        m_lastKnownLocation = transform.position;
    }
    
    // Rendering 
    public void SpotGuards(List<Guard> guards)
    {
        foreach (var guard in guards)
        {
            if (Area.gameView == GameView.Spectator)
            {
                guard.RenderGuard(true);
                RenderIntruder(true);
            }
            else if (Area.gameView == GameView.Intruder)
            {
                RenderIntruder(true);

                if (m_FovPolygon[0].IsCircleInPolygon(guard.transform.position, 0.5f))
                    guard.RenderGuard(true);
                else
                    guard.RenderGuard(false);
            }
        }
    }
    
    // Incognito behavior
    public void Incognito()
    {
        if (GetNpcData().intruderPlanner == IntruderPlanner.Random)
            SetGoal(m_HidingSpots.GetRandomHidingSpot(), false);
        else if (GetNpcData().intruderPlanner == IntruderPlanner.Heuristic)
        {
            m_HidingSpots.AssignHidingSpotsFitness(m_guards, World.GetNavMesh());
            SetGoal(m_HidingSpots.GetBestHidingSpot().Value, false);
        }
        else
        {
            ExecuteHidingStrategy();
        }
    }

    // Called when the intruder is spotted
    public void StartRunningAway()
    {
        m_state.ChangeState(new Chased(this));
        m_NoTimesSpotted++;
    }

    // Intruder behavior when being chased
    public void RunAway()
    {
        if (GetNpcData().intruderPlanner == IntruderPlanner.Random)
            SetGoal(m_HidingSpots.GetRandomHidingSpot(), false);
        else if (GetNpcData().intruderPlanner == IntruderPlanner.Heuristic)
        {
            if (IsIdle())
            {
                m_HidingSpots.AssignHidingSpotsFitness(m_guards, World.GetNavMesh());
                SetGoal(m_HidingSpots.GetBestHidingSpot().Value, false);
            }
        }
        else
        {
            ExecuteHidingStrategy();
        }
    }

    // To start hiding from guards searching for the intruder
    public void StartHiding()
    {
        m_state.ChangeState(new Hide(this));

        // Find a place to hide
        if (GetNpcData().intruderPlanner == IntruderPlanner.Random)
            SetGoal(m_HidingSpots.GetRandomHidingSpot(), false);
        else if (GetNpcData().intruderPlanner == IntruderPlanner.Heuristic)
        {
            if (IsIdle())
            {
                m_HidingSpots.AssignHidingSpotsFitness(m_guards, World.GetNavMesh());
                SetGoal(m_HidingSpots.GetBestHidingSpot().Value, false);
            }
        }
        else
        {
            ExecuteHidingStrategy();
        }
    }

    public float GetPercentAlertTime()
    {
        return m_AlertTime / Properties.EpisodeLength;
    }

    public int GetNumberOfTimesSpotted()
    {
        return m_NoTimesSpotted;
    }


    public IState GetState()
    {
        return m_state.GetState();
    }


    // Intruder behavior after escaping guards
    public void Hide()
    {
        if (GetNpcData().intruderPlanner == IntruderPlanner.Random)
        {

        }
        else if (GetNpcData().intruderPlanner == IntruderPlanner.Heuristic)
        {

        }
        else
        {
            ExecuteHidingStrategy();
        }
    }

    public override LogSnapshot LogNpcProgress()
    {
        /*float percentLookAhead = 0;
        if(GetNpcData().intruderPlanner == IntruderPlanner.LookAheadHybrid5 || GetNpcData().intruderPlanner == IntruderPlanner.LookAheadHybrid10)
        {
            percentLookAhead = (float)lookAheadSteps / totalSteps;
        }*/
        return new LogSnapshot(GetTravelledDistance(), StealthArea.episodeTime, Data, m_state.GetState().ToString(),m_NoTimesSpotted,
            m_AlertTime, m_SearchedTime, 0, 0f, rescuedTime, rescueCalls);
    }

    private void ExecuteHidingStrategy()
    {
        switch (GetNpcData().intruderPlanner)
        {
            case IntruderPlanner.LookAheadHybrid5: LookAheadHybrid(5); break;
            case IntruderPlanner.LookAheadHybrid10: LookAheadHybrid(10); break;
            default: SetGoal(m_HidingSpots.GetRandomHidingSpot(), false); break;
        }
    }

    private void LookAheadHybrid(int lookAheadDepth)
    {
        // Recalculate path every 0.25s
        if (Time.time - prevTime >= 0.25f)
        {
            // Heuristic: % of observers that can see the point
            Dictionary<Vector2, float> moves = new Dictionary<Vector2, float>();
            List<Vector2> positions = new List<Vector2>();
            List<Vector2> observerPos = new List<Vector2>();
            positions.Add(transform.position);
            foreach (Guard guard in m_guards)
            {
                observerPos.Add(guard.transform.position);
            }

            // Iterate over lookahead depth
            for (int i = 0; i < lookAheadDepth; i++)
            {
                List<Vector2> newMove = new List<Vector2>();
                List<Vector2> newObs = new List<Vector2>();
                foreach (Vector2 direction in directions)
                {
                    // Expand over possible intruder movements
                    for (int p = 0; p < positions.Count; p++)
                    {
                        Vector2 newPos = positions[p] + direction * lookAheadStepSize;
                        if (!positions.Contains(newPos) && !newMove.Contains(newPos) && !IsObstructed(newPos)
                            && PathFinding.GetShortestPathDistance(World.GetNavMesh(), transform.position, newPos) < 10)
                        {
                            newMove.Add(newPos);
                        }
                    }
                    // Expand over possible observer movements
                    for (int p = 0; p < observerPos.Count; p++)
                    {
                        Vector2 newPos = observerPos[p] + direction * lookAheadStepSize;
                        if (!observerPos.Contains(newPos) && !newObs.Contains(newPos) && !IsObstructed(newPos))
                        {
                            newObs.Add(newPos);
                        }
                    }
                }
                positions.AddRange(newMove);
                observerPos.AddRange(newObs);
            }
            // Calculate visibility for each point in look ahead range
            foreach (Vector2 pos in positions)
            {
                float visibility = GetVisibilityHeuristic(pos, observerPos);
                moves[pos] = visibility;
            }
            // For the 10 best points, calculate the visibility of the path to that point
            Dictionary<Vector2, float> bestMoves = new Dictionary<Vector2, float>();
            List<KeyValuePair<Vector2, float>> list = moves.ToList();
            // Sort moves by increasing visibility
            list.Sort((x, y) => { 
                int result = x.Value.CompareTo(y.Value);
                // If same value, order by distance from intruder
                if (x.Value == y.Value)
                {
                    result = Vector2.Distance(transform.position, x.Key).CompareTo(Vector2.Distance(transform.position, y.Key));
                }
                return result;
            });
            for (int i = 0; i < 10; i++)
            {
                if(i >= list.Count)
                {
                    break;
                }
                Vector2 nextPos = list[i].Key;
                List<Vector2> path = new List<Vector2> { transform.position };
                path.AddRange(PathFinding.GetShortestPath(World.GetNavMesh(), transform.position, nextPos));
                float pathHeuristic = 0;
                int numPoints = 0;
                //float max = 0;
                // Calculate heuristic for each point in path
                for (int j = 0; j < path.Count; j++)
                {
                    pathHeuristic += GetVisibilityHeuristic(path[j], observerPos);
                    numPoints++;
                    /*pathHeuristic = GetVisibilityHeuristic(path[j], observerPos);
                    if (pathHeuristic > max)
                        max = pathHeuristic;*/
                    // Also calculate visibility for intermediate points on path
                    if (j < path.Count - 1)
                    {
                        for (int k = 1; k < lookAheadStepSize; k++)
                        {
                            Vector2 intermediate = Vector2.Lerp(path[j], path[j + 1], k * 1.0f / lookAheadStepSize);
                            pathHeuristic += GetVisibilityHeuristic(intermediate, observerPos);
                            numPoints++;
                            /*pathHeuristic = GetVisibilityHeuristic(intermediate, observerPos);
                            if (pathHeuristic > max)
                                max = pathHeuristic;*/
                        }
                    }
                }
                // Calculate average heuristic
                pathHeuristic /= numPoints;
                bestMoves.Add(nextPos, pathHeuristic);
                //bestMoves.Add(nextPos, max);
            }
            // Don't move if already perfectly hidden
            if (moves[transform.position] == 0)
            {
                SetGoal(transform.position, true);
                rescuing = false;
            }
            // If all nearby points are bad, use Wael's hiding spots algorithm
            //else if (IsVisible(transform.position) && bestMoves.Values.Min() >= moves[transform.position])
            //else if (m_state.GetState() is Chased && bestMoves.Keys.All(x => IsVisible(x)))
            else if(IsVisible(transform.position) && bestMoves.Values.Min() >= 1)
            {
                //Debug.Log("Rescuing Intruder");
                m_HidingSpots.AssignHidingSpotsFitness(m_guards, World.GetNavMesh());
                SetGoal(m_HidingSpots.GetBestHidingSpot().Value, false);
                rescuing = true;
                rescueCalls++;
                //totalSteps++;
            }
            // Pick the best move
            else
            {
                Vector2 bestMove = bestMoves.KeyByValue(bestMoves.Values.Min());
                SetGoal(bestMove, true);
                Debug.DrawLine(transform.position, bestMove, Color.white, 0.5f);
                rescuing = false;
                //lookAheadSteps++;
                //totalSteps++;
            }
            prevTime = Time.time;
        }
    }

    // Calculate visibility heuristic for DeepLookAhead algorithm
    float GetVisibilityHeuristic(Vector2 pos, List<Vector2> observerPos)
    {
        RaycastHit2D hit;
        float visibility = 0;
        foreach (Vector2 observer in observerPos)
        {
            hit = Physics2D.Linecast(pos, observer, LayerMask.GetMask("Wall"));
            if (hit.collider == null)
            {
                visibility++;
            }
        }
        // Any visible point is worse than any non-visible point
        if (IsVisible(pos))
        {
            return 1 + visibility / observerPos.Count;
        }
        return visibility / observerPos.Count;
    }

    // Checks whether pos is blocked by a wall
    bool IsObstructed(Vector2 pos)
    {
        bool overlap = Physics2D.OverlapCircle(pos, 0.5f, LayerMask.GetMask("Wall"));
        return overlap;
    }

    // Checks if pos can be seen by guards
    bool IsVisible(Vector2 pos)
    {
        RaycastHit2D hit;
        foreach (Guard guard in m_guards)
        {
            hit = Physics2D.Linecast(pos, guard.transform.position, LayerMask.GetMask("Wall"));
            if (hit.collider == null)
            {
                return true;
            }
        }
        return false;
    }
}