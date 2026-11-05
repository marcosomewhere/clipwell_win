using Xunit.v3;

// WPF smoke tests share Application.Current and the clipboard; they must not run in parallel.
[assembly: Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
