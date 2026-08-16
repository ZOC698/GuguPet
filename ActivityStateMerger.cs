namespace GuguPet;

public static class ActivityStateMerger
{
    private const int MaxTasks = 8;

    public static CodexActivityState Merge(
        CodexActivityState? codex,
        CodexActivityState? dsh)
    {
        var tasks = new[] { codex, dsh }
            .Where(state => state is not null)
            .SelectMany(state => state!.Tasks)
            .GroupBy(task => $"{task.Source}:{task.ThreadId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(task => task.UpdatedAt).First())
            .OrderBy(task => StatePriority(task.State))
            .ThenByDescending(task => task.UpdatedAt)
            .Take(MaxTasks)
            .ToArray();

        // Each watcher has already selected its current actionable state.
        // Historical task rows may still contain a recently completed item,
        // so they must never drive the pet after that source has settled idle.
        var focus = new[] { codex, dsh }
            .Where(state => state is not null)
            .Select(state => state!)
            .OrderBy(state => StatePriority(state.State))
            .ThenByDescending(state => state.UpdatedAt)
            .FirstOrDefault();
        if (focus is not null)
            return new CodexActivityState(
                focus.State,
                focus.Message,
                focus.UpdatedAt,
                focus.ThreadId,
                tasks,
                focus.Source);
        return new CodexActivityState(
            "idle", LocalizationService.T("AI 助手已待命"), DateTimeOffset.Now, "", tasks);
    }

    private static int StatePriority(string state) => state switch
    {
        "waiting" => 0,
        "failed" => 1,
        "running" => 2,
        "review" => 3,
        _ => 4
    };
}
