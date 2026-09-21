using Xunit;

// Integration tests spin up a real WebApplicationFactory<Program> TestServer.
// Running test classes in parallel can race the ASP.NET Core host-factory
// interception (a known WebApplicationFactory gotcha) and can also race the
// in-process static run-tracking state the live orchestrator endpoints use.
// Serializing the whole assembly trades a little test wall-clock time for
// reliable, non-flaky runs - the right tradeoff for a small test suite like
// this one.
[assembly: CollectionBehavior(DisableTestParallelization = true)]