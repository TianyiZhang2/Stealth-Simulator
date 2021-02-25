﻿using System;
using UnityEngine;

public static class Properties
{
    // Winding order for outer polygons; inner polygon is opposite.
    public static WindingOrder outerPolygonWinding = WindingOrder.CounterClockwise;
    public static WindingOrder innerPolygonWinding = WindingOrder.Clockwise;

    // Polygon Smoothing parameters
    public static float MinAngle = 10f;
    public static float MaxAngle = 170f;

    // The walkable area offset from the actual map
    public static float InterPolygonOffset = 0.3f;

    // The number of episodes to record
    public static int EpisodesCount = 10;

    // Staleness Properties
    // Staleness range
    // Colors for view point state
    public const byte StalenessHigh = 255;
    public const byte StalenessLow = 0;

    public static Color32 GetStalenessColor(float staleness)
    {
        float cappedStaleness = Mathf.Min(staleness, StalenessHigh);
        byte colorLevel = (byte) (StalenessHigh - cappedStaleness);
        return new Color32(colorLevel, colorLevel, colorLevel, 255);
    }

    // Staleness rate per second
    public const float StalenessRate = 16f;

    // Time required to cover one distance unit in seconds
    public static float TimeRequiredToCoverOneUnit = 3f;

    // Hiding Spots
    // Number of static hiding spots
    public static readonly int HidingSpotsCount = 50;

    //------------------------------------------------------------------------------------------------------
    // NPC Properties
    public const float SpeedMultiplyer = 1f;
    public const float NpcSpeed = 4f * SpeedMultiplyer;
    public const float NpcRotationSpeed = 200f * SpeedMultiplyer;

    // ************************************
    // Field of View Properties
    private const float viewRadiusFractionOfMap = 0.1f;
    
    // Set the default value for view radius for the Npcs
    public static void SetViewRadius(float maxWidth)
    {
        //ViewRadius = maxWidth * viewRadiusFractionOfMap;
    }

    public static float ViewRadius = 15f;
    public const float ViewAngle = 90f;


    //------------------------------------------------------------------------------------------------------
    // Grid parameters representation
    static readonly int GridMultiplier = 1;
    public static readonly int GridDefaultSizeX = 16 * GridMultiplier;
    public static readonly int GridDefaultSizeY = 10 * GridMultiplier;
    public static readonly float NodeRadius = 0.1f;


    // Search Parameters
    public static Color32 GetSegmentColor(float age)
    {
        if (age < 0f) age = 0f;
        byte colorLevel = (byte) Math.Round((age/MaxAge) * 255);
        //byte colorLevel = (byte) Math.Round(age * 255);
        // return new Color32(255, (byte) (255 - colorLevel), (byte) (255 - colorLevel), 255);
        return new Color32(255, 0, 0, colorLevel);
    }
    
    //----------------------------------------------------------

    // The duration of an episode.
    public static float EpisodeLength = 250f;

    // The agent difference threshold
    public static readonly int AgeThreshold = 10;

    // Max age a search segment can have
    public static float MaxAge = EpisodeLength * 0.75f;

    // The Max path distance of the map, for normalization purposes. 
    public static float MaxPathDistance;

    // Max length an edge can have
    public static readonly float MaxEdgeLength = 2f;
    
    // The Maximum number of guards available.
    public static int MaxGuardCount = 10;
    
    // The maximum walkable area; This acts as a denominator for normalizing the map size.
    public static float MaxWalkableArea = 60000f;

    // Rate of increase of the probability value of search segment
    public static readonly float ProbabilityIncreaseRate = 0.0005f * NpcSpeed;
    public static readonly float ProbagationMultiplier = 4f;
}