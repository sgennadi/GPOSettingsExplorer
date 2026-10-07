namespace GPOSettingsExplorer.Services;

public static class StaTask
{
    public static Task Run(
        Action action)
    {
        return Run(
            () =>
            {
                action();
                return true;
            });
    }

    public static Task<T> Run<T>(
        Func<T> action)
    {
        var completion =
            new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var thread =
            new Thread(
                () =>
                {
                    try
                    {
                        completion.SetResult(
                            action());
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(
                            ex);
                    }
                })
            {
                IsBackground = true,
                Name = "GPOSettingsExplorer-STA"
            };

        thread.SetApartmentState(
            ApartmentState.STA);

        thread.Start();

        return completion.Task;
    }
}
