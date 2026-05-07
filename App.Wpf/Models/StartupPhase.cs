namespace TitanAILivePC.Models;



/// <summary>Application startup orchestration — keeps first UI frame light.</summary>

public enum StartupPhase

{

    Booting,

    UiReady,

    BackgroundInitializing,

    Running,

}

