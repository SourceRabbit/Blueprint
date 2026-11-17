namespace Blueprint.BackgroundTasks
{
    /// <summary>The current execution status of a background task.</summary>
    public enum EBackgroundTaskStatus
    {
        /// <summary>The task is not running.</summary>
        Idle,

        /// <summary>The task has started an execution.</summary>
        Running
    }
}
