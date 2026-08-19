using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using endfield_player_position_display.Models;
using endfield_player_position_display.Services;

namespace endfield_player_position_display.Tests
{
    internal static class SklandApiRequestTests
    {
        public static void GrantAndGenerateCredentialUseCurrentSklandEndpoints()
        {
            var handler = new QueueResponseHandler(
                new[]
                {
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"status\":0,\"data\":{\"code\":\"grant-code\"}}")
                    },
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"code\":0,\"message\":\"OK\",\"data\":{\"cred\":\"cred-value\",\"userId\":\"user-1\",\"token\":\"sign-token\"}}")
                    }
                });

            using (var httpClient = new HttpClient(handler))
            using (var apiClient = new SklandApiClient(httpClient, false))
            {
                string code = apiClient.GrantAsync("token-value", CancellationToken.None).GetAwaiter().GetResult();
                TestAssert.AreEqual("grant-code", code);

                CredentialResult credential = apiClient.GenerateCredentialAsync(code, CancellationToken.None).GetAwaiter().GetResult();
                TestAssert.AreEqual("cred-value", credential.Cred);
                TestAssert.AreEqual("user-1", credential.UserId);
                TestAssert.AreEqual("sign-token", credential.Token);
            }

            TestAssert.AreEqual("https://as.hypergryph.com/user/oauth2/v2/grant", handler.Requests[0].RequestUri);
            TestAssert.AreEqual("https://zonai.skland.com/api/v1/user/auth/generate_cred_by_code", handler.Requests[1].RequestUri);
            TestAssert.IsTrue(handler.Requests[0].UserAgent.Contains("Skland/1.0.1"));
            TestAssert.IsTrue(handler.Requests[1].UserAgent.Contains("Skland/1.0.1"));
        }

        private sealed class QueueResponseHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> responses;

            public QueueResponseHandler(IEnumerable<HttpResponseMessage> responses)
            {
                this.responses = new Queue<HttpResponseMessage>(responses);
                Requests = new List<RecordedRequest>();
            }

            public List<RecordedRequest> Requests { get; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string body = request.Content == null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Requests.Add(new RecordedRequest(
                    request.Method,
                    request.RequestUri.ToString(),
                    body,
                    request.Headers.UserAgent.ToString()));
                return Task.FromResult(responses.Dequeue());
            }
        }

        private sealed class RecordedRequest
        {
            public RecordedRequest(HttpMethod method, string requestUri, string body, string userAgent)
            {
                Method = method;
                RequestUri = requestUri;
                Body = body;
                UserAgent = userAgent;
            }

            public HttpMethod Method { get; }

            public string RequestUri { get; }

            public string Body { get; }

            public string UserAgent { get; }
        }
    }
}
