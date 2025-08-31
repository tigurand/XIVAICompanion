using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace XIVAICompanion.Utils
{
    public unsafe class WeatherForecastProvider
    {
        private const double Seconds = 1;
        private const double Minutes = 60 * Seconds;
        private const double WeatherPeriod = 23 * Minutes + 20 * Seconds;

        private readonly IDataManager _dataManager;

        public WeatherForecastProvider(IDataManager dataManager)
        {
            _dataManager = dataManager;
        }

        public List<WeatherForecast> GetWeatherForecast(ushort territoryId)
        {
            try
            {
                var framework = Framework.Instance();
                if (framework == null)
                    return CreateUnknownForecast();

                var weatherManager = WeatherManager.Instance();
                if (weatherManager == null)
                    return CreateUnknownForecast();

                var weatherSheet = _dataManager.GetExcelSheet<Weather>();
                if (weatherSheet == null)
                    return CreateUnknownForecast();

                byte currentWeatherId = weatherManager->GetCurrentWeather();
                var currentWeather = weatherSheet.GetRow(currentWeatherId);

                var result = new List<WeatherForecast>
                {
                    BuildResultObject(currentWeather, GetRootTime(0))
                };

                Weather lastWeather = currentWeather;

                for (var i = 1; i < 24 && result.Count < 5; i++)
                {
                    byte weatherId = weatherManager->GetWeatherForDaytime(territoryId, i);
                    var weather = weatherSheet.GetRow(weatherId);

                    var time = GetRootTime(i * WeatherPeriod);

                    if (lastWeather.RowId != weather.RowId)
                    {
                        lastWeather = weather;
                        result.Add(BuildResultObject(weather, time));
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Service.Log.Warning($"Failed to get weather forecast: {ex.Message}");
                return CreateUnknownForecast();
            }
        }

        private List<WeatherForecast> CreateUnknownForecast()
        {
            var now = DateTime.UtcNow;
            return new List<WeatherForecast>
            {
                new WeatherForecast(now, "Now", "Unknown", 0)
            };
        }

        private static WeatherForecast BuildResultObject(Weather weather, DateTime time)
        {
            var timeString = FormatForecastTime(time);
            var name = weather.Name.ExtractText();
            var iconId = (uint)weather.Icon;

            return new WeatherForecast(time, timeString, name, iconId);
        }

        private static DateTime GetRootTime(double initialOffset)
        {
            var now = DateTime.UtcNow;
            var rootTime = now.AddMilliseconds(-now.Millisecond).AddSeconds(initialOffset);
            var seconds = (long)(rootTime - DateTime.UnixEpoch).TotalSeconds % WeatherPeriod;

            rootTime = rootTime.AddSeconds(-seconds);

            return rootTime;
        }

        private static string FormatForecastTime(DateTime forecastTime)
        {
            var timeDiff = forecastTime - DateTime.UtcNow;

            if (Math.Abs(timeDiff.TotalMinutes) < 1)
                return "Now";
            else if (timeDiff.TotalMinutes < 0)
                return "Current";
            else if (timeDiff.TotalMinutes < 60)
                return $"in {(int)timeDiff.TotalMinutes}m";
            else if (timeDiff.TotalHours < 24)
                return $"in {(int)timeDiff.TotalHours}h {(int)(timeDiff.TotalMinutes % 60)}m";
            else
                return $"in {(int)timeDiff.TotalDays}d {(int)(timeDiff.TotalHours % 24)}h";
        }
    }
}