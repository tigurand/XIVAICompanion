using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XIVAICompanion.Utils
{
    public static class ExaSearchHelper
    {
        public static async Task<WebSearchResult> SearchAsync(string query, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return new WebSearchResult { Success = false, Text = "Exa API key is missing." };

            try
            {
                using var client = new HttpClient();
                var requestBody = new
                {
                    query = query,
                    type = "auto",
                    numResults = 5,
                    contents = new
                    {
                        text = new { maxCharacters = 1200 }
                    }
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.exa.ai/search")
                {
                    Content = content
                };
                request.Headers.Add("x-api-key", apiKey);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return new WebSearchResult { Success = false, Text = $"Exa search failed with status: {response.StatusCode}" };
                }

                var rawJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(rawJson);
                var results = json["results"] as JArray;

                if (results == null || results.Count == 0)
                {
                    return new WebSearchResult { Success = false, Text = "No search results found (Exa)." };
                }

                var sb = new StringBuilder();
                sb.AppendLine("Search Results (Exa):");
                foreach (var result in results)
                {
                    string title = (string?)result["title"] ?? "No Title";
                    string url = (string?)result["url"] ?? "No URL";

                    string snippet = (string?)result["text"] ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(snippet))
                    {
                        var highlights = result["highlights"] as JArray;
                        if (highlights != null && highlights.Count > 0)
                        {
                            var parts = highlights
                                .Select(h => (string?)h)
                                .Where(s => !string.IsNullOrWhiteSpace(s));
                            snippet = string.Join(" ... ", parts);
                        }
                    }
                    if (string.IsNullOrWhiteSpace(snippet)) snippet = (string?)result["summary"] ?? "No Content";

                    sb.AppendLine($"- {title} ({url}): {snippet}");
                }

                return new WebSearchResult { Success = true, Text = sb.ToString().TrimEnd() };
            }
            catch (Exception ex)
            {
                return new WebSearchResult { Success = false, Text = $"An error occurred during Exa search: {ex.Message}" };
            }
        }
    }
}
