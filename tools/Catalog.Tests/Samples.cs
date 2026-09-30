namespace NitrogenCatalog.Tests;

/// <summary>Record texts matching the pre-migration Python test fixtures.</summary>
static class Samples
{
    public const string Capability = """
        capability Concurrency.CoalesceInFlight
        {
          name "Coalesce in-flight work";
          definition "Share one pending computation per key.";
          contract (key: K) -> V effect host;
        }
        """;

    public static string Concept(string status = "candidate", string extra = "", bool provides = true) => $$"""
        concept Concurrency.SingleFlight {{status}}
        {
          name "SingleFlight";
          definition "Concurrent requests for one key share one pending computation.";
          inputs (key: K);
          outputs (value: V);
          limit "Cancellation policy must be explicit.";
        {{(provides ? "  provides Concurrency.CoalesceInFlight;" : "")}}
        {{extra}}
        }
        """;

    public static string Evidence(string id = "EV-20260929-failure", string extra = "") => $$"""
        evidence {{id}}
        {
          date 2026-09-29;
          problem "async cache";
          domain "software";
          requests Concurrency.CoalesceInFlight;
          subject Concurrency.SingleFlight;
          match adaptation;
          outcome failed;
          verification failed "concurrency test";
          reason "Cancellation policy did not match.";
          source "local test";
        {{extra}}
        }
        """;
}
