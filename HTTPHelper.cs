/*
 * HTTPHelper.cs - Helper functions for CommonHTTP's HttpClient
 *                 instance.
 * Created on: 16:36 17-04-2022
 * Author    : itsmevjnk
 */

using HRngBackend;
using HtmlAgilityPack;

namespace HRngLite
{
    internal static class HTTPHelper
    {
        /// <summary>
        ///  Send a GET request to an URL, retrieve the resulting content, and parse it as HTML.
        /// </summary>
        /// <param name="url">The URL to send the request to.</param>
        /// <returns>The HtmlDocument object for the resulting content.</returns>
        public static async Task<HtmlDocument> GetRequest(string url)
        {
            HtmlDocument doc = new HtmlDocument();
            var resp = await CommonHTTP.Client.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            doc.LoadHtml(await resp.Content.ReadAsStringAsync());
            return doc;
        }

        /// <summary>
        ///  Send a POST request to an URL, retrieve the resulting content, and parse it as HTML.
        /// </summary>
        /// <param name="url">The URL to send the request to.</param>
        /// <param name="data">A dictionary containing POST request data.</param>
        /// <returns>The HtmlDocument object for the resulting content.</returns>
        public static async Task<HtmlDocument> PostRequest(string url, IDictionary<string, string> data)
        {
            HtmlDocument doc = new HtmlDocument();
            var resp = await CommonHTTP.Client.PostAsync(url, new FormUrlEncodedContent(data));
            resp.EnsureSuccessStatusCode();
            doc.LoadHtml(await resp.Content.ReadAsStringAsync());
            return doc;
        }
    }
}