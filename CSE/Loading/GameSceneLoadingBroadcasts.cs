using FishNet.Broadcast;

public struct GameSceneLoadingBeginBroadcast : IBroadcast
{
    public string SceneName;
}

public struct GameSceneReadyBroadcast : IBroadcast
{
    public string SceneName;
}

public struct GameSceneLoadingReleaseBroadcast : IBroadcast
{
    public string SceneName;
    public bool TimedOut;
}
