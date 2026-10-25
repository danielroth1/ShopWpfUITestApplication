using Xunit;

// The harness owns exactly one WPF Application and one UI thread, so UI tests must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
