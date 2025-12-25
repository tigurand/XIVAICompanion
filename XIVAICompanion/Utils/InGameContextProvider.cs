using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace XIVAICompanion.Utils
{
    public enum Tribe : byte
    {
        Midlander = 1,
        Highlander = 2,
        Wildwood = 3,
        Duskwight = 4,
        Plainsfolk = 5,
        Dunesfolk = 6,
        SunSeeker = 7,
        MoonKeeper = 8,
        SeaWolf = 9,
        Hellsguard = 10,
        Raen = 11,
        Xaela = 12,
        Helion = 13,
        Lost = 14,
        Rava = 15,
        Veena = 16
    }

    public class PlayerContext
    {
        public string Race { get; set; } = "Unknown";
        public string Gender { get; set; } = "Unknown";
        public string Clan { get; set; } = "Unknown";
        public string SkinTone { get; set; } = "Unknown";
        public string HairColor { get; set; } = "Unknown";
        public string ClassJob { get; set; } = "Unknown";
        public int Level { get; set; } = 0;
        public string Homeworld { get; set; } = "Unknown";
    }

    public class GameContext
    {
        public string CurrentWorld { get; set; } = "Unknown";
        public string Location { get; set; } = "Unknown";
        public string Weather { get; set; } = "Unknown";
        public List<WeatherForecast> WeatherForecast { get; set; } = new List<WeatherForecast>();
    }

    public static class InGameContextProvider
    {
        public static PlayerContext GetPlayerContext(IPlayerCharacter? player, IDataManager dataManager)
        {
            var context = new PlayerContext();

            if (player == null)
                return context;

            try
            {
                context.Level = player.Level;
                context.ClassJob = player.ClassJob.Value.NameEnglish.ToString() ?? "Unknown";
                context.Homeworld = player.HomeWorld.Value.Name.ToString() ?? "Unknown";

                var customize = player.Customize;
                if (customize != null && customize.Length > 0)
                {
                    context.Gender = customize[1] == 1 ? "Female" : "Male";
                    context.Race = GetRaceName(customize[0]);
                    context.Clan = GetClanName(customize[0], customize[4]);
                    context.SkinTone = GetSkinTone(customize[8]);
                    context.HairColor = GetHairColor(customize[10]);
                }
            }
            catch (Exception ex)
            {
                Service.Log.Warning($"Failed to get player context: {ex.Message}");
            }

            return context;
        }

        public static GameContext GetGameContext(IClientState clientState, IDataManager dataManager)
        {
            var context = new GameContext();

            try
            {
                if (Service.ObjectTable.LocalPlayer?.CurrentWorld != null)
                {
                    context.CurrentWorld = Service.ObjectTable.LocalPlayer.CurrentWorld.Value.Name.ToString() ?? "Unknown";
                }

                if (clientState.TerritoryType != 0)
                {
                    context.Location = $"{Service.DataManager.GetExcelSheet<TerritoryType>()?.GetRow(Service.ClientState.TerritoryType).PlaceName.Value.Name.ToString() ?? "Unknown"}";

                    var weatherProvider = new WeatherForecastProvider(dataManager);
                    context.WeatherForecast = weatherProvider.GetWeatherForecast(clientState.TerritoryType);

                    if (context.WeatherForecast.Count > 0)
                    {
                        context.Weather = context.WeatherForecast[0].Name;
                    }
                    else
                    {
                        context.Weather = "Unknown";
                    }
                }
            }
            catch (Exception ex)
            {
                Service.Log.Warning($"Failed to get game context: {ex.Message}");
            }

            return context;
        }

        public static string GetEorzeaTime(DateTime? realTime = null)
        {
            const double multiplier = 1440.0 / 70.0;
            DateTime input = realTime?.ToUniversalTime() ?? DateTime.UtcNow;

            double unixMillis = (input - DateTime.UnixEpoch).TotalMilliseconds;
            double eorzeaMillis = unixMillis * multiplier;
            DateTime eorzeaTime = DateTime.UnixEpoch.AddMilliseconds(eorzeaMillis);

            return $"{eorzeaTime.Hour:D2}:{eorzeaTime.Minute:D2}";
        }


        public static string FormatContextForPrompt(PlayerContext playerContext, GameContext gameContext)
        {
            var contextLines = new List<string>();
            DateTimeOffset local = DateTimeOffset.Now;
            var offset = DateTimeOffset.Now.Offset;
            string tz = local.Offset.Minutes == 0
                ? $"UTC{(offset >= TimeSpan.Zero ? "+" : "-")}{offset.Hours}"
                : $"UTC{(offset >= TimeSpan.Zero ? "+" : "-")}{offset:hh\\:mm}";
            var et = GetEorzeaTime();

            contextLines.Add("=== Player Information ===");
            contextLines.Add($"Race: {playerContext.Race}");
            contextLines.Add($"Gender: {playerContext.Gender}");
            contextLines.Add($"Clan: {playerContext.Clan}");
            contextLines.Add($"Skin Tone: {playerContext.SkinTone}");
            contextLines.Add($"Hair Color: {playerContext.HairColor}");
            contextLines.Add($"Class/Job: {playerContext.ClassJob}");
            contextLines.Add($"Level: {playerContext.Level}");
            contextLines.Add($"Homeworld (Home Server): {playerContext.Homeworld}");

            contextLines.Add("\n=== Time Information ===");
            contextLines.Add($"Local Time: {local.DateTime}");
            contextLines.Add($"Time Zone: {tz}");
            contextLines.Add($"Eorzea Time: {et}");

            contextLines.Add("\n=== Current Environment ===");
            contextLines.Add($"Current World/Server: {gameContext.CurrentWorld}");
            contextLines.Add($"Location: {gameContext.Location}");
            contextLines.Add($"Current Weather: {gameContext.Weather}");

            if (gameContext.WeatherForecast.Count > 0)
            {
                contextLines.Add("\n=== Weather Forecast ===");

                for (int i = 0; i < Math.Min(gameContext.WeatherForecast.Count, 5); i++)
                {
                    var forecast = gameContext.WeatherForecast[i];
                    if (i == 0)
                    {
                        contextLines.Add($"Current: {forecast.Name}");
                    }
                    else
                    {
                        contextLines.Add($"Next: {forecast.Name} ({forecast.TimeString})");
                    }
                }
            }

            return string.Join("\n", contextLines) + "\n\n";
        }

        private static string GetRaceName(byte raceId)
        {
            return raceId switch
            {
                1 => "Hyur",
                2 => "Elezen",
                3 => "Lalafell",
                4 => "Miqo'te",
                5 => "Roegadyn",
                6 => "Au Ra",
                7 => "Hrothgar",
                8 => "Viera",
                _ => "Unknown"
            };
        }

        private static string GetClanName(byte raceId, byte tribeId)
        {
            return (Tribe)tribeId switch
            {
                Tribe.Midlander => "Midlander",
                Tribe.Highlander => "Highlander",
                Tribe.Wildwood => "Wildwood",
                Tribe.Duskwight => "Duskwight",
                Tribe.Plainsfolk => "Plainsfolk",
                Tribe.Dunesfolk => "Dunesfolk",
                Tribe.SunSeeker => "Seeker of the Sun",
                Tribe.MoonKeeper => "Keeper of the Moon",
                Tribe.SeaWolf => "Sea Wolf",
                Tribe.Hellsguard => "Hellsguard",
                Tribe.Raen => "Raen",
                Tribe.Xaela => "Xaela",
                Tribe.Helion => "Helions",
                Tribe.Lost => "The Lost",
                Tribe.Rava => "Rava",
                Tribe.Veena => "Veena",
                _ => "Unknown"
            };
        }

        private static string GetSkinTone(byte skinColorValue)
        {
            int index = 0;
            int xCoordinate = 0;
            for (int y = 0; y < 24; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (index == skinColorValue)
                    {
                        xCoordinate = x;
                        break;
                    }
                    index++;
                }
                if (index == skinColorValue)
                {
                    break;
                }
            }
            switch (xCoordinate)
            {
                case 0:
                case 1:
                    return "Pale";
                case 2:
                case 3:
                    return "Fair";
                case 4:
                case 5:
                    return "Tan";
                case 6:
                case 7:
                    return "Dark";
            }
            return "Unknown";
        }

        private static string GetHairColor(byte hairColorValue)
        {
            string[] names = new string[] { "Grey","Cream","Greyish Cream","Tinted Yellow","Greyish Tinted Yellow",
                "Yellow","Greyish Tinted Yellow","Tinted Orange","Greyish Tinted Orange","Orange","Greyish Orange",
                "Red","Greyish Red","Pink","Magenta","Greyish Magenta","Greyish Pink","Purple","Greyish Purple","Blue",
                "Greyish Greenish Blue","Green","Greyish Green","Tinted Green" };
            int index = 0;
            int xCoordinate = 0;
            int yCoordinate = 0;
            for (int y = 0; y < 24; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (index == hairColorValue)
                    {
                        xCoordinate = x;
                        yCoordinate = y;
                        break;
                    }
                    index++;
                }
                if (index == hairColorValue)
                {
                    break;
                }
            }
            switch (xCoordinate)
            {
                case 0:
                    if (yCoordinate <= 2) return "White";
                    return "light " + names[yCoordinate].ToLower();
                case 1:
                case 2:
                case 3:
                    return "light " + names[yCoordinate].ToLower();
                case 4:
                case 5:
                case 6:
                case 7:
                    if (yCoordinate == 0) return "Black";
                    return "Dark " + names[yCoordinate].ToLower();
            }
            return "Unknown";
        }
    }
}