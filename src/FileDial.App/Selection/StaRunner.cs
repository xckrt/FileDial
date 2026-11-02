namespace FileDial.App.Selection;

/// <summary>Выполняет работу в отдельном STA-потоке, чтобы COM-вызовы в Проводник не блокировали UI.</summary>
internal static class StaRunner
{
    public static async Task<T> RunAsync<T>(Func<T> work, TimeSpan timeout, Func<T> onTimeout)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true, // зависший Проводник не помешает выходу из программы
            Name = "FileDial shell STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var finished = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(true);
        return finished == tcs.Task ? await tcs.Task.ConfigureAwait(true) : onTimeout();
    }
}
