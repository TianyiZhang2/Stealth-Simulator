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
    private float lookAheadStepSize = 0.5f;
    // Metric for times Wael's algorithm is called
    private bool rescuing = false;
    private float rescuedTime = 0f;
    private int rescueCalls = 0;
    // Metric for times we enter an alcove
    private bool inAlcove = false;
    private int alcoveCount = 0;
 
    private float prevTime;

    // Throw away experiment if guards stand still for too long
    private float prevGuardTime;
    private Vector2 prevGuardPos;
    private bool cancelExperiment;

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

        prevGuardPos = m_guards[0].transform.position;
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
        inAlcove = false;
        alcoveCount = 0;

        prevGuardPos = m_guards[0].transform.position;
        cancelExperiment = false;
    }
    
    // Run the state the intruder is in
    public void ExecuteState()
    {
        if (GetNpcData().intruderPlanner == IntruderPlanner.UserInput)
            MoveByInput();
        else
        {
            m_state.UpdateState();
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
        // Check if intruder is in alcove
        Vector2 topAlcove = new Vector2(-15, 3);
        Vector2 bottomAlcove = new Vector2(-11, -5);
        if (!inAlcove && (Vector2.Distance(transform.position, topAlcove) < 5 || Vector2.Distance(transform.position, bottomAlcove) < 3))
        {
            inAlcove = true;
            alcoveCount++;
        }
        else if (inAlcove && Vector2.Distance(transform.position, topAlcove) >= 5 && Vector2.Distance(transform.position, bottomAlcove) >= 3)
        {
            inAlcove = false;
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
        if(cancelExperiment)
        {
            alcoveCount = -1;
        }
        return new LogSnapshot(GetTravelledDistance(), StealthArea.episodeTime, Data, m_state.GetState().ToString(),m_NoTimesSpotted,
            m_AlertTime, m_SearchedTime, 0, 0f, rescuedTime, rescueCalls, alcoveCount);
    }

    private void ExecuteHidingStrategy()
    {
        switch (GetNpcData().intruderPlanner)
        {
            case IntruderPlanner.LookAhead5: LookAheadHybrid(5); break;
            case IntruderPlanner.LookAhead10: LookAheadHybrid(10); break;
            case IntruderPlanner.DeadReckoning3: LookAheadHybrid(3, deadReckoning: true); break;
            case IntruderPlanner.DeadReckoning5: LookAheadHybrid(5, deadReckoning: true); break;
            case IntruderPlanner.DeadReckoning7: LookAheadHybrid(7, deadReckoning: true); break;
            case IntruderPlanner.DeadReckoning10: LookAheadHybrid(10, deadReckoning: true); break;
            case IntruderPlanner.RescueWhenSeen5: LookAheadHybrid(5, rescueWhenSeen: true, considerUnseenToSeen: false); break;
            case IntruderPlanner.RescueWhenSeen10: LookAheadHybrid(10, rescueWhenSeen: true, considerUnseenToSeen: false); break;
            case IntruderPlanner.MaxPath5: LookAheadHybrid(5, considerUnseenToSeen: false); break;
            case IntruderPlanner.MaxPath10: LookAheadHybrid(10, considerUnseenToSeen: false); break;
            default: SetGoal(m_HidingSpots.GetRandomHidingSpot(), false); break;
        }
    }

    // deadReckoning: consider only future observer positions within observer's FoV
    // rescueWhenSeen: call Wael's algorithm whenever you are seen
    // considerUnseenToSeen: Punish paths that take you from unseen to seen
    private void LookAheadHybrid(int lookAheadDepth, bool deadReckoning = false, bool rescueWhenSeen = false, bool considerUnseenToSeen = true)
    {
        // Recalculate path every 0.25s
        if (Time.time - prevTime >= 0.25f)
        {
            // Heuristic: % of observers that can see the point
            Dictionary<Vector2, float> moves = new Dictionary<Vector2, float>();
            List<Vector2> positions = new List<Vector2>();
            // For each observer position, keep track of distance from observer
            Dictionary<Vector2, int> observerPos = new Dictionary<Vector2, int>();
            positions.Add(transform.position);
            foreach (Guard guard in m_guards)
            {
                observerPos[guard.transform.position] = 1;
                //observerPos.Add(guard.transform.position);
            }
            // Iterate over lookahead depth
            for (int i = 0; i < lookAheadDepth; i++)
            {
                List<Vector2> newMove = new List<Vector2>();
                Dictionary<Vector2, int> newObs = new Dictionary<Vector2, int>();
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
                    foreach (Vector2 pos in observerPos.Keys)
                    {
                        Vector2 newPos = pos + direction * lookAheadStepSize;
                        if (!observerPos.ContainsKey(newPos) && !newObs.ContainsKey(newPos) && !IsObstructed(newPos))
                        {
                            // If using dead reckoning, search only points within a guard's visibility polygon
                            if (deadReckoning)
                            {
                                bool inPolygon = false;
                                foreach (Guard guard in m_guards)
                                {
                                    if (guard.GetFoV().IsPointInPolygon(newPos, true))
                                    {
                                        inPolygon = true;
                                        break;
                                    }
                                }
                                if (inPolygon)
                                {
                                    // newPos distance from observer = 1 + pos distance
                                    newObs[newPos] = observerPos[pos] + 1;
                                    //newObs.Add(newPos);
                                }
                            }
                            // Otherwise, search all possible observer positions
                            else
                            {
                                newObs[newPos] = observerPos[pos] + 1;
                                //newObs.Add(newPos);
                            }
                        }
                    }
                }
                positions.AddRange(newMove);
                foreach(Vector2 pos in newObs.Keys)
                {
                    observerPos[pos] = newObs[pos];
                }
                //observerPos.AddRange(newObs);
            }
            // Calculate visibility for each point in look ahead range
            foreach (Vector2 pos in positions)
            {
                float visibility = GetVisibilityHeuristic(pos, observerPos);
                moves[pos] = visibility;
            }
            // For the 10 best points, calculate the visibility of the path to that point
            Dictionary<Vector2, float> bestMoves = GetBestMovesByPath(moves, observerPos, 10, considerUnseenToSeen);
            
            // Don't move if already perfectly hidden
            if (moves[transform.position] == 0)
            {
                SetGoal(transform.position, true);
                rescuing = false;
            }
            // If all nearby points are bad, use Wael's hiding spots algorithm
            //else if((rescueWhenSeen && m_state.GetState() is Chased) || (IsVisible(transform.position) && bestMoves.Values.Min() >= 1))
            else if ((rescueWhenSeen && m_state.GetState() is Chased) || (IsVisible(transform.position) && bestMoves.Values.Min() >= moves[transform.position]))
            {
                //Debug.Log("Rescuing Intruder");
                m_HidingSpots.AssignHidingSpotsFitness(m_guards, World.GetNavMesh());
                SetGoal(m_HidingSpots.GetBestHidingSpot().Value, false);
                rescuing = true;
                rescueCalls++;
            }
            // Pick the best move
            else
            {
                Vector2 bestMove = bestMoves.KeyByValue(bestMoves.Values.Min());
                SetGoal(bestMove, true);
                Debug.DrawLine(transform.position, bestMove, Color.white, 0.5f);
                //Debug.Log(bestMoves[bestMove]);
                rescuing = false;
            }
            prevTime = Time.time;
            // Check whether we need to cancel the experiment
            if(Time.time - prevGuardTime > 5)
            {
                if(prevGuardPos.Equals(m_guards[0].transform.position))
                {
                    cancelExperiment = true;
                }
                prevGuardPos = m_guards[0].transform.position;
                prevGuardTime = Time.time;
            }
        }
    }

    // Calculate visibility heuristic for DeepLookAhead algorithm
    float GetVisibilityHeuristic(Vector2 pos, Dictionary<Vector2, int> observerPos)
    {
        RaycastHit2D hit;
        float visibility = 0;
        foreach (Vector2 observer in observerPos.Keys)
        {
            hit = Physics2D.Linecast(pos, observer, LayerMask.GetMask("Wall"));
            if (hit.collider == null)
            {
                visibility += 1.0f / observerPos[observer];
            }
        }
        // Any visible point is worse than any non-visible point
        if (IsVisible(pos))
        {
            //return 1 + visibility / observerPos.Count;
            return 100;
        }
        //return visibility / observerPos.Count;
        return visibility;
    }

    // Returns the 'numMoves' best moves, with heuristic based on the visibility of points on the path
    // considerUnseenToSeen: Punish paths that take you from unseen to seen
    Dictionary<Vector2, float> GetBestMovesByPath(Dictionary<Vector2, float> moves, Dictionary<Vector2, int> observerPos, int numMoves, bool considerUnseenToSeen = true)
    {
        Dictionary<Vector2, float> bestMoves = new Dictionary<Vector2, float>();
        List<KeyValuePair<Vector2, float>> list = moves.ToList();
        // Sort moves by increasing visibility
        list.Sort((x, y) =>
        {
            int result = x.Value.CompareTo(y.Value);
            // If same value, order by distance from intruder
            if (x.Value == y.Value)
            {
                result = Vector2.Distance(transform.position, x.Key).CompareTo(Vector2.Distance(transform.position, y.Key));
            }
            return result;
        });
        // Calculate visibility of path to each point
        for (int i = 0; i < numMoves; i++)
        {
            if (i >= list.Count)
            {
                break;
            }
            Vector2 nextPos = list[i].Key;
            List<Vector2> path = GetPath(transform.position, nextPos);
            float max = 0;
            // Calculate heuristic for each point in path
            foreach(Vector2 point in path)
            {
                float pathHeuristic = GetVisibilityHeuristic(point, observerPos);
                if (pathHeuristic > max)
                {
                    max = pathHeuristic;
                }
            }
            // Calculate whether the path takes you from unseen -> seen
            // If it does, make it worse
            if(considerUnseenToSeen)
            {
                for(int j = 0; j < path.Count - 1; j++)
                {
                    if(!IsVisible(path[j]) && IsVisible(path[j + 1]))
                    {
                        max = 100;
                        break;
                    }
                }
            }
            bestMoves.Add(nextPos, max);
        }
        return bestMoves;
    }

    // Returns a path from start to end
    List<Vector2> GetPath(Vector2 start, Vector2 end)
    {
        List<Vector2> path = new List<Vector2>();
        PathFinding.GetShortestPath(World.GetNavMesh(), start, end, path);
        path.Insert(0, transform.position);
        List<Vector2> result = new List<Vector2>();
        for(int i = 0; i < path.Count - 1; i++)
        {
            result.Add(path[i]);
            // Add intermediate points
            for(int j = 1; j < 10; j++)
            {
                result.Add(Vector2.Lerp(path[i], path[i + 1], j * 0.1f));
            }
        }
        result.Add(path[path.Count - 1]);
        return result;
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
        bool visible = false;
        foreach(Guard guard in m_guards)
        {
            if(guard.GetFoV().IsPointInPolygon(pos, true))
            {
                visible = true;
                break;
            }
        }
        return visible;
        /*
        RaycastHit2D hit;
        foreach (Guard guard in m_guards)
        {
            hit = Physics2D.Linecast(pos, guard.transform.position, LayerMask.GetMask("Wall"));
            if (hit.collider == null)
            {
                return true;
            }
        }
        return false;*/
    }
}