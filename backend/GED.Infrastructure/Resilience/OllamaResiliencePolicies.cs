using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace GED.Infrastructure.Resilience;

/// <summary>
/// Polly resilience policies for all outbound calls to Ollama (LLM).
///
/// Why: If Ollama goes down or gets slow, raw HttpClient calls hang for 60s+,
/// ASP.NET threads pile up, and the entire API becomes unresponsive.
/// Pattern: Circuit Breaker (ByteByteGo) — fail fast when a dependency is unhealthy,
/// let it recover, then gradually re-allow traffic.
///
/// USAGE in Program.cs:
///   var ollamaPolicy = OllamaResiliencePolicies.Combined();
///   builder.Services.AddHttpClient<RagService>().AddPolicyHandler(ollamaPolicy);
/// </summary>
public static class OllamaResiliencePolicies
{
    // ─── Per-request timeout ────────────────────────────────────────────────────
    // Fail fast instead of waiting 60s for an Ollama response.
    // TimeoutStrategy.Optimistic cooperates with CancellationToken.
    public static IAsyncPolicy<HttpResponseMessage> TimeoutPolicy(int seconds = 90)
        => Policy.TimeoutAsync<HttpResponseMessage>(
            TimeSpan.FromSeconds(seconds),
            TimeoutStrategy.Optimistic);

    // ─── Retry with exponential backoff + jitter ────────────────────────────────
    // 3 retries: waits 2s, 4s, 8s (± 400ms random jitter each).
    // Jitter prevents "thundering herd" — all retries hitting at once after recovery.
    // (ByteByteGo: Retry Strategy — always add jitter to exponential backoff)
    public static IAsyncPolicy<HttpResponseMessage> RetryPolicy()
        => HttpPolicyExtensions
            .HandleTransientHttpError()                        // 5xx, network errors
            .Or<TimeoutRejectedException>()                    // our own timeout
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                {
                    var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    var jitter    = TimeSpan.FromMilliseconds(Random.Shared.Next(-400, 400));
                    return baseDelay + jitter;
                },
                onRetry: (outcome, delay, attempt, _) =>
                {
                    var reason = outcome.Exception?.Message
                                 ?? outcome.Result.StatusCode.ToString();
                    Console.WriteLine(
                        $"[Ollama] Retry {attempt}/3 in {delay.TotalSeconds:F1}s — {reason}");
                });

    // ─── Circuit Breaker ────────────────────────────────────────────────────────
    // Opens after 5 consecutive failures; stays open 30s (no traffic during that time).
    //
    // States:
    //   Closed   → normal operation, requests flow through
    //   Open     → requests fail immediately (no waiting for Ollama timeout)
    //   HalfOpen → one test request; if it succeeds, circuit Closes again
    //
    // (ByteByteGo: Circuit Breaker — prevent cascading failures)
    public static IAsyncPolicy<HttpResponseMessage> CircuitBreakerPolicy()
        => HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak:    (outcome, ts) =>
                {
                    var reason = outcome.Exception?.Message
                                 ?? outcome.Result.StatusCode.ToString();
                    Console.WriteLine(
                        $"[Ollama] ⛔ Circuit OPEN for {ts.TotalSeconds}s — {reason}");
                },
                onReset:    () => Console.WriteLine("[Ollama] ✅ Circuit CLOSED — Ollama recovered"),
                onHalfOpen: () => Console.WriteLine("[Ollama] 🔄 Circuit HALF-OPEN — testing Ollama")
            );

    // ─── Combined policy (recommended for most services) ───────────────────────
    // Wrap order matters: Timeout → Retry → CircuitBreaker
    // The timeout applies per attempt (not total), so 3 retries × 90s = 270s max.
    public static IAsyncPolicy<HttpResponseMessage> Combined(int timeoutSeconds = 90)
        => Policy.WrapAsync(
            TimeoutPolicy(timeoutSeconds),
            RetryPolicy(),
            CircuitBreakerPolicy()
        );

    // ─── Variant for chunked OCR cleaning (longer per-chunk timeout) ───────────
    public static IAsyncPolicy<HttpResponseMessage> ForOcrCleaning()
        => Combined(timeoutSeconds: 120);
}