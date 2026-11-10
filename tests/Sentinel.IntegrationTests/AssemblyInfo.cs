// The local RPC/index fixture and Serilog are process-wide; isolate test scenarios.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
