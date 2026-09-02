using Xunit;

// Order-independence guard: opt-in shuffling via LOCINTEL_TEST_SHUFFLE=<seed>.
// See RandomOrderer.
[assembly: TestCaseOrderer("LocIntel.IntegrationTests.RandomOrderer", "LocIntel.IntegrationTests")]
