using System.Net;
using TeamStation.Core.Services;

namespace TeamStation.Tests;

public class TeamViewerCloudSyncServiceTests
{
    [Fact]
    public async Task PullAsync_imports_only_ids_that_normalize_to_valid_ascii_digits()
    {
        using var http = new HttpClient(new StubTeamViewerHandler())
        {
            BaseAddress = new Uri("https://teamviewer.test/api/v1/"),
        };
        var service = new TeamViewerCloudSyncService(http);

        var result = await service.PullAsync("token");

        var entry = Assert.Single(result.Entries);
        Assert.Equal("123456789", entry.TeamViewerId);
        Assert.Equal("Spaced ASCII", entry.Name);
    }

    private sealed class StubTeamViewerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            var json = path.Contains("groups", StringComparison.OrdinalIgnoreCase)
                ? """{"groups":[{"id":"g1","name":"Ops"}]}"""
                : """
                  {
                    "devices": [
                      { "remotecontrol_id": "123 456 789", "alias": "Spaced ASCII" },
                      { "remotecontrol_id": "\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669", "alias": "Unicode digits" },
                      { "remotecontrol_id": "1234567", "alias": "Too short" }
                    ]
                  }
                  """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            });
        }
    }
}
