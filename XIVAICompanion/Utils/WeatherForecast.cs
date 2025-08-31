using System;

namespace XIVAICompanion.Utils
{
    public class WeatherForecast
    {
        public DateTime Time { get; set; }
        public TimeSpan TimeSpan { get; set; }
        public string TimeString { get; set; }
        public string Name { get; set; }
        public uint IconId { get; set; }

        public WeatherForecast(DateTime time, string timeString, string name, uint iconId)
        {
            Time = time;
            TimeSpan = time - DateTime.UtcNow;
            TimeString = timeString;
            Name = name;
            IconId = iconId;
        }

        public override string ToString()
        {
            return $"{Name} {TimeString}";
        }
    }
}