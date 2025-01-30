using Application.Interfaces;
using Application.Interfaces.Policies;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ServiceMeshHelper;
using ServiceMeshHelper.BusinessObjects;
using ServiceMeshHelper.BusinessObjects.InterServiceRequests;
using ServiceMeshHelper.Controllers;

namespace Infrastructure.Clients;

public class RouteTimeProviderClient : IRouteTimeProvider
{
    private const string AliveMessage = "isAlive";

    private readonly IInfiniteRetryPolicy<RouteTimeProviderClient> _infiniteRetry;
    private readonly ILogger _logger;

    public RouteTimeProviderClient(IInfiniteRetryPolicy<RouteTimeProviderClient> infiniteRetry, ILogger<RouteTimeProviderClient> logger)
    {
        _infiniteRetry = infiniteRetry;
        _logger = logger;
    }

    public Task<int> GetTravelTimeInSeconds(string startingCoordinates, string destinationCoordinates)
    {
        return _infiniteRetry.ExecuteAsync(async () =>
        {
            var res = await RestController.Get(new GetRoutingRequest()
            {
                TargetService = "RouteTimeProvider",
                Endpoint = $"RouteTime/Get",
                Params = new List<NameValue>()
                {
                    new()
                    {
                        Name = "startingCoordinates",
                        Value = startingCoordinates
                    },
                    new()
                    {
                        Name = "destinationCoordinates",
                        Value = destinationCoordinates
                    },
                },
                Mode = LoadBalancingMode.Broadcast
            });

            var times = new List<int>();

            await foreach (var result in res!.ReadAllAsync())
            {
                times.Add(JsonConvert.DeserializeObject<int>(result.Content));
            }

            return (int)times.Average();
        });
    }

    public Task<bool> IsServiceAlive()
    {
        return _infiniteRetry.ExecuteAsync(async () =>
        {
            var res = await RestController.Get(new GetRoutingRequest
            {
                TargetService = "RouteTimeProvider",
                Endpoint = "ServiceHealth/Get",
                Mode = LoadBalancingMode.Broadcast
            });

            var isAlive = false;
            await foreach (var result in res.ReadAllAsync())
            {
                if (result.Content is not null)
                {
                    isAlive |= JsonConvert.DeserializeObject<string>(result.Content) == AliveMessage;
                }
            }

            // hello?
            if (!isAlive)
            {
                var tests = await RestController.GetAddress(Environment.GetEnvironmentVariable("RTP_SERVICE_NAME"),
                    LoadBalancingMode.Broadcast);

                foreach (var test in tests)
                {
                    _logger.LogInformation($"Address:{test.Address} Host:{test.Host} Port:{test.Port}");
                }
            }

            return isAlive;
        });
    }
}

//Example of how to use Restsharp for a simple request to a service (without the pros (and cons) of using the NodeController)

//var restClient = new RestClient("http://RouteTimeProvider");
//var restRequest = new RestRequest("RouteTime/Get");

//restRequest.AddQueryParameter("startingCoordinates", startingCoordinates);
//restRequest.AddQueryParameter("destinationCoordinates", destinationCoordinates);

//return (await restClient.ExecuteGetAsync<int>(restRequest)).Data;