// parallellization fucks with metrics tests due to shared activity sources / meters
// metrics collection works correctly but tests see each other's metrics and traces
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]