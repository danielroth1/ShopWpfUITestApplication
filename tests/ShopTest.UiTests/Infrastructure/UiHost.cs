using System.Windows.Threading;

namespace ShopTest.UiTests.Infrastructure;

/// <summary>
/// Owns the single WPF <see cref="System.Windows.Application"/> and the STA thread it lives on.
/// </summary>
/// <remarks>
/// <para>
/// Only one <see cref="System.Windows.Application"/> can exist per process and it is thread affine, so every
/// UI test marshals onto this one thread. The real <see cref="App"/> is constructed so that <c>App.xaml</c>
/// resources (brushes, styles, converters) resolve exactly as they do at runtime.
/// </para>
/// <para>
/// WPF raises <c>Startup</c> - and therefore <see cref="App.OnStartup"/> - as soon as the dispatcher loop
/// begins, even though <c>App.Run()</c> is never called here. <see cref="App.SkipStartupVariable"/> turns
/// that off, so no host is built, no migration or seeding runs, no database is touched and no window appears.
/// </para>
/// </remarks>
internal static class UiHost
{
    private static readonly Lazy<Dispatcher> LazyDispatcher =
        new(StartUiThread, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dispatcher Dispatcher => LazyDispatcher.Value;

    /// <summary>Runs <paramref name="action"/> on the UI thread and rethrows any failure.</summary>
    public static void Invoke(Action action) => Dispatcher.Invoke(DispatcherPriority.Send, action);

    /// <summary>Runs <paramref name="func"/> on the UI thread and returns its result.</summary>
    public static T Invoke<T>(Func<T> func) => Dispatcher.Invoke(func, DispatcherPriority.Send);

    /// <summary>
    /// Pumps the dispatcher until <paramref name="condition"/> holds, or throws <see cref="TimeoutException"/>.
    /// </summary>
    /// <remarks>
    /// Needed because WPF automation peers post their work: <c>ButtonAutomationPeer.Invoke</c> dispatches
    /// the click at <see cref="DispatcherPriority.Input"/>, so the change is not observable until the
    /// dispatcher has run it.
    /// </remarks>
    public static void PumpUntil(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(5);
        DateTime deadline = DateTime.UtcNow + limit;

        while (!Invoke(condition))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"UI did not reach the expected state within {limit.TotalSeconds:0.#}s: {because}");
            }

            // Let queued work (bindings, layout, peer-posted clicks) run, then re-check.
            Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            Thread.Sleep(10);
        }
    }

    private static Dispatcher StartUiThread()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                // Keep the real App class (LoadComponent only accepts the exact BAML-registered type) but
                // tell it not to start the host. See App.SkipStartupVariable.
                Environment.SetEnvironmentVariable(ShopTest.App.App.SkipStartupVariable, "1");

                var app = new ShopTest.App.App();
                app.InitializeComponent();
                ready.SetResult(Dispatcher.CurrentDispatcher);
            }
            catch (Exception ex)
            {
                ready.SetException(ex);
                return;
            }

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "ShopTest UI thread",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return ready.Task.GetAwaiter().GetResult();
    }
}
