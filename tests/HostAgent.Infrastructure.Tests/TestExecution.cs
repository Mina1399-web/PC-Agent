using Xunit;
// Windows IPC/deadline tests and PowerShell/MSI compilation share the constrained
// DEV VM. Preserve their assertions without resource-starving parallel classes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
