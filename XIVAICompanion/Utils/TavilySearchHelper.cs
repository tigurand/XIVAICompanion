using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XIVAICompanion.Utils
{
    public static class TavilySearchHelper
    {
        public static async Task<string> SearchAsync(string query, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) return "Tavily API key is missing.";

            try
            {
                using var client = new HttpClient();
                var requestBody = new
                {
                    api_key = apiKey,
                    query = query,
                    search_depth = "basic",
                    max_results = 5
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.tavily.com/search", content);

                if (!response.IsSuccessStatusCode)
                {
                    return $"Search failed with status: {response.StatusCode}";
                }

                var rawJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(rawJson);
                var results = json["results"];

                if (results == null || !results.HasValues)
                {
                    return "No search results found.";
                }

                var sb = new StringBuilder();
                sb.AppendLine("Search Results:");
                foreach (var result in results)
                {
                    string title = result["title"]?.ToString() ?? "No Title";
                    string url = result["url"]?.ToString() ?? "No URL";
                    string contentSnippet = result["content"]?.ToString() ?? "No Content";

                    sb.AppendLine($"- {title} ({url}): {contentSnippet}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"An error occurred during search: {ex.Message}";
            }
        }
    }
}
