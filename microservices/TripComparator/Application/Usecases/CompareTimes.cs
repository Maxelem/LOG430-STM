using System.Threading.Channels;
using Application.BusinessObjects;
using Application.DTO;
using Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Usecases
{
    public class CompareTimes
    {
        private readonly IRouteTimeProvider _routeTimeProvider;

        private readonly IBusInfoProvider _iBusInfoProvider;

        private readonly IDataStreamWriteModel _dataStreamWriteModel;

        //This is a very aggressive polling rate, is there a better way to do this?
        private readonly PeriodicTimer _periodicTimer = new(TimeSpan.FromMilliseconds(50));

        private readonly PeriodicTimer _healthCheckPeriodicTimer = new(TimeSpan.FromMilliseconds(50));

        private int _averageCarTravelTime;

        private RideDto? _optimalBus;

        private readonly ILogger<CompareTimes> _logger;

        public CompareTimes(IRouteTimeProvider routeTimeProvider, IBusInfoProvider iBusInfoProvider, IDataStreamWriteModel dataStreamWriteModel, ILogger<CompareTimes> logger)
        {
            _routeTimeProvider = routeTimeProvider;
            _iBusInfoProvider = iBusInfoProvider;
            _dataStreamWriteModel = dataStreamWriteModel;
            _logger = logger;
        }

        public async Task BeginHealthCheck()
        {
            var isServiceAlive = true;
            while (await _healthCheckPeriodicTimer.WaitForNextTickAsync())
            {
                isServiceAlive = await _routeTimeProvider.IsServiceAlive();

                if (!isServiceAlive)
                {
                    _logger.LogInformation($"RouteTimeProvider service is dead.");
                }
            }
        }

        public async Task<Channel<IBusPositionUpdated>> BeginComparingBusAndCarTime(string startingCoordinates, string destinationCoordinates)
        {
            await Task.WhenAll(
                _routeTimeProvider.GetTravelTimeInSeconds(startingCoordinates, destinationCoordinates)
                    .ContinueWith(task => _averageCarTravelTime = task.Result),

                _iBusInfoProvider.GetBestBus(startingCoordinates, destinationCoordinates)
                    .ContinueWith(task =>
                    {
                        _optimalBus = task.Result;

                        return _iBusInfoProvider.BeginTracking(_optimalBus);
                    })
                );

            if (_optimalBus is null || _averageCarTravelTime < 1)
            {
                throw new Exception("bus or car data was null");
            }

            var channel = Channel.CreateUnbounded<IBusPositionUpdated>();

            return channel;
        }

        //Is polling ideal?
        public async Task PollTrackingUpdate(ChannelWriter<IBusPositionUpdated> channel)
        {
            if (_optimalBus is null)
            {
                throw new Exception("bus data was null");
            }

            var trackingOnGoing = true;

            while (trackingOnGoing && await _periodicTimer.WaitForNextTickAsync())
            {
                var trackingResult = await _iBusInfoProvider.GetTrackingUpdate();

                if (trackingResult is null)
                {
                    continue;
                }

                trackingOnGoing = !trackingResult.TrackingCompleted;

                var busPosition = new BusPosition
                {
                    Message = trackingResult.Message + $"\nCar: {_averageCarTravelTime} seconds",
                    Seconds = trackingResult.Duration,
                };

                await channel.WriteAsync(busPosition);
            }

            channel.Complete();
        }

        public async Task WriteToStream(ChannelReader<IBusPositionUpdated> channelReader)
        {
            await foreach (var busPositionUpdated in channelReader!.ReadAllAsync())
            {
                await _dataStreamWriteModel.Produce(busPositionUpdated);
            }
        }
    }
}