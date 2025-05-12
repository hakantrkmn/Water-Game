using System;
using System.Collections.Generic;
using Unity.Cinemachine;

public static class EventManager
{
    public static Action LevelGenerated;
    public static Action LevelCompleted;

    public static Func<Tile[]> GetAllTiles;

}