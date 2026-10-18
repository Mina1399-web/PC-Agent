using Xunit;

// Parallel STA fixtures reproduced a shared PackagePart stream-list race while
// loading XAML. Match the desktop's single UI-thread model between test cases;
// each test still exercises its own asynchronous response/cancellation races.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
