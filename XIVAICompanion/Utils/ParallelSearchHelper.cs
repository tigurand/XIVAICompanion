using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XIVAICompanion.Utils
{
    public static class ParallelSearchHelper
    {
        public static async Task<WebSearchResult> SearchAsync(string query, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return new WebSearchResult { Success = false, Text = "Parallel API key is missing." };

            try
            {
                using var client = new HttpClient();
                var requestBody = new
                {
                    objective = query,
                    search_queries = new[] { query },
                    mode = "basic",
                    max_chars_total = 6000,
                    advanced_settings = new
                    {
                        max_results = 5,
                        excerpt_settings = new { max_chars_per_result = 1200 }
                    }
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.parallel.ai/v1/search")
                {
                    Content = content
                };
                request.Headers.Add("x-api-key", apiKey);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return new WebSearchResult { Success = false, Text = $"Parallel search failed with status: {response.StatusCode}" };
                }

                var rawJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(rawJson);
                var results = json["results"] as JArray;

                if (results == null || results.Count == 0)
                {
                    return new WebSearchResult { Success = false, Text = "No search results found (Parallel)." };
                }

                var sb = new StringBuilder();
                sb.AppendLine("Search Results (Parallel):");
                foreach (var result in results)
                {
                    string title = (string?)result["title"] ?? "No Title";
                    string url = (string?)result["url"] ?? "No URL";

                    string snippet = string.Empty;
                    var excerpts = result["excerpts"] as JArray;
                    if (excerpts != null && excerpts.Count > 0)
                    {
                        snippet = string.Join(" ... ", excerpts.Select(e => (string?)e ?? string.Empty));
                    }
                    if (string.IsNullOrWhiteSpace(snippet)) snippet = "No Content";

                    sb.AppendLine($"- {title} ({url}): {snippet}");
                }

                return new WebSearchResult { Success = true, Text = sb.ToString().TrimEnd() };
            }
            catch (Exception ex)
            {
                return new WebSearchResult { Success = false, Text = $"An error occurred during Parallel search: {ex.Message}" };
            }
        }
    }
}
